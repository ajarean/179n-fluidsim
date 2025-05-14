using System.Collections.Generic;
using UnityEngine;

public class PBFSimulator : MonoBehaviour
{
  public GameObject particlePrefab;
  public int particleCount = 300;
  public float smoothingRadius = 0.3f;
  public float restDensity = 10f;
  public float timeStep = 0.016f;
  public int solverIterations = 4;
  public float boundaryDamping = 0.5f;
  public float xBoundary = 3f;
  public float yBoundary = 5f;
  public float zBoundary = 3f;

  private List<PBFParticle> particles = new();
  private Dictionary<Vector3Int, List<PBFParticle>> spatialHash = new();
  private float cellSize; // Size of each grid cell

  void InitializeParticles()
  {
    for (int i = 0; i < particleCount; i++)
    {
      Vector3 pos = new Vector3(
          Random.Range(-1.5f, 1.5f), //TODO: make this a param, or time base the spawning (like faucet)
          Random.Range(1.0f, 3.0f),
          Random.Range(-1.5f, 1.5f)
      );
      GameObject go = Instantiate(particlePrefab, pos, Quaternion.identity);
      particles.Add(new PBFParticle(pos, go));
    }
  }
  private Vector3Int GetCell(Vector3 position)
  {
    return new Vector3Int(
      Mathf.FloorToInt(position.x / cellSize),
      Mathf.FloorToInt(position.y / cellSize),
      Mathf.FloorToInt(position.z / cellSize)
    );
  }
  private void UpdateSpatialHash()
  {
    spatialHash.Clear();
    foreach (var p in particles)
    {
      Vector3Int cell = GetCell(p.predictedPosition);
      if (!spatialHash.ContainsKey(cell))
      {
        spatialHash[cell] = new List<PBFParticle>();
      }
      spatialHash[cell].Add(p);
    }
  }
  private List<PBFParticle> GetNeighbors(PBFParticle particle)
  {
    List<PBFParticle> neighbors = new();
    Vector3Int cell = GetCell(particle.predictedPosition);

    // Check the current cell and adjacent cells
    for (int x = -1; x <= 1; x++)
    {
      for (int y = -1; y <= 1; y++)
      {
        for (int z = -1; z <= 1; z++)
        {
          Vector3Int neighborCell = cell + new Vector3Int(x, y, z);
          if (spatialHash.ContainsKey(neighborCell))
          {
            neighbors.AddRange(spatialHash[neighborCell]);
          }
        }
      }
    }

    return neighbors;
  }

  void Start()
  {
    cellSize = smoothingRadius; //match smoothing radius to cell size for 
    InitializeParticles();
  }

  void FixedUpdate()
  {
    // Predict positions
    foreach (var p in particles)
    {
      p.velocity += new Vector3(0, -9.8f, 0) * timeStep; // Apply gravity
      p.predictedPosition = p.position + p.velocity * timeStep;
    }

    // Update spatial hash
    UpdateSpatialHash();

    // Solve constraints
    for (int iter = 0; iter < solverIterations; iter++)
    {
      foreach (var pi in particles)
      {
        float density = 0;
        foreach (var pj in GetNeighbors(pi))
        {
          Vector3 rij = pj.predictedPosition - pi.predictedPosition;
          float r2 = rij.sqrMagnitude;
          if (r2 < smoothingRadius * smoothingRadius)
          {
            float r = Mathf.Sqrt(r2);
            float q = 1 - r / smoothingRadius;
            density += q * q * q; // Poly6 kernel
          }
        }
        pi.density = density * restDensity;

        // Compute lambda (constraint correction factor)
        float constraint = pi.density / restDensity - 1;
        float sumGradC2 = 0;
        Vector3 gradCi = Vector3.zero;

        foreach (var pj in GetNeighbors(pi))
        {
          Vector3 rij = pj.predictedPosition - pi.predictedPosition;
          float r2 = rij.sqrMagnitude;
          if (r2 < smoothingRadius * smoothingRadius)
          {
            float r = Mathf.Sqrt(r2);
            float q = 1 - r / smoothingRadius;
            Vector3 gradCj = -3 * q * q / smoothingRadius * rij.normalized;
            gradCi += gradCj;
            sumGradC2 += gradCj.sqrMagnitude;
          }
        }
        sumGradC2 += gradCi.sqrMagnitude;
        pi.lambda = -constraint / (sumGradC2 + 1e-6f);
      }

      // Apply position corrections
      foreach (var pi in particles)
      {
        Vector3 deltaP = Vector3.zero;
        foreach (var pj in GetNeighbors(pi))
        {
          if (pi == pj) continue;

          Vector3 rij = pj.predictedPosition - pi.predictedPosition;
          float r2 = rij.sqrMagnitude;
          if (r2 < smoothingRadius * smoothingRadius)
          {
            float r = Mathf.Sqrt(r2);
            float q = 1 - r / smoothingRadius;
            deltaP += (pi.lambda + pj.lambda) * 3 * q * q / smoothingRadius * rij.normalized;
          }
        }
        pi.predictedPosition += deltaP;
      }
    }

    // Update velocities and positions
    foreach (var p in particles)
    {
      p.velocity = (p.predictedPosition - p.position) / timeStep;
      p.position = p.predictedPosition;

      // Handle boundary collisions
      if (p.position.x < -xBoundary) { p.position.x = -xBoundary; p.velocity.x *= -boundaryDamping; }
      if (p.position.x > xBoundary) { p.position.x = xBoundary; p.velocity.x *= -boundaryDamping; }
      if (p.position.y < 0f) { p.position.y = 0f; p.velocity.y *= -boundaryDamping; }
      if (p.position.y > yBoundary) { p.position.y = yBoundary; p.velocity.y *= -boundaryDamping; }
      if (p.position.z < -zBoundary) { p.position.z = -zBoundary; p.velocity.z *= -boundaryDamping; }
      if (p.position.z > zBoundary) { p.position.z = zBoundary; p.velocity.z *= -boundaryDamping; }

      p.obj.transform.position = p.position;
    }
  }

  private class PBFParticle
  {
    public Vector3 position;
    public Vector3 predictedPosition;
    public Vector3 velocity;
    public float density;
    public float lambda;
    public GameObject obj;

    public PBFParticle(Vector3 pos, GameObject o)
    {
      position = pos;
      predictedPosition = pos;
      velocity = Vector3.zero;
      density = 0f;
      lambda = 0f;
      obj = o;
    }
  }
}