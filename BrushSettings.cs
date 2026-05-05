using UnityEngine;

public enum WorkflowMode { Metallic, Specular }
public enum SurfaceMode { Opaque, Transparent }
public enum RenderFace { Front = 2, Back = 1, Both = 0 }

[CreateAssetMenu(fileName = "NewBrushSettings", menuName = "VRArt/BrushSettings")]
public class BrushSettings : ScriptableObject
{
    public Color activeColor = Color.white;
    public float brushRadius = 0.02f;
    public int smoothingLevel = 5; // How many points to average for smooth lines

    [Header("Preset Assets")]
    public Material strokeMaterial;
    public GameObject brushPrefabOverride;

    [Header("Placement Mode")]
    public bool isSingleStampMode = false; // If true, one click = one prefab

    [Header("PBR Properties")]
    [Range(0f, 1f)] public float smoothness = 0.5f;
    [Range(0f, 1f)] public float metallic = 0.0f;

    [Header("Advanced Modes")]
    public WorkflowMode workflow = WorkflowMode.Metallic;
    public SurfaceMode surface = SurfaceMode.Opaque;

    [Header("Advanced Rendering")]
    public bool isElectric;
    public bool useEmission;
    public RenderFace renderFace = RenderFace.Front;
    public Color glowColor = Color.cyan;
    [Min(0f)] public float glowIntensity = 1f;

    public Color emissionColor = Color.black;
    [Min(0f)] public float emissionIntensity = 0f;

    [Header("Texture Architecture")]
    public Texture2D brushTexture;
    public Vector2 tiling = Vector2.one;
    public bool useTextureAlpha = true;

    // Smart helper for material/shader branching.
    public bool HasTexture => brushTexture != null;
    
    // Enum to define different brush shapes - Professional Artist Strokes
    public enum BrushShape { Round, Flat, Tapered, Calligraphy, Ribbon, Spray, Oil }
    public BrushShape activeShape = BrushShape.Round;
    
    [Header("Artist Stroke Settings")]
    // Controls the taper profile for tapered and calligraphy strokes
    // 0 = start of stroke (thin), 1 = end of stroke (thin)
    public AnimationCurve edgeProfile = AnimationCurve.Linear(0, 1, 1, 1);
    
    // For flat brush: the width-to-height ratio
    [Range(0.1f, 5f)] public float flatBrushAspectRatio = 2f;

    [Header("Eraser Mode Settings")]
    public bool isEraserMode = false;
    public bool eraseOnlyTouchedPoints = false;
    public float eraseCooldownInterval = 0.05f; // 50ms cooldown for performance

    // This method will be called by your ColorPicker.cs logic
    public void SetColor(Color newColor)
    {
        activeColor = newColor;
    }
}