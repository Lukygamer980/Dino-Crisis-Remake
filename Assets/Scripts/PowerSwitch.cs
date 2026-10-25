using UnityEngine;

// A switch that powers something once: the backup generator, or the console that releases the
// laser shutters. Until it is on, doors that name it as their power source stay shut. Switching
// it on turns on the lights it powers and wakes the raptors it was holding back, and it is loud
// enough to draw raptors nearby.
public class PowerSwitch : Interactable
{
    public string prompt = "Switch on the generator";
    [TextArea] public string message = "";
    public Light[] poweredLights = new Light[0];
    // Inactive raptors that appear once the switch is on.
    public GameObject[] ambushRaptors = new GameObject[0];
    public float noiseRadius = 25f;
    // Lit while the switch is on (assigned by the scene builder).
    public Renderer statusLamp;
    public Material runningMaterial;

    public bool IsOn { get; private set; }

    public override string Prompt => prompt;

    void Start()
    {
        foreach (Light poweredLight in poweredLights) poweredLight.enabled = false;
    }

    public override void Interact(PlayerController player)
    {
        IsOn = true;
        foreach (Light poweredLight in poweredLights) poweredLight.enabled = true;
        foreach (GameObject raptor in ambushRaptors) raptor.SetActive(true);
        if (statusLamp != null && runningMaterial != null) statusLamp.sharedMaterial = runningMaterial;
        if (noiseRadius > 0f) Raptor.MakeNoise(transform.position, noiseRadius);
        GameUI.Instance.ShowMessage(message, 3.5f);
        // Nothing left to do here, so the prompt goes away.
        enabled = false;
    }
}
