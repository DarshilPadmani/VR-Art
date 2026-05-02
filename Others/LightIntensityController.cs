using UnityEngine;
using UnityEngine.UI;

public class LightIntensityController : MonoBehaviour
{
    public GameObject uiCanvas; // Drag your World Space Canvas here
    public Slider intensitySlider; // Drag your UI Slider here
    public Light pointLight; // Drag your Point Light here

    void Start()
    {
        // Set slider range and current value
        intensitySlider.minValue = 0f;
        intensitySlider.maxValue = 10f;
        intensitySlider.value = pointLight.intensity;

        // Add listener to slider
        intensitySlider.onValueChanged.AddListener(UpdateIntensity);
    }

    // This method will be called by the Ray Interactable Event
    public void ToggleUI()
    {
        uiCanvas.SetActive(!uiCanvas.activeSelf);
    }

    void UpdateIntensity(float value)
    {
        pointLight.intensity = value;
    }
}