using UnityEngine;

public class MaterialPropertyButton : MonoBehaviour
{
    [Header("Shared Settings")]
    public BrushSettings settings;
    public BrushUIController uiController;

    [Header("Preset Values")]
    public string presetName = "Preset";
    public BrushSettings.BrushShape targetShape = BrushSettings.BrushShape.Round;
    public WorkflowMode targetWorkflow = WorkflowMode.Metallic;
    public SurfaceMode targetSurface = SurfaceMode.Opaque;
    [Min(0f)] public float targetBrushRadius = 0.02f;
    [Range(0f, 1f)] public float targetSmoothness = 0.5f;
    [Range(0f, 1f)] public float targetMetallic = 0f;
    public Color targetColor = Color.white;

    [Header("Style Assets")]
    public Material targetStrokeMaterial;
    public GameObject targetBrushPrefab;
    public Texture2D targetTexture;
    public Vector2 targetTiling = Vector2.one;

    [Header("Stamp Settings")]
    public bool useSingleStamp;

    [Header("Premium Presets")]
    public bool targetIsElectric;
    public RenderFace targetRenderFace = RenderFace.Front;

    [HideInInspector]
    public bool targetUseEmission;

    public bool isElectric;
    public Color glowColor = Color.cyan;
    [Min(0f)] public float glowIntensity = 5f;

    public void ApplyToBrush()
    {
        if (settings == null || uiController == null)
        {
            Debug.LogError("<color=red>[Brush Error]</color> Settings or UIController missing on Button: " + gameObject.name);
            return;
        }

        Color activeColor = settings.activeColor;
        if (uiController.colorPreviewDisplay != null)
            activeColor = uiController.colorPreviewDisplay.color;

        settings.activeShape = targetShape;
        settings.workflow = targetWorkflow;
        settings.surface = targetSurface;
        settings.brushRadius = targetBrushRadius;
        settings.smoothness = targetSmoothness;
        settings.metallic = targetMetallic;
        settings.isSingleStampMode = useSingleStamp;
        if (targetStrokeMaterial != null)
            settings.strokeMaterial = targetStrokeMaterial;
        if (targetBrushPrefab != null)
            settings.brushPrefabOverride = targetBrushPrefab;
        settings.brushTexture = targetTexture;
        settings.tiling = targetTiling;
        settings.activeColor = activeColor;
        settings.isElectric = targetIsElectric || targetUseEmission || isElectric;
        settings.useEmission = settings.isElectric;
        settings.renderFace = targetRenderFace;
        settings.glowColor = activeColor;
        settings.glowIntensity = glowIntensity;
        settings.emissionColor = activeColor;
        settings.emissionIntensity = glowIntensity;

        // Keep legacy emission fields synced for components that still read them.
        if (settings.isElectric)
        {
            settings.emissionColor = activeColor;
            settings.emissionIntensity = settings.glowIntensity;
        }
        else
        {
            settings.emissionColor = Color.black;
            settings.emissionIntensity = 0f;
        }

        Debug.Log($"<color=white>[Preset]</color> {targetShape} applied. Base and Emission synced to {activeColor}");

        uiController.UpdateBrushColor(activeColor);
        uiController.SyncUIWithSettings();

        Debug.Log("Applied brush preset: " + presetName);
    }
}
