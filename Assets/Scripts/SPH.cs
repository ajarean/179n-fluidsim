using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using System.Runtime.InteropServices;

[System.Serializable]
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
    public Vector3Int numToSpawn = new Vector3Int(10,10,10);
    private int totalParticles => numToSpawn.x * numToSpawn.y * numToSpawn.z;
    public Vector3 boxSize = new Vector3(4,10,3);
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

    [Header("Tooltip Settings")]
    [Tooltip("Scale multiplier for drawing force vectors")]
    public float forceVectorScale = 0.1f;
    [Tooltip("Pixel radius for hover detection on screen")]
    public float hoverPixelRadius = 10f;

    // GPU Buffers
    private ComputeBuffer _argsBuffer;
    private ComputeBuffer _particlesBuffer;
    private ComputeBuffer _particleIndices;
    private ComputeBuffer _particleCellIndices;
    private ComputeBuffer _cellOffsets;

    // Kernels
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

    // Tooltip storage
    private struct Tooltip { public Vector3 screenPos; public string text; }
    private List<Tooltip> _labels = new List<Tooltip>();

    private void Awake()
    {
        SpawnParticlesInBox();

        // Indirect args
        uint[] args = new uint[5] {
            particleMesh.GetIndexCount(0),
            (uint)totalParticles,
            particleMesh.GetIndexStart(0),
            particleMesh.GetBaseVertex(0),
            0
        };
        _argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        _argsBuffer.SetData(args);

        // Particle buffer
        _particlesBuffer = new ComputeBuffer(totalParticles, Marshal.SizeOf(typeof(Particle)));
        _particlesBuffer.SetData(particles);

        _particleIndices = new ComputeBuffer(totalParticles, sizeof(uint));
        _particleCellIndices = new ComputeBuffer(totalParticles, sizeof(uint));
        _cellOffsets = new ComputeBuffer(totalParticles, sizeof(uint));

        // Fill indices
        particleIdx = new uint[totalParticles];
        for (int i = 0; i < totalParticles; i++) particleIdx[i] = (uint)i;
        _particleIndices.SetData(particleIdx);

        SetupComputeBuffers();
        shader.Dispatch(integrateKernel, totalParticles / 256, 1, 1);
    }

    private void FixedUpdate()
    {
        if (IsActuallyPaused && !executeSingleStepRequest) return;

        // Update uniforms
        shader.SetVector("boxSize", boxSize);
        shader.SetFloat("timestep", timestep);
        shader.SetFloat("viscosity", viscosity);
        shader.SetFloat("gasConstant", gasConstant);
        shader.SetFloat("particleMass", particleMass);
        shader.SetFloat("restDensity", restingDensity);
        shader.SetVector("spherePos", collisionSphere.position);
        shader.SetFloat("sphereRadius", collisionSphere.localScale.x * 0.5f);

        // SPH steps
        shader.Dispatch(clearCellOffsetsKernel, totalParticles / 256, 1, 1);
        shader.Dispatch(hashParticlesKernel, totalParticles / 256, 1, 1);
        SortParticles();
        shader.Dispatch(computeCellOffsetsKernel, totalParticles / 256, 1, 1);
        shader.Dispatch(densityPressureKernel, totalParticles / 256, 1, 1);
        shader.Dispatch(computeForceKernel, totalParticles / 256, 1, 1);
        shader.Dispatch(integrateKernel, totalParticles / 256, 1, 1);

        executeSingleStepRequest = false;

        RequestPressureReadback();
    }

    private void RequestPressureReadback()
    {
        AsyncGPUReadback.Request(_particlesBuffer, OnReadbackComplete);
    }

    private void OnReadbackComplete(AsyncGPUReadbackRequest req)
    {
        if (req.hasError)
        {
            Debug.LogError("GPU readback error");
            return;
        }
        var cpuParticles = req.GetData<Particle>();
        ShowTooltips(cpuParticles);
    }

    private void ShowTooltips(NativeArray<Particle> parts)
{
    Camera cam = Camera.main;
    Vector2 mousePos = Input.mousePosition;
    // Find the single nearest particle under the cursor
    bool found = false;
    float minDist = hoverPixelRadius;
    Particle nearestP = default;
    Vector3 nearestSp = Vector3.zero;
    foreach (var p in parts)
    {
        Vector3 worldPos = p.position;
        Vector3 sp = cam.WorldToScreenPoint(worldPos);
        if (sp.z > 0)
        {
            float dist = Vector2.Distance(new Vector2(sp.x, sp.y), mousePos);
            if (dist <= hoverPixelRadius && dist < minDist)
            {
                found = true;
                minDist = dist;
                nearestP = p;
                nearestSp = sp;
            }
        }
    }
    if (found)
    {
        _labels.Add(new Tooltip { screenPos = nearestSp, text = nearestP.pressure.ToString("F2") });
        Debug.DrawRay(nearestP.position, nearestP.currentForce * forceVectorScale, Color.red);
    }
}

private void OnGUI()
    {
        foreach (var lbl in _labels)
        {
            Rect r = new Rect(lbl.screenPos.x, Screen.height - lbl.screenPos.y, 60, 20);
            GUI.Label(r, lbl.text);
        }
        _labels.Clear();
    }

    private void SortParticles()
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

        float r = particleRadius;
        shader.SetFloat("radius", r);
        shader.SetFloat("radius2", r*r);
        shader.SetFloat("radius3", r*r*r);
        shader.SetFloat("radius4", r*r*r*r);
        shader.SetFloat("radius5", r*r*r*r*r);

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
        shader.SetBuffer(hashParticlesKernel, "particleIndices", _particleIndices);
        shader.SetBuffer(hashParticlesKernel, "particleCellIndices", _particleCellIndices);
        shader.SetBuffer(hashParticlesKernel, "_particles", _particlesBuffer);
        shader.SetBuffer(clearCellOffsetsKernel, "cellOffsets", _cellOffsets);
        shader.SetBuffer(computeCellOffsetsKernel, "cellOffsets", _cellOffsets);
        shader.SetBuffer(computeCellOffsetsKernel, "particleIndices", _particleIndices);
        shader.SetBuffer(computeCellOffsetsKernel, "particleCellIndices", _particleCellIndices);
        shader.SetBuffer(sortKernel, "particleIndices", _particleIndices);
        shader.SetBuffer(sortKernel, "particleCellIndices", _particleCellIndices);
    }

    private void SpawnParticlesInBox()
    {
        List<Particle> list = new List<Particle>(totalParticles);
        for (int x = 0; x < numToSpawn.x; x++)
            for (int y = 0; y < numToSpawn.y; y++)
                for (int z = 0; z < numToSpawn.z; z++)
                {
                    Vector3 pos = spawnCenter + new Vector3(x, y, z) * particleRadius * 3f;
                    pos += Random.onUnitSphere * particleRadius * spawnJitter;
                    list.Add(new Particle { position = pos });
                }
        particles = list.ToArray();
    }

    private static readonly int SizeProperty = Shader.PropertyToID("_size");
    private static readonly int ParticlesBufferProperty = Shader.PropertyToID("_particlesBuffer");

    private void Update()
    {
        material.SetFloat(SizeProperty, particleRenderSize);
        material.SetBuffer(ParticlesBufferProperty, _particlesBuffer);
        if (showSpheres)
            Graphics.DrawMeshInstancedIndirect(
                particleMesh, 0, material,
                new Bounds(Vector3.zero, boxSize),
                _argsBuffer,
                castShadows: UnityEngine.Rendering.ShadowCastingMode.Off
            );
    }
}
