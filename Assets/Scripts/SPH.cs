using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;

[System.Serializable]
[StructLayout(LayoutKind.Sequential, Size = 56)]
public struct Particle
{
    public float pressure; // 4
    public float density; // 8
    public Vector3 currentForce; // 20
    public Vector3 velocity; // 32
    public Vector3 position; // 44 
    public Vector3 colorVisual; // 56
}

public class SPH : MonoBehaviour
{
    [Header("General")]
    public Transform collisionSphere;
    public bool showSpheres = true;
    public Vector3Int numToSpawn = new Vector3Int(10,10,10);
    private int totalParticles {
        get {
            return numToSpawn.x*numToSpawn.y*numToSpawn.z;
        }
    }
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

    private void OnDrawGizmos()
    {

        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(Vector3.zero, boxSize);

        if (!Application.isPlaying)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(spawnCenter, 0.1f);
        }

    }

    [Header("Debug")]
    public uint[] particleIdx;
    public uint[] particleCellIdx;
    public uint[] cellOffsets;
    [Header("Color")]
    public bool coloring = false;
    public bool pressureColor = false;

    private void Awake()
    {

        SpawnParticlesInBox(); // Spawn Particles

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
        _particlesBuffer = new ComputeBuffer(totalParticles, 56);
        _particlesBuffer.SetData(particles);

        _particleIndices = new ComputeBuffer(totalParticles, 4);
        _particleCellIndices = new ComputeBuffer(totalParticles, 4);
        _cellOffsets = new ComputeBuffer(totalParticles, 4);


        cellOffsets = new uint[totalParticles];
        particleCellIdx = new uint[totalParticles];
        particleIdx = new uint[totalParticles];


        for (int i = 0; i < particleIdx.Length; i++) particleIdx[i] = (uint)i;

        _particleIndices.SetData(particleIdx);

        //calcualte starting mass based on M/N

        SetupComputeBuffers();

        shader.Dispatch(integrateKernel, totalParticles / 256, 1, 1); // Initialize particles

    }

    public void SortParticles() {
        var count = totalParticles;
        
        for (var dim = 2; dim <= count; dim <<= 1){
            shader.SetInt("dim", dim);
            for (var block = dim >> 1; block > 0; block >>= 1){
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

        shader.SetFloat("radius", particleRadius);
        shader.SetFloat("radius2", particleRadius * particleRadius);
        shader.SetFloat("radius3", particleRadius * particleRadius * particleRadius);
        shader.SetFloat("radius4", particleRadius * particleRadius * particleRadius * particleRadius);
        shader.SetFloat("radius5", particleRadius * particleRadius * particleRadius * particleRadius * particleRadius);

        shader.SetInt("neighborColor", coloring ? 1 : 0);
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



    private void FixedUpdate() {

        shader.SetVector("boxSize", boxSize);
        shader.SetFloat("timestep", timestep);
        shader.SetFloat("viscosity", viscosity);
        shader.SetFloat("gasConstant", gasConstant);
        shader.SetFloat("particleMass", particleMass);
        shader.SetFloat("restDensity", restingDensity);

        shader.SetVector("spherePos", collisionSphere.transform.position);
        shader.SetFloat("sphereRadius", collisionSphere.transform.localScale.x/2);
        shader.Dispatch(clearCellOffsetsKernel, totalParticles / 256, 1, 1);
        // Total Particles has to be divisible by 2 for bitonic sort to work, 256 good for warps 
        shader.Dispatch(hashParticlesKernel, totalParticles / 256, 1, 1); // 0. Hash Particles to cells

        SortParticles(); // Sort Particles by cell index

        shader.Dispatch(computeCellOffsetsKernel, totalParticles / 256, 1, 1); // 0. Calculate cell offsets

        // int groups = Mathf.Max(1, totalParticles / 100);

        shader.Dispatch(densityPressureKernel, totalParticles / 256, 1, 1); // 1. Compute Density/Pressure for each particle
        shader.Dispatch(computeForceKernel, totalParticles / 256, 1, 1); // 2. Use Density/Pressure to calculate forces
        shader.Dispatch(integrateKernel, totalParticles / 256, 1, 1); // 3. Use forces to move particles
    }


    private void SpawnParticlesInBox() {

        Vector3 spawnPoint = spawnCenter;
        List<Particle> _particles = new List<Particle>();

        for (int x = 0; x < numToSpawn.x; x++) {
            for (int y = 0; y < numToSpawn.y; y++) {
                for (int z = 0; z < numToSpawn.z; z++) {

                    Vector3 spawnPos = spawnPoint + new Vector3(x*particleRadius*3, y*particleRadius*3, z*particleRadius*3);

                    // Randomize spawning position a little bit for more convincing simulation
                    spawnPos += Random.onUnitSphere * particleRadius * spawnJitter; 

                    Particle p = new Particle {
                        position = spawnPos
                    };

                    _particles.Add(p);
                }
            }
        }

        particles = _particles.ToArray();

    }


    private static readonly int SizeProperty = Shader.PropertyToID("_size");
    private static readonly int ParticlesBufferProperty = Shader.PropertyToID("_particlesBuffer");



    private void Update()
    {

        // Render the particles
        material.SetFloat(SizeProperty, particleRenderSize);
        material.SetBuffer(ParticlesBufferProperty, _particlesBuffer);

        if (showSpheres)
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