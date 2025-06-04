using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

[Serializable]
[StructLayout(LayoutKind.Sequential, Size = 56)]
public struct Particle
{
    public float pressure;       // 4
    public float density;        // 8
    public Vector3 currentForce; // 20
    public Vector3 velocity;     // 32
    public Vector3 position;     // 44
    public Vector3 colorVisual;  // 56
}

public class SPH : MonoBehaviour
{
    [Header("General")]
    public Transform collisionSphere;
    public bool showSpheres = true;
    public Vector3Int numToSpawn = new Vector3Int(10, 10, 10);
    private int totalParticles {
        get { return numToSpawn.x * numToSpawn.y * numToSpawn.z; }
    }
    public Vector3 boxSize = new Vector3(4, 10, 3);
    public Vector3 spawnCenter;
    public float particleRadius = 0.1f;
    public float spawnJitter = 0.2f;

    [Header("Particle Rendering")]
    public Mesh particleMesh;
    public float particleRenderSize = 8f;
    public Material material;

    [Header("Compute")]
    public ComputeShader shader;
    public Particle[] particles;

    [Header("Fluid Constants")]
    public float boundDamping = -0.3f;
    public float viscosity = -0.003f;
    public float particleMass = 1f;
    public float gasConstant = 2f;
    public float restingDensity = 1f;
    public float timestep = 0.007f;

    public bool IsActuallyPaused { get; set; } = false;
    private bool executeSingleStepRequest = false;

    public void RequestSingleStep()
    {
        if (IsActuallyPaused)
        {
            executeSingleStepRequest = true;
            Debug.Log("Single step requested for SPH simulation.");
        }
    }

    // Private Variables
    private ComputeBuffer _argsBuffer;
    public ComputeBuffer _particlesBuffer;

    private ComputeBuffer _particleIndices;
    private ComputeBuffer _particleCellIndices;
    private ComputeBuffer _cellOffsets;

    private int integrateKernel;
    private int computeForceKernel;
    private int densityPressureKernel;
    private int hashParticlesKernel;
    private int sortKernel;
    private int computeCellOffsetsKernel;
    private int clearCellOffsetsKernel;

    [Header("Debug")]
    public uint[] particleIdx;
    public uint[] particleCellIdx;
    public uint[] cellOffsets;
    [Header("Color")]
    public bool neighborColor = false;
    public bool pressureColor = false;

    [Header("Label Settings")]
    public int labelFontSize = 16;
    public Color labelColor = Color.white;

    [Header("Particle GameObjects (public array)")]
    public GameObject[] particleObjects;
    private int hoveredParticleIndex = -1;
    private int selectedParticleIndex = -1;

    private static readonly int SizeProperty = Shader.PropertyToID("_size");
    private static readonly int ParticlesBufferProperty = Shader.PropertyToID("_particlesBuffer");

    private void OnDrawGizmos()
    {
        // Draw bounding box
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(Vector3.zero, boxSize);

        if (!Application.isPlaying)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(spawnCenter, 0.1f);
        }
        else if (_particlesBuffer != null && particles != null)
        {
            // Draw velocity vectors via Gizmos
            _particlesBuffer.GetData(particles);
            Gizmos.color = Color.red;
            float arrowLength = particleRadius * 2f;
            for (int i = 0; i < particles.Length; i++)
            {
                Vector3 pos = particles[i].position;
                Vector3 vel = particles[i].velocity;
                if (vel.sqrMagnitude < 1e-6f) continue;
                Gizmos.DrawRay(pos, vel.normalized * arrowLength);
            }
        }
    }

    private void Awake()
    {
        // Ensure mesh + material assigned
        if (particleMesh == null || material == null || shader == null)
        {
            Debug.LogError("Assign Particle Mesh, Material, and ComputeShader in the Inspector.");
            enabled = false;
            return;
        }

        // Initialize particle array
        SpawnParticlesInBox();

        // Setup args buffer for instanced rendering
        uint[] args = {
            particleMesh.GetIndexCount(0),
            (uint)totalParticles,
            particleMesh.GetIndexStart(0),
            particleMesh.GetBaseVertex(0),
            0
        };
        _argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        _argsBuffer.SetData(args);

        // Setup particle buffer (56 bytes per Particle)
        _particlesBuffer = new ComputeBuffer(totalParticles, Marshal.SizeOf(typeof(Particle)));
        _particlesBuffer.SetData(particles);

        // Setup auxiliary buffers for spatial hashing
        _particleIndices = new ComputeBuffer(totalParticles, sizeof(uint));
        _particleCellIndices = new ComputeBuffer(totalParticles, sizeof(uint));
        _cellOffsets = new ComputeBuffer(totalParticles, sizeof(uint));

        particleIdx = new uint[totalParticles];
        particleCellIdx = new uint[totalParticles];
        cellOffsets = new uint[totalParticles];
        for (int i = 0; i < totalParticles; i++) particleIdx[i] = (uint)i;
        _particleIndices.SetData(particleIdx);

        SetupComputeBuffers();

        // Initialize particles once
        shader.Dispatch(integrateKernel, totalParticles / 256, 1, 1);

        CreateParticleGameObjects();
    }

    private void SetupComputeBuffers()
    {
        integrateKernel = shader.FindKernel("Integrate");
        computeForceKernel = shader.FindKernel("ComputeForces");
        densityPressureKernel = shader.FindKernel("ComputeDensityPressure");
        hashParticlesKernel = shader.FindKernel("HashParticles");
        sortKernel = shader.FindKernel("BitonicSort");
        computeCellOffsetsKernel = shader.FindKernel("CalculateCellOffsets");
        clearCellOffsetsKernel = shader.FindKernel("ClearCellOffsets");

        shader.SetInt("particleLength", totalParticles);
        shader.SetFloat("particleMass", particleMass);
        shader.SetFloat("viscosity", viscosity);
        shader.SetFloat("gasConstant", gasConstant);
        shader.SetFloat("restDensity", restingDensity);
        shader.SetFloat("boundDamping", boundDamping);
        shader.SetFloat("pi", Mathf.PI);
        shader.SetVector("boxSize", boxSize);

        shader.SetFloat("radius", particleRadius);
        shader.SetFloat("radius2", particleRadius * particleRadius);
        shader.SetFloat("radius3", particleRadius * particleRadius * particleRadius);
        shader.SetFloat("radius4", particleRadius * particleRadius * particleRadius * particleRadius);
        shader.SetFloat("radius5", particleRadius * particleRadius * particleRadius * particleRadius * particleRadius);

        shader.SetInt("neighborColor", neighborColor ? 1 : 0);
        shader.SetInt("pressureColor", pressureColor ? 1 : 0);

        shader.SetBuffer(integrateKernel, "_particles", _particlesBuffer);

        shader.SetBuffer(computeForceKernel, "_particles", _particlesBuffer);
        shader.SetBuffer(computeForceKernel, "particleIndices", _particleIndices);
        shader.SetBuffer(computeForceKernel, "particleCellIndices", _particleCellIndices);
        shader.SetBuffer(computeForceKernel, "cellOffsets", _cellOffsets);

        shader.SetBuffer(densityPressureKernel, "_particles", _particlesBuffer);
        shader.SetBuffer(densityPressureKernel, "particleIndices", _particleIndices);
        shader.SetBuffer(densityPressureKernel, "particleCellIndices", _particleCellIndices);
        shader.SetBuffer(densityPressureKernel, "cellOffsets", _cellOffsets);

        shader.SetBuffer(hashParticlesKernel, "_particles", _particlesBuffer);
        shader.SetBuffer(hashParticlesKernel, "particleIndices", _particleIndices);
        shader.SetBuffer(hashParticlesKernel, "particleCellIndices", _particleCellIndices);

        shader.SetBuffer(clearCellOffsetsKernel, "cellOffsets", _cellOffsets);

        shader.SetBuffer(computeCellOffsetsKernel, "cellOffsets", _cellOffsets);
        shader.SetBuffer(computeCellOffsetsKernel, "particleIndices", _particleIndices);
        shader.SetBuffer(computeCellOffsetsKernel, "particleCellIndices", _particleCellIndices);

        shader.SetBuffer(sortKernel, "particleIndices", _particleIndices);
        shader.SetBuffer(sortKernel, "particleCellIndices", _particleCellIndices);
    }

    public void SortParticles()
    {
        int count = totalParticles;
        for (int dim = 2; dim <= count; dim <<= 1)
        {
            shader.SetInt("dim", dim);
            for (int block = dim >> 1; block > 0; block >>= 1)
            {
                shader.SetInt("block", block);
                shader.Dispatch(sortKernel, count / 256, 1, 1);
            }
        }
    }

    private void FixedUpdate()
    {
        if (IsActuallyPaused && !executeSingleStepRequest)
            return;
        executeSingleStepRequest = false;

        shader.SetVector("boxSize", boxSize);
        shader.SetFloat("timestep", timestep);
        shader.SetFloat("viscosity", viscosity);
        shader.SetFloat("gasConstant", gasConstant);
        shader.SetFloat("particleMass", particleMass);
        shader.SetFloat("restDensity", restingDensity);

        if (collisionSphere != null)
        {
            shader.SetVector("spherePos", collisionSphere.transform.position);
            shader.SetFloat("sphereRadius", collisionSphere.transform.localScale.x / 2);
        }

        shader.Dispatch(clearCellOffsetsKernel, totalParticles / 256, 1, 1);
        shader.Dispatch(hashParticlesKernel, totalParticles / 256, 1, 1);

        SortParticles();

        shader.Dispatch(computeCellOffsetsKernel, totalParticles / 256, 1, 1);

        shader.Dispatch(densityPressureKernel, totalParticles / 256, 1, 1);
        shader.Dispatch(computeForceKernel, totalParticles / 256, 1, 1);
        shader.Dispatch(integrateKernel, totalParticles / 256, 1, 1);
    }

    private void SpawnParticlesInBox()
    {
        Vector3 spawnPoint = spawnCenter;
        var tempList = new List<Particle>(totalParticles);

        for (int x = 0; x < numToSpawn.x; x++)
        {
            for (int y = 0; y < numToSpawn.y; y++)
            {
                for (int z = 0; z < numToSpawn.z; z++)
                {
                    Vector3 spawnPos = spawnPoint + new Vector3(
                        x * particleRadius * 3f,
                        y * particleRadius * 3f,
                        z * particleRadius * 3f
                    );
                    spawnPos += UnityEngine.Random.onUnitSphere * particleRadius * spawnJitter;

                    Particle p = new Particle
                    {
                        position = spawnPos,
                        velocity = Vector3.zero,
                        currentForce = Vector3.zero,
                        density = 0f,
                        pressure = 0f,
                        colorVisual = Vector3.one
                    };
                    tempList.Add(p);
                }
            }
        }
        particles = tempList.ToArray();
    }

    private void CreateParticleGameObjects()
    {
        // Expose each particle as a public GameObject
        particleObjects = new GameObject[totalParticles];
        for (int i = 0; i < totalParticles; i++)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.parent = transform;
            go.name = $"Particle_{i}";
            go.transform.position = particles[i].position;
            go.transform.localScale = Vector3.one * (particleRadius * 2f);

            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null && material != null)
            {
                mr.material = material;
            }

            SphereCollider sc = go.GetComponent<SphereCollider>();
            if (sc != null) sc.isTrigger = true;

            ParticleInfo info = go.AddComponent<ParticleInfo>();
            info.index = i;
            info.sph = this;

            particleObjects[i] = go;
        }
    }

    private void Update()
    {
        if (_particlesBuffer == null) return;

        // Update buffer data to CPU array
        _particlesBuffer.GetData(particles);

        // Move each GameObject to its new position
        for (int i = 0; i < totalParticles; i++)
        {
            if (particleObjects != null && particleObjects[i] != null)
                particleObjects[i].transform.position = particles[i].position;
        }

        // Instanced rendering (if enabled)
        if (showSpheres && particleMesh != null && material != null && _argsBuffer != null)
        {
            material.SetFloat(SizeProperty, particleRenderSize);
            material.SetBuffer(ParticlesBufferProperty, _particlesBuffer);
            Graphics.DrawMeshInstancedIndirect(
                particleMesh,
                0,
                material,
                new Bounds(Vector3.zero, boxSize),
                _argsBuffer,
                castShadows: UnityEngine.Rendering.ShadowCastingMode.Off
            );
        }
    }

    private void OnGUI()
    {
        int toShow = (hoveredParticleIndex >= 0) ? hoveredParticleIndex : selectedParticleIndex;
        if (toShow >= 0 && toShow < totalParticles && particleObjects != null && particleObjects[toShow] != null)
        {
            Vector3 worldPos = particleObjects[toShow].transform.position;
            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = labelFontSize;
            style.normal.textColor = labelColor;

            GUI.Label(
                new Rect(screenPos.x + 10f, Screen.height - screenPos.y + 10f, 200f, 30f),
                $"Particle {toShow}",
                style
            );
        }
    }

    public void SelectParticle(int index)
    {
        if (index < 0 || index >= totalParticles) return;
        selectedParticleIndex = index;
    }

    public void HoverParticle(int index, bool isHovering)
    {
        if (isHovering) hoveredParticleIndex = index;
        else if (hoveredParticleIndex == index) hoveredParticleIndex = -1;
    }

    private void OnDestroy()
    {
        if (_particlesBuffer != null)
        {
            _particlesBuffer.Release();
            _particlesBuffer = null;
        }
        if (_argsBuffer != null)
        {
            _argsBuffer.Release();
            _argsBuffer = null;
        }
        if (_particleIndices != null)
        {
            _particleIndices.Release();
            _particleIndices = null;
        }
        if (_particleCellIndices != null)
        {
            _particleCellIndices.Release();
            _particleCellIndices = null;
        }
        if (_cellOffsets != null)
        {
            _cellOffsets.Release();
            _cellOffsets = null;
        }
    }
}

public class ParticleInfo : MonoBehaviour
{
    public int index;
    public SPH sph;

    private void OnMouseEnter()
    {
        sph?.HoverParticle(index, true);
    }

    private void OnMouseExit()
    {
        sph?.HoverParticle(index, false);
    }

    private void OnMouseDown()
    {
        sph?.SelectParticle(index);
    }
}
