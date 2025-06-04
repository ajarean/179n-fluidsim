using UnityEngine;

public class TimeScript : MonoBehaviour
{
    public SPH sphSimulation; // Assign your SPH script component in the Inspector

    public KeyCode pauseToggleKey = KeyCode.P;
    public KeyCode incrementTimestepKey = KeyCode.RightArrow; 

    public float timestepIncrementAmount = 0.0005f;

    private bool isPaused = false;

    private float originalTimestep;

    void Start()
    {
        if (sphSimulation == null)
        {
            Debug.LogError("SPH Simulation script not assigned in SimulationControl. Please assign it in the Inspector.");
            enabled = false; // Disable this control script if target is not set
            return;
        }

        originalTimestep = sphSimulation.timestep; // Store the original timestep value

        // Initialize pause state based on SPH script's initial enabled state (optional)
        // isPaused = !sphSimulation.enabled; 
    }

    
    void Update()
    {
        if (sphSimulation == null) return;

        // Pause / Unpause
        if (Input.GetKeyDown(pauseToggleKey))
        {

            sphSimulation.isPausedFR = !sphSimulation.isPausedFR; // Toggle the state in SPH.cs
            if (sphSimulation.isPausedFR)
            {
                Debug.Log("SPH Simulation PAUSED. Particles frozen. Press '" + incrementTimestepKey.ToString() + "' to step.");
            }
            else
            {
                sphSimulation.timestep = originalTimestep; // Reset timestep to original value when unpaused
                Debug.Log("SPH Simulation UNPAUSED. Particles active.");
            }
        }

        // Step Forward (only if paused)
        if (sphSimulation.isPausedFR && Input.GetKeyDown(incrementTimestepKey))
        {
            sphSimulation.RequestSingleStep();
            // The Debug.Log for step request is now in SPH.cs's RequestSingleStep method
        }

        // Increment Timestep
        if (Input.GetKeyDown(incrementTimestepKey))
        {
            AdjustTimestep(timestepIncrementAmount);
        }

    }

    public void AdjustTimestep(float amount)
    {
        if (sphSimulation == null) return;

        // float newTimestep = sphSimulation.timestep + amount;
        sphSimulation.timestep += amount;

        Debug.Log("SPH Timestep set to: " + sphSimulation.timestep.ToString("F5"));
    }

    public void TriggerStepForward()
    {
        if (sphSimulation == null) return;
        if (sphSimulation.isPausedFR)
        {
            sphSimulation.RequestSingleStep();
        }
        else
        {
            Debug.LogWarning("Cannot step forward: SPH Simulation is not paused.");
        }
    }
}