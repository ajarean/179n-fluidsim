// using UnityEngine;
// using System.Collections.Generic;
// using System.Runtime.InteropServices;

// [System.Serializable]
// [StructLayout(LayoutKind.Sequential, Size = 44)]
// public struct Particle
// {
//     public float pressure;
//     public float density;
//     public Vector3 force;
//     public Vector3 velocity;
//     public Vector3 position;
// }

// public class FluidSimulator : MonoBehaviour
// {
//     [Header("General")]
//     public int particleCount = 300;
//     public Vector3 boxSize = new(6, 5, 6);
//     public float particleRadius = 0.1f;
//     public Vector3 gravity = new(0, -9.81f, 0);

//     [Header("Fluid Constants")]
//     public float restDensity = 300f;
//     public float gasConstant = 500f;
//     public float viscosity = 10f;
//     public float particleMass = 0.02f;
//     public float smoothingRadius = 0.3f;
//     public float boundDamping = -0.3f;

//     [Header("Rendering")]
//     public Mesh particleMesh;
//     public Material material;
//     public float particleRenderSize = 16f;

//     private Particle[] particles;
//     private ComputeBuffer particleBuffer;
//     private ComputeBuffer argsBuffer;

//     private static readonly int SizeID = Shader.PropertyToID("_size");
//     private static readonly int BufferID = Shader.PropertyToID("_particlesBuffer");

//     void Start()
//     {
//         InitParticles();
//         InitRendering();
//     }

//     void InitParticles()
//     {
//         particles = new Particle[particleCount];

//         for (int i = 0; i < particleCount; i++)
//         {
//             Vector3 pos = new Vector3(
//                 Random.Range(-1.5f, 1.5f),
//                 Random.Range(1.0f, 3.0f),
//                 Random.Range(-1.5f, 1.5f)
//             );

//             particles[i] = new Particle { position = pos };
//         }

//         particleBuffer = new ComputeBuffer(particleCount, 44);
//         particleBuffer.SetData(particles);
//     }

//     void InitRendering()
//     {
//         uint[] args = new uint[5] {
//             (uint)particleMesh.GetIndexCount(0),
//             (uint)particleCount,
//             (uint)particleMesh.GetIndexStart(0),
//             (uint)particleMesh.GetBaseVertex(0),
//             0
//         };

//         argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
//         argsBuffer.SetData(args);
//     }

//     void FixedUpdate()
//     {
//         Simulate();
//         particleBuffer.SetData(particles); // Update GPU buffer
//     }

//     void Simulate()
//     {
//         float h2 = smoothingRadius * smoothingRadius;
//         float poly6 = 315f / (64f * Mathf.PI * Mathf.Pow(smoothingRadius, 9));
//         float spikyGrad = -45f / (Mathf.PI * Mathf.Pow(smoothingRadius, 6));
//         float viscLaplacian = 45f / (Mathf.PI * Mathf.Pow(smoothingRadius, 6));

//         // Density + Pressure
//         for (int i = 0; i < particles.Length; i++)
//         {
//             particles[i].density = 0;

//             for (int j = 0; j < particles.Length; j++)
//             {
//                 Vector3 rij = particles[j].position - particles[i].position;
//                 float r2 = rij.sqrMagnitude;

//                 if (r2 < h2)
//                 {
//                     float diff = h2 - r2;
//                     particles[i].density += particleMass * poly6 * diff * diff * diff;
//                 }
//             }

//             particles[i].pressure = gasConstant * (particles[i].density - restDensity);
//         }

//         // Force
//         for (int i = 0; i < particles.Length; i++)
//         {
//             Vector3 force = Vector3.zero;

//             for (int j = 0; j < particles.Length; j++)
//             {
//                 if (i == j) continue;

//                 Vector3 rij = particles[j].position - particles[i].position;
//                 float r = rij.magnitude;

//                 if (r < smoothingRadius && r > 1e-5f)
//                 {
//                     Vector3 dir = rij / r;
//                     float avgPressure = (particles[i].pressure + particles[j].pressure) / 2f;

//                     force += -dir * particleMass * avgPressure / particles[j].density * Mathf.Pow(smoothingRadius - r, 2) * spikyGrad;

//                     Vector3 velDiff = particles[j].velocity - particles[i].velocity;
//                     force += viscosity * particleMass * velDiff / particles[j].density * viscLaplacian * (smoothingRadius - r);
//                 }
//             }

//             force += gravity * particleMass;
//             particles[i].force = force;
//         }

//         // Integrate
//         Vector3 min = -boxSize / 2;
//         Vector3 max = boxSize / 2;

//         for (int i = 0; i < particles.Length; i++)
//         {
//             Vector3 accel = particles[i].force / particles[i].density;
//             particles[i].velocity += accel * Time.fixedDeltaTime;
//             particles[i].position += particles[i].velocity * Time.fixedDeltaTime;

//             for (int axis = 0; axis < 3; axis++)
//             {
//                 if (particles[i].position[axis] - particleRadius < min[axis])
//                 {
//                     particles[i].velocity[axis] *= boundDamping;
//                     particles[i].position[axis] = min[axis] + particleRadius;
//                 }
//                 if (particles[i].position[axis] + particleRadius > max[axis])
//                 {
//                     particles[i].velocity[axis] *= boundDamping;
//                     particles[i].position[axis] = max[axis] - particleRadius;
//                 }
//             }
//         }
//     }

//     void Update()
//     {
//         material.SetFloat(SizeID, particleRenderSize);
//         material.SetBuffer(BufferID, particleBuffer);

//         Graphics.DrawMeshInstancedIndirect(
//             particleMesh,
//             0,
//             material,
//             new Bounds(Vector3.zero, boxSize),
//             argsBuffer,
//             castShadows: UnityEngine.Rendering.ShadowCastingMode.Off
//         );
//     }

//     void OnDestroy()
//     {
//         particleBuffer?.Release();
//         argsBuffer?.Release();
//     }
// }
