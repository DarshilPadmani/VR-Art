using UnityEngine;

[CreateAssetMenu(fileName = "NewBrushSettings", menuName = "VRArt/BrushSettings")]
public class BrushSettings : ScriptableObject
{
    public Color activeColor = Color.white;
    public float brushRadius = 0.02f;
    public int smoothingLevel = 5; // How many points to average for smooth lines

    [Header("PBR Properties")]
    [Range(0f, 1f)] public float smoothness = 0.5f;
    [Range(0f, 1f)] public float metallic = 0.0f;
    public Color emissionColor = Color.black;
    [Min(0f)] public float emissionIntensity = 0f;
    
    // Enum to define different 3D shapes
    public enum BrushShape { Tube, Ribbon, Square }
    public BrushShape activeShape = BrushShape.Tube;

    // This method will be called by your ColorPicker.cs logic
    public void SetColor(Color newColor)
    {
        activeColor = newColor;
    }
}