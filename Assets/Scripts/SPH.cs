using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

[Serializable]
[StructLayout(LayoutKind.Sequential, Size = 44)]
public struct Particle
{
    public float pressure;      // 4
    public float density;       // 8
    public Vector3 currentForce; // 20
    public Vector3 velocity;     // 32
    public Vector3 position;     // 44 total bytes
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
    public Material material; // assign your desired material here

    [Header("Label Settings")]
    public int labelFontSize = 16;
    public Color labelColor = Color.white;

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
    private int calculateCellOffsetsKernel;

    // For hover/selection
    private List<GameObject> particleObjects;
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
        else if (_particlesBuffer != null)
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
        SpawnParticlesInBox();

        // Setup Args for Instanced Particle Rendering
        uint[] args = {
            particleMesh.GetIndexCount(0),
            (uint)totalParticles,
            particleMesh.GetIndexStart(0),
            particleMesh.GetBaseVertex(0),
            0
        };
        _argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        _argsBuffer.SetData(args);

        // Setup Particle Buffer
        _particlesBuffer = new ComputeBuffer(totalParticles, Marshal.SizeOf(typeof(Particle)));
        _particlesBuffer.SetData(particles);

        _particleIndices = new ComputeBuffer(totalParticles, 4);
        _particleCellIndices = new ComputeBuffer(totalParticles, 4);
        _cellOffsets = new ComputeBuffer(totalParticles, 4);

        uint[] particleIndices = new uint[totalParticles];

        for (int i = 0; i < particleIndices.Length; i++) particleIndices[i] = (uint)i;

        _particleIndices.SetData(particleIndices);

        //calcualte starting mass based on M/N

        SetupComputeBuffers();
        CreateParticleGameObjects();
    }

    private void SetupComputeBuffers()
    {
        integrateKernel = shader.FindKernel("Integrate");
        computeForceKernel = shader.FindKernel("ComputeForces");
        densityPressureKernel = shader.FindKernel("ComputeDensityPressure");
        hashParticlesKernel = shader.FindKernel("HashParticles");
        sortKernel = shader.FindKernel("BitonicSort");
        calculateCellOffsetsKernel = shader.FindKernel("CalculateCellOffsets");

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

        shader.SetBuffer(integrateKernel, "_particles", _particlesBuffer);
        shader.SetBuffer(computeForceKernel, "_particles", _particlesBuffer);
        shader.SetBuffer(densityPressureKernel, "_particles", _particlesBuffer);
        shader.SetBuffer(hashParticlesKernel, "_particles", _particlesBuffer);

        shader.SetBuffer(computeForceKernel, "_particleIndices", _particleIndices);
        shader.SetBuffer(densityPressureKernel, "_particleIndices", _particleIndices);
        shader.SetBuffer(hashParticlesKernel, "_particleIndices", _particleIndices);
        shader.SetBuffer(sortKernel, "_particleIndices", _particleIndices);
        shader.SetBuffer(calculateCellOffsetsKernel, "_particleIndices", _particleIndices);

        shader.SetBuffer(computeForceKernel, "_particleCellIndices", _particleCellIndices);
        shader.SetBuffer(densityPressureKernel, "_particleCellIndices", _particleCellIndices);
        shader.SetBuffer(hashParticlesKernel, "_particleCellIndices", _particleCellIndices);
        shader.SetBuffer(sortKernel, "_particleCellIndices", _particleCellIndices);
        shader.SetBuffer(calculateCellOffsetsKernel, "_particleCellIndices", _particleCellIndices);

        shader.SetBuffer(computeForceKernel, "_cellOffsets", _cellOffsets);
        shader.SetBuffer(hashParticlesKernel, "_cellOffsets", _cellOffsets);
        shader.SetBuffer(densityPressureKernel, "_cellOffsets", _cellOffsets);
        shader.SetBuffer(calculateCellOffsetsKernel, "_cellOffsets", _cellOffsets);

    }

    private void SortParticles() {

        for (var dim = 2; dim <= totalParticles; dim <<= 1) {
            shader.SetInt("dim", dim);
            for (var block = dim >> 1; block > 0; block >>= 1) {
                shader.SetInt("block", block);
                shader.Dispatch(sortKernel, totalParticles/256, 1, 1);
            }
        }
    }

    private void FixedUpdate()
    {
        shader.SetVector("boxSize", boxSize);
        shader.SetFloat("timestep", timestep);
        shader.SetVector("spherePos", collisionSphere.transform.position);
        shader.SetFloat("sphereRadius", collisionSphere.transform.localScale.x / 2f);

        // Total Particles has to be divisible by 256 
        shader.Dispatch(hashParticlesKernel, totalParticles / 256, 1, 1); // 0. Hash Particles to cells

        SortParticles(); // Sort Particles by cell index

        shader.Dispatch(calculateCellOffsetsKernel, totalParticles / 256, 1, 1); // 0. Calculate cell offsets

        int groups = Mathf.Max(1, totalParticles / 100);

        shader.Dispatch(densityPressureKernel, totalParticles / 256, 1, 1); // 1. Compute Density/Pressure for each particle
        shader.Dispatch(computeForceKernel, totalParticles / 256, 1, 1); // 2. Use Density/Pressure to calculate forces
        shader.Dispatch(integrateKernel, totalParticles / 256, 1, 1); // 3. Use forces to move particles
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
                        x * particleRadius * 2f,
                        y * particleRadius * 2f,
                        z * particleRadius * 2f
                    );
                    spawnPos += UnityEngine.Random.onUnitSphere * particleRadius * spawnJitter;

                    Particle p = new Particle
                    {
                        position = spawnPos,
                        velocity = Vector3.zero,
                        currentForce = Vector3.zero,
                        density = 0f,
                        pressure = 0f
                    };
                    tempList.Add(p);
                }
            }
        }
        particles = tempList.ToArray();
    }

    private void CreateParticleGameObjects()
    {
        particleObjects = new List<GameObject>(totalParticles);
        for (int i = 0; i < particles.Length; i++)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.parent = transform;
            go.name = $"Particle_{i}";
            go.transform.position = particles[i].position;
            go.transform.localScale = Vector3.one * (particleRadius * 2f);

            // Assign the chosen material to each GameObject so it shows up:
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && material != null)
            {
                mr.material = material;
            }

            var collider = go.GetComponent<SphereCollider>();
            if (collider != null) collider.isTrigger = true;

            var info = go.AddComponent<ParticleInfo>();
            info.index = i;
            info.sph = this;

            particleObjects.Add(go);
        }
    }

    private void Update()
    {
        if (_particlesBuffer == null) return;

        // Update GPU → CPU data
        _particlesBuffer.GetData(particles);

        // Move GameObjects for hover/click
        for (int i = 0; i < particles.Length; i++)
        {
            if (particleObjects != null && i < particleObjects.Count && particleObjects[i] != null)
            {
                particleObjects[i].transform.position = particles[i].position;
            }
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
    }

    private void OnGUI()
    {
        int toShow = (hoveredParticleIndex >= 0) ? hoveredParticleIndex : selectedParticleIndex;
        if (toShow >= 0 && toShow < particles.Length && particleObjects != null)
        {
            Vector3 worldPos = particleObjects[toShow].transform.position;
            Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);

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

    // Called by ParticleInfo when a particle is clicked
    public void SelectParticle(int index)
    {
        if (index < 0 || index >= particles.Length) return;
        selectedParticleIndex = index;
    }

    // Called by ParticleInfo when mouse enters/exits
    public void HoverParticle(int index, bool isHovering)
    {
        if (isHovering) hoveredParticleIndex = index;
        else if (hoveredParticleIndex == index) hoveredParticleIndex = -1;
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
