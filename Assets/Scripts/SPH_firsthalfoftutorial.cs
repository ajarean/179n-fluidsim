// using System.Collections;
// using System.Collections.Generic;
// using UnityEngine;
// using System.Runtime.InteropServices;

// [System.Serializable]
// [StructLayout(LayoutKind.Sequential)]
// public struct Particle {
//     public float pressure;          // 4
//     public float density;           // 8
//     public Vector3 currentForce;    // 20
//     public Vector3 velocity;        // 32
//     public Vector3 position;        // 44 total bytes
// }

// public class SPH : MonoBehaviour
// {
//     [Header("General")]
//     public bool showSpheres = true;
//     public Vector3Int numToSpawn = new Vector3Int(10, 10, 10);
//     private int totalParticles
//     {
//         get
//         {
//             return numToSpawn.x * numToSpawn.y * numToSpawn.z;
//         }
//     }

//     public Vector3 boxSize = new Vector3(4, 10, 3);
//     public Vector3 spawnCenter;
//     public float particleRadius = 0.1f;

//     [Header("Particle Rendering")]
//     public Mesh particleMesh;
//     public float particleRenderSize = 8f;
//     public Material material;

//     [Header("Compute")]
//     public ComputeShader shader;
//     public Particle[] particles;

//     // Private Variables
//     private ComputeBuffer _argsBuffer;
//     private ComputeBuffer _particlesBuffer;

//     private void Awake() {
//         // Setup args for Instanced Particle Rendering
//         uint[] args = {
//             particleMesh.GetIndexCount(0),
//             (uint)totalParticles,
//             particleMesh.GetIndexStart(0),
//             particleMesh.GetBaseVertex(0),
//         };

//         _argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
//         _argsBuffer.SetData(args);

//         // Setup Particle Buffer
//         _particlesBuffer = new ComputeBuffer(totalParticles, 44);
//         _particlesBuffer.SetData(particles);
//     }


//     private void SpawnParticlesInBox() {
//         Vector3 spawnPoint = spawnCenter;
//         List<Particle> _particles = new List<Particle>();

//         for (int x = 0; x < numToSpawn.x; x++) {
//             for (int y = 0; y < numToSpawn.y; y++) {
//                 for (int z = 0; z < numToSpawn.z; z++) {
//                     Vector3 spawnPos = spawnPoint + new Vector3(x * particleRadius * 2, y * particleRadius * 2, z * particleRadius * 2);
//                     Particle p = new Particle {
//                         position = spawnPos
//                     };
//                     _particles.Add(p);
//                 }
//             }
//         }

//         particles = _particles.ToArray();
//     }

//     private void OnDrawGizmos() {
//         Gizmos.color = Color.blue;
//         Gizmos.DrawWireCube(Vector3.zero, boxSize);

//         if(!Application.isPlaying) {
//             Gizmos.color = Color.cyan;
//             Gizmos.DrawWireSphere(spawnCenter, 0.1f);
//         }
//     }

//     private static readonly int SizeProperty = Shader.PropertyToID("_size");
//     private static readonly int ParticlesBufferProperty = Shader.PropertyToID("_particlesBuffer");

//     private void Update() {
//         // Render the particles
//         material.SetFloat(SizeProperty, particleRenderSize);
//         material.SetBuffer(ParticlesBufferProperty, _particlesBuffer);

//         if (showSpheres) {
//             Graphics.DrawMeshInstancedIndirect(
//                 particleMesh,
//                 0,
//                 material,
//                 new Bounds(Vector3.zero, boxSize),
//                 _argsBuffer,
//                 castShadows: UnityEngine.Rendering.ShadowCastingMode.Off
//             );
//         }
//     }
// }
