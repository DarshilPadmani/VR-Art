using UnityEngine;

public class MaterialPropertyButton : MonoBehaviour
{
    [Header("Shared Settings")]
    public BrushSettings settings;

    [Header("Preset Values")]
    [Range(0f, 1f)] public float targetSmoothness = 0.5f;
    [Range(0f, 1f)] public float targetMetallic = 0f;
    public bool isElectric;
    public Color glowColor = Color.cyan;
    [Min(0f)] public float glowIntensity = 5f;

    public void ApplyToBrush()
    {
        if (settings == null)
        {
            Debug.LogWarning("MaterialPropertyButton has no BrushSettings assigned.");
            return;
        }

        settings.smoothness = targetSmoothness;
        settings.metallic = targetMetallic;

        if (isElectric)
        {
            settings.emissionColor = glowColor;
            settings.emissionIntensity = glowIntensity;
        }
        else
        {
            settings.emissionColor = Color.black;
            settings.emissionIntensity = 0f;
        }
    }
}
