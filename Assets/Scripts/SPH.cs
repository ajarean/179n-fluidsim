using UnityEngine;
using System.Collections.Generic;
using System.Generic;


public class SPH : MonoBehaviour {
  [System.Serializable]
  public struct particle{
      public Vector3 position;
      public Vector3 velocity;
      public Vector3 force;
      public float density;
      public float pressure;
    }

    public Vector3 box = new Vector3(1, 1, 1);
    public Vector3 spawnBox = new Vector3(1, 1, 1);
    public vector3 spawnPosition = new Vector3(0, 0, 0);

    public Vector3 gravity = new Vector3(0, -9.81f, 0);
    public float smoothingRadius = 0.3f;
    public float radius = 0.1f;
    public bool showSpheres = true;
    public bool wireSpheres = false;

    public float particleCount = 300;

    public float timeStep = 0.0001f;

    private List<particle> particles = new List<particle>();

    private void onDrawGizmos() {
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(transform.position, box);
       
        if(Applicaitno.isPlaying){
            Gizmos.color = Color.pink;
            Gizmos.DrawWireCube(spawnPosition, spawnBox);
        } else {
            foreach (var p in particles) {
                Gizmos.color = Color.white;
                Gizmos.DrawSphere(p.position, radius);
            }
        }
    }

    private void GenerateParticles() {
        Vector3 topLeft = spawnPosition - spawnBox / 2;
        int xDim = Math.RoundToInt(spawnBox.x/ particleRadius*2);
        int yDim = Math.RoundToInt(spawnBox.y/ particleRadius*2);
        int zDim = Math.RoundToInt(spawnBox.z/ particleRadius*2);
        for (int i = 0; i < particles.Count; i++) {
            particle p = new particle();

           
            p.velocity = Vector3.zero;
            p.force = Vector3.zero;
            p.density = 0;
            p.pressure = 0;

            particles.Add(p);
        }
    }

}