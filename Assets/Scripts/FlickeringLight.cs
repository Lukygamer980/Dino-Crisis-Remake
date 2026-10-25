using UnityEngine;

[RequireComponent(typeof(Light))]
public class FlickeringLight : MonoBehaviour
{
    [Range(0f, 1f)] public float dropoutChance = 0.12f;

    Light flickeringLight;
    float baseIntensity;
    float nextChangeTime;

    void Awake()
    {
        flickeringLight = GetComponent<Light>();
        baseIntensity = flickeringLight.intensity;
    }

    void Update()
    {
        if (Time.time < nextChangeTime) return;
        bool dropout = Random.value < dropoutChance;
        flickeringLight.intensity = baseIntensity * (dropout ? 0.08f : Random.Range(0.8f, 1.1f));
        nextChangeTime = Time.time + (dropout ? Random.Range(0.04f, 0.15f) : Random.Range(0.1f, 0.6f));
    }
}
