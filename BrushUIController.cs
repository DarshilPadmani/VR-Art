using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BrushUIController : MonoBehaviour
{
    [Header("Data Architecture")]
    public BrushSettings settings;
    public DrawingManager drawingManager;
    public DrawingHistoryManager historyManager;
    public SelectionManager selectionManager;

    [Header("UI References")]
    public RawImage colorPreviewDisplay;
    public MeshRenderer controllerSphereRenderer;
    public Slider brushSizeSlider;
    public Button openPickerButton;
    public Button clearCanvasButton;
    public Button undoButton;
    public Button redoButton;
    public Button duplicateButton;
    public Dropdown shapeDropdown;
    public Slider metallicSlider;
    public Slider smoothnessSlider;
    public TMP_Dropdown workflowDropdown;
    public TMP_Dropdown surfaceDropdown;

    [Header("Advanced Rendering")]
    public Toggle emissionToggle;
    public TMP_Dropdown renderFaceDropdown;

    [Header("Brush Size Range")]
    [Min(0f)] public float minBrushRadius = 0.001f;
    [Min(0.0001f)] public float maxBrushRadius = 0.25f;

    private Material _previewMaterialInstance;
    private Vector3 _lastControllerPosition;
    private bool _hasLastControllerPosition;

    public float CurrentStretchAmount { get; private set; } = 1f;

    private void Start()
    {
        // Initial sync from settings to UI.
        if (brushSizeSlider != null)
        {
            GetBrushRadiusRange(out float minRadius, out float maxRadius);
            brushSizeSlider.value = Mathf.InverseLerp(minRadius, maxRadius, settings.brushRadius);
            brushSizeSlider.onValueChanged.AddListener(UpdateBrushSize);
        }

        if (openPickerButton != null)
        {
            openPickerButton.onClick.AddListener(RequestColorPicker);
        }

        if (colorPreviewDisplay != null)
        {
            colorPreviewDisplay.color = settings.activeColor;
        }

        if (clearCanvasButton != null)
        {
            clearCanvasButton.onClick.AddListener(ClearCanvas);
        }

        if (undoButton != null)
        {
            undoButton.onClick.AddListener(UndoStroke);
        }

        if (redoButton != null)
        {
            redoButton.onClick.AddListener(RedoStroke);
        }

        if (duplicateButton != null && selectionManager == null)
        {
            Debug.LogWarning("<color=orange>[Duplicate]</color> SelectionManager is missing, so duplicate button wiring is skipped.");
        }

        if (shapeDropdown != null)
        {
            EnsureShapeDropdownOptions();
            int shapeIndex = ClampShapeIndex((int)settings.activeShape);
            settings.activeShape = (BrushSettings.BrushShape)shapeIndex;
            shapeDropdown.value = shapeIndex;
            shapeDropdown.onValueChanged.AddListener(SetBrushShape);
        }

        if (metallicSlider != null)
        {
            metallicSlider.value = settings.metallic;
            metallicSlider.onValueChanged.AddListener(OnMetallicChanged);
        }

        if (smoothnessSlider != null)
        {
            smoothnessSlider.value = settings.smoothness;
            smoothnessSlider.onValueChanged.AddListener(OnSmoothnessChanged);
        }

        if (workflowDropdown != null)
        {
            workflowDropdown.value = (int)settings.workflow;
            workflowDropdown.onValueChanged.AddListener(OnWorkflowChanged);
        }

        if (surfaceDropdown != null)
        {
            surfaceDropdown.value = (int)settings.surface;
            surfaceDropdown.onValueChanged.AddListener(OnSurfaceChanged);
        }

        if (emissionToggle != null)
        {
            emissionToggle.isOn = settings.isElectric;
            emissionToggle.onValueChanged.AddListener((val) =>
            {
                settings.isElectric = val;
                settings.useEmission = val;
                if (!val)
                {
                    settings.emissionColor = Color.black;
                    settings.emissionIntensity = 0f;
                }
            });
        }

        if (renderFaceDropdown != null)
        {
            renderFaceDropdown.value = MapRenderFaceToDropdown(settings.renderFace);
            renderFaceDropdown.onValueChanged.AddListener(OnRenderFaceChanged);
        }

        if (colorPreviewDisplay != null && colorPreviewDisplay.material != null)
        {
            // Create one runtime instance and share it across preview targets.
            _previewMaterialInstance = new Material(colorPreviewDisplay.material);
            colorPreviewDisplay.material = _previewMaterialInstance;

            if (controllerSphereRenderer != null)
                controllerSphereRenderer.material = _previewMaterialInstance;

            Debug.Log("<color=green>[Material Sync]</color> Shared instance created and locked.");
        }

        SyncUIWithSettings();
    }

    private void Update()
    {
        if (settings == null || _previewMaterialInstance == null)
            return;

        // Keep the preview image locked to the live instance.
        if (colorPreviewDisplay != null && colorPreviewDisplay.material != _previewMaterialInstance)
            colorPreviewDisplay.material = _previewMaterialInstance;

        if (controllerSphereRenderer != null && controllerSphereRenderer.material != _previewMaterialInstance)
            controllerSphereRenderer.material = _previewMaterialInstance;

        // Keep UI controls in sync when settings are changed externally.
        if (metallicSlider != null)
            metallicSlider.SetValueWithoutNotify(settings.metallic);

        if (smoothnessSlider != null)
            smoothnessSlider.SetValueWithoutNotify(settings.smoothness);

        if (emissionToggle != null)
            emissionToggle.SetIsOnWithoutNotify(settings.isElectric);

        if (renderFaceDropdown != null)
            renderFaceDropdown.SetValueWithoutNotify(MapRenderFaceToDropdown(settings.renderFace));

        UpdateStretchAmount();

        // Smart texture assignment: sample textures only when a style texture exists.
        ApplyTextureArchitecture(_previewMaterialInstance);

        ApplyStretchAmount(_previewMaterialInstance);

        // 1. Sync color
        _previewMaterialInstance.color = settings.activeColor;
        SyncBrushMaterialColor(settings.activeColor);

        // 2. Sync metallic and smoothness
        if (_previewMaterialInstance.HasProperty("_Metallic"))
            _previewMaterialInstance.SetFloat("_Metallic", settings.metallic);

        if (_previewMaterialInstance.HasProperty("_Smoothness"))
            _previewMaterialInstance.SetFloat("_Smoothness", settings.smoothness);
        else if (_previewMaterialInstance.HasProperty("_Glossiness"))
            _previewMaterialInstance.SetFloat("_Glossiness", settings.smoothness);

        // 3. Force metallic keyword behavior where required.
        if (settings.metallic > 0.01f)
            _previewMaterialInstance.EnableKeyword("_METALLICGLOSSMAP");
        else
            _previewMaterialInstance.DisableKeyword("_METALLICGLOSSMAP");

        // Emission logic driven by isElectric for premium stroke behavior.
        if (settings.isElectric)
        {
            _previewMaterialInstance.EnableKeyword("_EMISSION");
            if (_previewMaterialInstance.HasProperty("_EmissionColor"))
            {
                Color finalGlow = settings.emissionColor * Mathf.LinearToGammaSpace(settings.emissionIntensity);
                _previewMaterialInstance.SetColor("_EmissionColor", finalGlow);
            }

            settings.useEmission = true;
            settings.glowColor = settings.emissionColor;
            settings.glowIntensity = settings.emissionIntensity;
        }
        else
        {
            _previewMaterialInstance.DisableKeyword("_EMISSION");
            if (_previewMaterialInstance.HasProperty("_EmissionColor"))
                _previewMaterialInstance.SetColor("_EmissionColor", Color.black);

            settings.useEmission = false;
            settings.emissionColor = Color.black;
            settings.emissionIntensity = 0f;
            settings.glowIntensity = 0f;
        }

        // Unity _Cull values: 0 Off (both), 1 Front (shows back), 2 Back (shows front).
        if (_previewMaterialInstance.HasProperty("_Cull"))
            _previewMaterialInstance.SetFloat("_Cull", (float)settings.renderFace);

        // 4. Update child visual scale if a dedicated sphere is parented under preview display.
        if (colorPreviewDisplay != null && colorPreviewDisplay.transform.childCount > 0)
        {
            float visualScale = settings.brushRadius * 10f;
            colorPreviewDisplay.transform.GetChild(0).localScale = Vector3.one * visualScale;
        }
    }

    private void OnDestroy()
    {
        if (_previewMaterialInstance != null)
            Destroy(_previewMaterialInstance);
    }

    private void UpdateBrushSize(float sliderValue)
    {
        GetBrushRadiusRange(out float minRadius, out float maxRadius);
        settings.brushRadius = Mathf.Lerp(minRadius, maxRadius, sliderValue);

        if (drawingManager != null)
            drawingManager.UpdateFireBrushSize(settings.brushRadius);
    }

    public void AdjustSliderValue(float delta)
    {
        if (brushSizeSlider == null)
            return;

        // Changing slider value invokes UpdateBrushSize via the existing listener.
        brushSizeSlider.value = Mathf.Clamp01(brushSizeSlider.value + delta);
    }

    private void GetBrushRadiusRange(out float minRadius, out float maxRadius)
    {
        minRadius = Mathf.Max(0f, minBrushRadius);
        maxRadius = Mathf.Max(minRadius + 0.0001f, maxBrushRadius);
    }

    public void SetBrushShape(int index)
    {
        settings.activeShape = (BrushSettings.BrushShape)ClampShapeIndex(index);
    }

    public void SetStandardBrush()
    {
        ChangeBrushType("standard");
    }

    public void SetFireBrush()
    {
        ChangeBrushType("fire");
    }

    public void SetSparkleBrush()
    {
        ChangeBrushType("sparkle");
    }

    public void SetIceBrush()
    {
        ChangeBrushType("sparkle");
    }

    public void ChangeBrushType(string type)
    {
        if (drawingManager == null)
            return;

        drawingManager.ChangeBrushType(type);
    }

    public void SelectStrokePreset(BrushSettings newPreset)
    {
        if (newPreset == null || settings == null)
            return;

        // Preserve the user's chosen color while swapping preset assets.
        Color currentColor = settings.activeColor;
        settings = newPreset;
        settings.activeColor = currentColor;

        if (drawingManager != null)
            drawingManager.settings = settings;

        SyncSettingsToTools();
    }

    public void SyncSettingsToTools()
    {
        if (settings == null || drawingManager == null)
            return;

        // Pull latest color from preview UI because color picker writes to this target.
        if (colorPreviewDisplay != null)
            settings.activeColor = colorPreviewDisplay.color;

        // Push the shared settings asset to drawing.
        drawingManager.settings = settings;
        drawingManager.SyncActiveBrushMaterialColor(settings.activeColor);

        if (_previewMaterialInstance != null)
        {
            _previewMaterialInstance.color = settings.activeColor;
            SyncBrushMaterialColor(settings.activeColor);

            if (_previewMaterialInstance.HasProperty("_Metallic"))
                _previewMaterialInstance.SetFloat("_Metallic", settings.metallic);

            if (_previewMaterialInstance.HasProperty("_Smoothness"))
                _previewMaterialInstance.SetFloat("_Smoothness", settings.smoothness);
            else if (_previewMaterialInstance.HasProperty("_Glossiness"))
                _previewMaterialInstance.SetFloat("_Glossiness", settings.smoothness);
        }

        SyncUIWithSettings();

        Debug.ClearDeveloperConsole();
        Debug.Log($"<color=green>Brush Sync:</color> Color {settings.activeColor} applied to {settings.name}");
    }

    public void SyncUIWithSettings()
    {
        if (settings == null)
            return;

        if (brushSizeSlider != null)
        {
            GetBrushRadiusRange(out float minRadius, out float maxRadius);
            float brushT = Mathf.InverseLerp(minRadius, maxRadius, settings.brushRadius);
            brushSizeSlider.SetValueWithoutNotify(brushT);
        }

        if (metallicSlider != null)
            metallicSlider.SetValueWithoutNotify(settings.metallic);

        if (smoothnessSlider != null)
            smoothnessSlider.SetValueWithoutNotify(settings.smoothness);

        if (workflowDropdown != null)
            workflowDropdown.SetValueWithoutNotify((int)settings.workflow);

        if (surfaceDropdown != null)
            surfaceDropdown.SetValueWithoutNotify((int)settings.surface);

        if (emissionToggle != null)
            emissionToggle.SetIsOnWithoutNotify(settings.isElectric);

        if (renderFaceDropdown != null)
            renderFaceDropdown.SetValueWithoutNotify(MapRenderFaceToDropdown(settings.renderFace));

        if (shapeDropdown != null)
        {
            EnsureShapeDropdownOptions();
            int shapeIndex = ClampShapeIndex((int)settings.activeShape);
            settings.activeShape = (BrushSettings.BrushShape)shapeIndex;
            shapeDropdown.SetValueWithoutNotify(shapeIndex);
        }

        RefreshPreviewMaterial();
        RefreshPreviewScale();
    }

    public void OnMetallicChanged(float value)
    {
        settings.metallic = Mathf.Clamp01(value);
    }

    public void OnSmoothnessChanged(float value)
    {
        settings.smoothness = Mathf.Clamp01(value);
    }

    public void OnWorkflowChanged(int index)
    {
        settings.workflow = (WorkflowMode)Mathf.Clamp(index, 0, 1);
    }

    public void OnSurfaceChanged(int index)
    {
        settings.surface = (SurfaceMode)Mathf.Clamp(index, 0, 1);
    }

    public void OnRenderFaceChanged(int index)
    {
        // Dropdown: 0=Front, 1=Back, 2=Both
        if (index == 0)
            settings.renderFace = RenderFace.Front;
        else if (index == 1)
            settings.renderFace = RenderFace.Back;
        else
            settings.renderFace = RenderFace.Both;
    }

    private int MapRenderFaceToDropdown(RenderFace face)
    {
        if (face == RenderFace.Front)
            return 0;

        if (face == RenderFace.Back)
            return 1;

        return 2;
    }

    private int ClampShapeIndex(int index)
    {
        int max = System.Enum.GetValues(typeof(BrushSettings.BrushShape)).Length - 1;
        return Mathf.Clamp(index, 0, Mathf.Max(0, max));
    }

    private void EnsureShapeDropdownOptions()
    {
        if (shapeDropdown == null)
            return;

        string[] enumNames = System.Enum.GetNames(typeof(BrushSettings.BrushShape));
        if (shapeDropdown.options.Count == enumNames.Length)
            return;

        shapeDropdown.ClearOptions();
        shapeDropdown.AddOptions(new System.Collections.Generic.List<string>(enumNames));
    }

    private void RefreshPreviewMaterial()
    {
        if (_previewMaterialInstance == null || settings == null)
            return;

        ApplyTextureArchitecture(_previewMaterialInstance);
        ApplyStretchAmount(_previewMaterialInstance);

        _previewMaterialInstance.color = settings.activeColor;
        SyncBrushMaterialColor(settings.activeColor);

        if (_previewMaterialInstance.HasProperty("_Metallic"))
            _previewMaterialInstance.SetFloat("_Metallic", settings.metallic);

        if (_previewMaterialInstance.HasProperty("_Smoothness"))
            _previewMaterialInstance.SetFloat("_Smoothness", settings.smoothness);
        else if (_previewMaterialInstance.HasProperty("_Glossiness"))
            _previewMaterialInstance.SetFloat("_Glossiness", settings.smoothness);

        if (_previewMaterialInstance.HasProperty("_SpecColor"))
        {
            if (settings.workflow == WorkflowMode.Specular)
            {
                _previewMaterialInstance.SetColor("_SpecColor", Color.white * settings.metallic);
                _previewMaterialInstance.EnableKeyword("_SPECULAR_SETUP");
            }
            else
            {
                _previewMaterialInstance.DisableKeyword("_SPECULAR_SETUP");
            }
        }

        if (_previewMaterialInstance.HasProperty("_Surface"))
        {
            float surfaceValue = settings.surface == SurfaceMode.Transparent ? 1f : 0f;
            _previewMaterialInstance.SetFloat("_Surface", surfaceValue);
        }

        if (settings.isElectric)
        {
            _previewMaterialInstance.EnableKeyword("_EMISSION");
            if (_previewMaterialInstance.HasProperty("_EmissionColor"))
                _previewMaterialInstance.SetColor("_EmissionColor", settings.emissionColor * Mathf.LinearToGammaSpace(settings.emissionIntensity));

            settings.useEmission = true;
            settings.glowColor = settings.emissionColor;
            settings.glowIntensity = settings.emissionIntensity;
        }
        else
        {
            _previewMaterialInstance.DisableKeyword("_EMISSION");
            if (_previewMaterialInstance.HasProperty("_EmissionColor"))
                _previewMaterialInstance.SetColor("_EmissionColor", Color.black);

            settings.useEmission = false;
            settings.emissionColor = Color.black;
            settings.emissionIntensity = 0f;
            settings.glowIntensity = 0f;
        }

        if (_previewMaterialInstance.HasProperty("_Cull"))
            _previewMaterialInstance.SetFloat("_Cull", (float)settings.renderFace);
    }

    private void ApplyTextureArchitecture(Material mat)
    {
        if (mat == null || settings == null)
            return;

        if (!mat.HasProperty("_BaseMap"))
            return;

        if (settings.HasTexture)
        {
            mat.SetTexture("_BaseMap", settings.brushTexture);
            mat.SetTextureScale("_BaseMap", settings.tiling);
            mat.EnableKeyword("_USE_TEXTURE_ON");
        }
        else
        {
            mat.SetTexture("_BaseMap", null);
            mat.DisableKeyword("_USE_TEXTURE_ON");
        }
    }

    private void RefreshPreviewScale()
    {
        if (colorPreviewDisplay == null || settings == null)
            return;

        GetBrushRadiusRange(out float minRadius, out float maxRadius);
        float t = Mathf.InverseLerp(minRadius, maxRadius, settings.brushRadius);
        float uiScale = Mathf.Lerp(0.5f, 1.5f, t);
        colorPreviewDisplay.rectTransform.localScale = Vector3.one * uiScale;
    }

    private void UpdateStretchAmount()
    {
        if (drawingManager == null || drawingManager.brushTip == null)
        {
            CurrentStretchAmount = 1f;
            _hasLastControllerPosition = false;
            return;
        }

        Vector3 currentPosition = drawingManager.brushTip.position;
        if (!_hasLastControllerPosition)
        {
            _lastControllerPosition = currentPosition;
            _hasLastControllerPosition = true;
            CurrentStretchAmount = 1f;
            return;
        }

        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        float speed = Vector3.Distance(currentPosition, _lastControllerPosition) / deltaTime;
        CurrentStretchAmount = Mathf.Lerp(1.0f, 1.5f, Mathf.Clamp01(speed / 5f));
        _lastControllerPosition = currentPosition;
    }

    private void ApplyStretchAmount(Material mat)
    {
        if (mat == null || !mat.HasProperty("_StretchAmount"))
            return;

        mat.SetFloat("_StretchAmount", CurrentStretchAmount);
    }

    private void RequestColorPicker()
    {
        ColorPicker.Create(
            settings.activeColor,
            "Color Palette",
            (c) =>
            {
                settings.activeColor = c;
                SyncBrushMaterialColor(c);
                SyncUIWithSettings();
                Debug.Log("<color=cyan>[Brush]</color> Live Color Sync: " + c);
            },
            (c) =>
            {
                settings.activeColor = c;
                SyncBrushMaterialColor(c);
            },
            false
        );
    }

    public void UpdateBrushColor(Color newColor)
    {
        if (settings == null)
            return;

        settings.activeColor = newColor;

        if (colorPreviewDisplay != null)
        {
            colorPreviewDisplay.color = newColor;
        }

        if (_previewMaterialInstance != null)
        {
            _previewMaterialInstance.color = newColor;
        }

        if (drawingManager != null)
        {
            drawingManager.settings = settings;
        }

        SyncBrushMaterialColor(newColor);

        Debug.Log($"<color=green>[Brush Sync]</color> Brush Color updated to: {newColor}");
    }

    private void SyncBrushMaterialColor(Color color)
    {
        if (drawingManager != null)
        {
            drawingManager.SyncActiveBrushMaterialColor(color);
            return;
        }

        if (settings == null || settings.strokeMaterial == null)
            return;

        Material material = settings.strokeMaterial;
        material.color = color;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);

        if (settings != null && settings.isElectric && material.HasProperty("_EmissionColor"))
            material.SetColor("_EmissionColor", settings.emissionColor * Mathf.LinearToGammaSpace(settings.emissionIntensity));
    }

    public void UndoStroke()
    {
        if (historyManager != null)
            historyManager.Undo();
    }

    public void RedoStroke()
    {
        if (historyManager != null)
            historyManager.Redo();
    }

    public void PressDuplicateButton()
    {
        if (selectionManager != null)
        {
            // Toggle sphere selection mode using the available API.
            // Since ToggleSphereMode() does not exist, use isSphereModeActive and ClearAllSelection as a workaround.
            selectionManager.isSphereModeActive = !selectionManager.isSphereModeActive;
            if (!selectionManager.isSphereModeActive)
            {
                selectionManager.ClearAllSelection();
            }
            return;
        }

        Debug.LogWarning("<color=orange>[Duplicate]</color> SelectionManager is missing, so the duplicate button has no target.");
    }

    public void ClearCanvas()
    {
        if (historyManager != null)
            historyManager.ClearAll();
    }
}
