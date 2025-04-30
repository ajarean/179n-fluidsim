using System.Collections.Generic;
using UnityEngine;

public class FluidSimulator : MonoBehaviour
{
    public GameObject particlePrefab;
    public int particleCount = 300;
    public float smoothingRadius = 0.3f;
    public float restDensity = 10f;
    public float pressureCoeff = 200f;
    public float viscosityCoeff = 1f;
    public float particleMass = 1f;
    public float gravity = -9.8f;

    private List<FluidParticle> particles = new();

    void Start()
    {
        for (int i = 0; i < particleCount; i++)
        {
            Vector3 pos = new Vector3(
                Random.Range(-1.5f, 1.5f),
                Random.Range(1.0f, 3.0f),
                Random.Range(-1.5f, 1.5f)
            );
            GameObject go = Instantiate(particlePrefab, pos, Quaternion.identity);
            particles.Add(new FluidParticle(pos, go));
        }
    }

    void FixedUpdate() //corresponds to substep
    {
        foreach (var p in particles)
        {
            p.density = 0;
            p.pressure = 0;
        }

        // density
        foreach (var pi in particles)
        {
            foreach (var pj in particles)
            {
                Vector3 rij = pj.position - pi.position;
                float r2 = rij.sqrMagnitude;
                if (r2 < smoothingRadius * smoothingRadius)
                {
                    float r = Mathf.Sqrt(r2);
                    float q = 1 - r / smoothingRadius;
                    pi.density += particleMass * q * q * q; // Poly6
                }
            }
            pi.pressure = pressureCoeff * (pi.density - restDensity);
        }

        // force
        foreach (var pi in particles)
        {
            Vector3 force = Vector3.zero;

            foreach (var pj in particles)
            {
                if (pi == pj) continue;

                Vector3 rij = pj.position - pi.position;
                float r = rij.magnitude;
                if (r < smoothingRadius && r > 0.0001f)
                {
                    Vector3 dir = rij / r;

                    // Pressure force
                    float avgPressure = (pi.pressure + pj.pressure) / 2f;
                    force -= dir * particleMass * avgPressure / pj.density * (1 - r / smoothingRadius);

                    // Viscosity force
                    Vector3 velDiff = pj.velocity - pi.velocity;
                    force += viscosityCoeff * velDiff * particleMass / pj.density * (1 - r / smoothingRadius);
                }
            }

            // Gravity
            force += new Vector3(0, gravity, 0) * pi.density;

            // Acceleration = Force / Density
            Vector3 acceleration = force / pi.density;
            pi.velocity += acceleration * Time.fixedDeltaTime;
        }

        // Integrate and update
        foreach (var p in particles)
        {
            p.position += p.velocity * Time.fixedDeltaTime;

            // boundary (-3,0,-3) to (3,5,3)
            if (p.position.x < -3f) { p.position.x = -3f; p.velocity.x *= -0.5f; }
            if (p.position.x > 3f)  { p.position.x = 3f;  p.velocity.x *= -0.5f; }
            if (p.position.y < 0f)  { p.position.y = 0f;  p.velocity.y *= -0.5f; }
            if (p.position.y > 5f)  { p.position.y = 5f;  p.velocity.y *= -0.5f; }
            if (p.position.z < -3f) { p.position.z = -3f; p.velocity.z *= -0.5f; }
            if (p.position.z > 3f)  { p.position.z = 3f;  p.velocity.z *= -0.5f; }

            p.obj.transform.position = p.position;
        }
    }

    private class FluidParticle
    {
        public Vector3 position;
        public Vector3 velocity;
        public float density;
        public float pressure;
        public GameObject obj;

        public FluidParticle(Vector3 pos, GameObject o)
        {
            position = pos;
            obj = o;
            velocity = Vector3.zero;
            density = 0f;
            pressure = 0f;
        }
    }
}
