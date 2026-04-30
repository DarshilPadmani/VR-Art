using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class DrawingManager : MonoBehaviour
{
    public enum ToolMode { Draw, Erase }

    [Header("Setup")]
    public GameObject strokePrefab;

    [Header("Brush Library")]
    public GameObject standardBrushPrefab;
    [Tooltip("Legacy fallback for scenes that still assign the old standardPrefab field.")]
    public GameObject standardPrefab;
    public GameObject fireBrushPrefab;
    public GameObject sparkleBrushPrefab;
    public Transform brushTip; // The point on your right controller
    public Material brushMaterial;
    [Tooltip("Parent transform for all generated strokes. If null, strokes are parented under this object.")]
    public Transform artworkContainer;

    [Header("Input Actions")]
    public InputActionProperty drawAction; // Map to Right Controller Trigger/Press
    public InputActionProperty undoAction; // Map to Left Controller X / primary button
    public InputActionProperty redoAction; // Map to Left Controller Y / secondary button
    [Tooltip("Map to <XRController>{RightHand}/primary2DAxis for brush size changes.")]
    public InputActionProperty sizeChangeAction;
    [Tooltip("If drawAction is not assigned, use Meta right joystick click as fallback.")]
    public bool useFallbackRightJoystickClick = true;
    [Tooltip("If undo/redo actions are not assigned, use Meta left X/Y buttons as fallback.")]
    public bool useFallbackUndoRedoButtons = true;

    [Header("Joystick Size Control")]
    [Range(0.1f, 2f)] public float sizeSensitivity = 0.8f;
    public BrushUIController uiController;

    [Header("Current State")]
    [SerializeField] private GameObject _activeBrushPrefab;

    private GameObject _currentStrokeInstance;
    private ParticleSystem _currentPS; // Cache for efficiency
    private bool _isParticleBrushActive = false;
    public UnityEngine.UI.Slider brushSizeSlider;

    [Header("Shared Settings")]
    public BrushSettings settings;
    public DrawingHistoryManager historyManager;
    public SelectionManager selectionManager;

    [Header("Tool Mode")]
    public ToolMode currentTool = ToolMode.Draw;

    [Header("Eraser")]
    public GameObject eraserVisual;
    [Min(0f)] public float eraserRadiusMultiplier = 1f;
    public LayerMask eraseLayerMask = ~0;
    public QueryTriggerInteraction eraseTriggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Spray")]
    public ParticleSystem sprayParticleSystem;
    [Min(1f)] public float sprayEmissionRate = 45f;

    [Header("UI Interaction")]
    public EventSystem eventSystem;
    [Tooltip("Hard switch that can disable drawing from UI events (e.g. Color Picker PointerEnter/Exit).")]
    public bool canDraw = true;

    [Header("Debug")]
    public bool enableFireDebugLogs = false;

    [Header("Smoothing")]
    [Min(1)] public int smoothingWindowSize = 6;

    private ProBrushStroke _activeStroke;
    private readonly List<Vector3> _pointHistory = new List<Vector3>();
    private InputAction _fallbackDrawAction;
    private InputAction _fallbackUndoAction;
    private InputAction _fallbackRedoAction;
    private readonly Collider[] _eraseHits = new Collider[64];
    private float _sprayAccumulator;

    private InputAction ActiveDrawAction => drawAction.action != null ? drawAction.action : _fallbackDrawAction;
    private InputAction ActiveUndoAction => undoAction.action != null ? undoAction.action : _fallbackUndoAction;
    private InputAction ActiveRedoAction => redoAction.action != null ? redoAction.action : _fallbackRedoAction;
    private InputAction ActiveSizeChangeAction => sizeChangeAction.action;

    private void Awake()
    {
        if (selectionManager == null)
            selectionManager = FindObjectOfType<SelectionManager>();

        if (_activeBrushPrefab == null)
            ResolveBrushPrefab("standard");

        if (drawAction.action == null && useFallbackRightJoystickClick)
        {
            _fallbackDrawAction = new InputAction(
                "Draw",
                InputActionType.Button,
                "<XRController>{RightHand}/primary2DAxisClick");
        }

        if (useFallbackUndoRedoButtons)
        {
            if (undoAction.action == null)
            {
                _fallbackUndoAction = new InputAction(
                    "Undo",
                    InputActionType.Button,
                    "<XRController>{LeftHand}/primaryButton");
            }

            if (redoAction.action == null)
            {
                _fallbackRedoAction = new InputAction(
                    "Redo",
                    InputActionType.Button,
                    "<XRController>{LeftHand}/secondaryButton");
            }
        }
    }

    private GameObject StandardBrushPrefab => standardBrushPrefab != null ? standardBrushPrefab : standardPrefab;

    private void ResolveBrushPrefab(string type)
    {
        switch (type)
        {
            case "fire":
                _activeBrushPrefab = fireBrushPrefab != null ? fireBrushPrefab : StandardBrushPrefab != null ? StandardBrushPrefab : strokePrefab;
                break;
            case "sparkle":
                _activeBrushPrefab = sparkleBrushPrefab != null ? sparkleBrushPrefab : StandardBrushPrefab != null ? StandardBrushPrefab : strokePrefab;
                break;
            case "standard":
            default:
                _activeBrushPrefab = StandardBrushPrefab != null ? StandardBrushPrefab : strokePrefab;
                break;
        }
    }

    private void SetActiveBrushPrefab(GameObject brushPrefab, string label)
    {
        _activeBrushPrefab = brushPrefab != null ? brushPrefab : StandardBrushPrefab != null ? StandardBrushPrefab : strokePrefab;
        Debug.Log($"<color=cyan>[Brush]</color> Switched to: {label}");
    }

    public void ChangeBrushType(string type)
    {
        string normalizedType = string.IsNullOrWhiteSpace(type) ? "standard" : type.Trim().ToLowerInvariant();

        switch (normalizedType)
        {
            case "fire":
                SetBrushFire();
                break;
            case "sparkle":
            case "ice":
                SetBrushSparkle();
                break;
            case "standard":
            default:
                SetBrushStandard();
                break;
        }
    }

    public void SetBrushStandard()
    {
        SetActiveBrushPrefab(StandardBrushPrefab, "standard");
    }

    public void SetBrushToStandard()
    {
        SetBrushStandard();
    }

    public void SetBrushFire()
    {
        SetActiveBrushPrefab(fireBrushPrefab, "fire");
    }

    public void SetBrushToFire()
    {
        SetBrushFire();
    }

    public void SetBrushSparkle()
    {
        SetActiveBrushPrefab(sparkleBrushPrefab, "sparkle");
    }

    public void SetBrushToSparkle()
    {
        SetBrushSparkle();
    }

    private void OnEnable()
    {
        if (ActiveDrawAction != null)
            ActiveDrawAction.Enable();
        if (ActiveUndoAction != null)
            ActiveUndoAction.Enable();
        if (ActiveRedoAction != null)
            ActiveRedoAction.Enable();
        if (ActiveSizeChangeAction != null)
            ActiveSizeChangeAction.Enable();
    }

    private void OnDisable()
    {
        if (ActiveDrawAction != null)
            ActiveDrawAction.Disable();
        if (ActiveUndoAction != null)
            ActiveUndoAction.Disable();
        if (ActiveRedoAction != null)
            ActiveRedoAction.Disable();
        if (ActiveSizeChangeAction != null)
            ActiveSizeChangeAction.Disable();
    }

    private void OnDestroy()
    {
        if (_fallbackDrawAction != null)
            _fallbackDrawAction.Dispose();
        if (_fallbackUndoAction != null)
            _fallbackUndoAction.Dispose();
        if (_fallbackRedoAction != null)
            _fallbackRedoAction.Dispose();
    }

    void Update()
    {
        UpdateBrushTipVisualScale();

        InputAction action = ActiveDrawAction;
        InputAction undo = ActiveUndoAction;
        InputAction redo = ActiveRedoAction;

        if (undo != null && undo.WasPressedThisFrame())
            UndoLastStroke();
        if (redo != null && redo.WasPressedThisFrame())
            RedoLastStroke();

        HandleJoystickSizeChange();

        if (action == null)
            return;

        // Read the trigger value directly so analog trigger input is handled consistently.
        float triggerValue = action.ReadValue<float>();
        EventSystem activeEventSystem = eventSystem != null ? eventSystem : EventSystem.current;
        bool isOverUI = activeEventSystem != null && activeEventSystem.IsPointerOverGameObject(-1);

        UpdateEraserVisual();

        // Erase mode: hold trigger to remove any stroke touching the brush tip volume.
        if (currentTool == ToolMode.Erase)
        {
            if (_activeStroke != null)
                FinishActiveStroke();

            if (triggerValue > 0.5f && canDraw && !isOverUI)
                PerformErase();

            return;
        }

        if (settings != null && settings.activeShape == BrushSettings.BrushShape.Spray)
        {
            if (_activeStroke != null)
                FinishActiveStroke();

            if (triggerValue > 0.5f && canDraw && !isOverUI)
                EmitSpray();

            return;
        }

        // If UI has explicitly disabled drawing, end the active stroke immediately.
        if (!canDraw && _activeStroke != null)
        {
            FinishActiveStroke();
        }

        // Particle brush (fire) mode
        if (_activeBrushPrefab == fireBrushPrefab)
        {
            if (_currentStrokeInstance != null && (!canDraw || isOverUI))
            {
                StopDrawing();
                return;
            }

            // TRIGGER PRESSED - start drawing
            if (triggerValue > 0.5f && canDraw && !isOverUI && _currentStrokeInstance == null)
            {
                StartDrawing();
            }
            // TRIGGER HELD - update drawing
            else if (triggerValue > 0.5f && canDraw && _currentStrokeInstance != null)
            {
                UpdateDrawing();
            }
            // TRIGGER RELEASED - stop drawing
            else if (triggerValue < 0.1f && _currentStrokeInstance != null)
            {
                StopDrawing();
            }
            // Extra safety for digital button-style bindings
            if (action.WasReleasedThisFrame() && _currentStrokeInstance != null)
            {
                StopDrawing();
            }
            return;
        }

        // Standard brush mode (LineRenderer based)
        bool isFireStrokeActive = _currentStrokeInstance != null && _activeStroke == null && GetStrokeParticleSystem(_currentStrokeInstance) != null;

        if (isFireStrokeActive)
        {
            if (triggerValue > 0.5f && canDraw && !isOverUI)
            {
                UpdateFireBrushPosition();
                return;
            }

            if (triggerValue < 0.1f || !canDraw)
            {
                FinishActiveStroke();
                return;
            }
        }

        // Standard line renderer drawing
        if (triggerValue > 0.5f && _activeStroke == null && canDraw && !isOverUI)
        {
            StartStroke();
        }
        else if (triggerValue > 0.5f && _activeStroke != null && canDraw)
        {
            AddPointToActiveStroke();
        }
        else if (triggerValue < 0.1f && _activeStroke != null)
        {
            FinishActiveStroke();
        }

        if (action.WasReleasedThisFrame() && _activeStroke != null)
        {
            FinishActiveStroke();
        }
    }

    private void HandleJoystickSizeChange()
    {
        InputAction sizeAction = ActiveSizeChangeAction;
        if (sizeAction == null || uiController == null)
            return;

        Vector2 joystickInput = sizeAction.ReadValue<Vector2>();
        if (Mathf.Abs(joystickInput.x) <= 0.1f)
            return;

        float changeAmount = joystickInput.x * Time.deltaTime * sizeSensitivity;
        uiController.AdjustSliderValue(changeAmount);
    }

    private void UpdateBrushTipVisualScale()
    {
        if (brushTip == null || settings == null)
            return;

        // Convert brush radius to visual diameter for the tip sphere scale.
        float visualScale = settings.brushRadius * 2f;
        brushTip.localScale = Vector3.one * visualScale;
    }

    private void EmitSpray()
    {
        if (brushTip == null)
            return;

        if (sprayParticleSystem == null)
            sprayParticleSystem = brushTip.GetComponentInChildren<ParticleSystem>();

        if (sprayParticleSystem == null)
            return;

        _sprayAccumulator += sprayEmissionRate * Time.deltaTime;
        int emitCount = Mathf.FloorToInt(_sprayAccumulator);
        if (emitCount <= 0)
            return;

        _sprayAccumulator -= emitCount;
        sprayParticleSystem.Emit(emitCount);
    }

    private void StartStroke()
    {
        GameObject activePrefab = _activeBrushPrefab != null ? _activeBrushPrefab : (StandardBrushPrefab != null ? StandardBrushPrefab : strokePrefab);
        if (activePrefab == null || brushTip == null || brushMaterial == null || settings == null)
        {
            Debug.LogWarning("DrawingManager is missing an active brush prefab, brushTip, brushMaterial, or settings.");
            return;
        }

        _pointHistory.Clear();
        _currentStrokeInstance = Instantiate(activePrefab, brushTip.position, brushTip.rotation, artworkContainer != null ? artworkContainer : transform);

        // Parent every stroke under a shared container for clean hierarchy management.
        SetLayerRecursively(_currentStrokeInstance, LayerMask.NameToLayer("Drawing"));

        _activeStroke = _currentStrokeInstance.GetComponent<ProBrushStroke>();
        if (_activeStroke == null)
        {
            _activeStroke = null;

            ParticleSystem particleSystem = GetStrokeParticleSystem(_currentStrokeInstance);
            if (particleSystem != null)
            {
                UpdateFireBrushSize(settings.brushRadius);
                particleSystem.Play(true);

                if (enableFireDebugLogs)
                    Debug.Log($"<color=orange>[Fire]</color> Start stroke at {brushTip.position}");

                return;
            }

            Debug.LogWarning("The strokePrefab does not contain a ProBrushStroke or ParticleSystem component.");
            return;
        }
        _activeStroke.Initialize(brushMaterial, settings);
        SyncParticleToMesh(_currentStrokeInstance);
    }

    public void UpdateFireBrushSize(float newSize)
    {
        if (_currentStrokeInstance == null)
            return;

        ParticleSystem particleSystem = GetStrokeParticleSystem(_currentStrokeInstance);
        if (particleSystem == null)
            return;

        ParticleSystem.MainModule main = particleSystem.main;
        main.startSize = new ParticleSystem.MinMaxCurve(newSize);
        main.startSpeed = new ParticleSystem.MinMaxCurve(newSize * 2f);
    }

    private void UpdateFireBrushPosition()
    {
        if (_currentStrokeInstance == null)
            return;

        ParticleSystem particleSystem = GetStrokeParticleSystem(_currentStrokeInstance);
        if (particleSystem == null)
            return;

        _currentStrokeInstance.transform.position = brushTip.position;
        _currentStrokeInstance.transform.rotation = brushTip.rotation;

        if (settings != null)
            UpdateFireBrushSize(settings.brushRadius);

        if (particleSystem.isStopped)
            particleSystem.Play(true);

        if (enableFireDebugLogs && Time.frameCount % 60 == 0)
            Debug.Log($"<color=orange>[Fire]</color> Following tip at {brushTip.position}");
    }

    private ParticleSystem GetStrokeParticleSystem(GameObject strokeInstance)
    {
        if (strokeInstance == null)
            return null;

        ParticleSystem particleSystem = strokeInstance.GetComponent<ParticleSystem>();
        if (particleSystem != null)
            return particleSystem;

        return strokeInstance.GetComponentInChildren<ParticleSystem>();
    }

    private void SyncParticleToMesh(GameObject strokeObject)
    {
        MeshFilter mf = strokeObject.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
            return;

        ParticleSystem ps = strokeObject.GetComponent<ParticleSystem>();
        if (ps == null)
            return;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Mesh;
        shape.mesh = mf.sharedMesh;

        Debug.Log("<color=cyan>[Sync]</color> Particles now emitting from the drawing mesh.");
    }

    private void SetLayerRecursively(GameObject target, int layer)
    {
        if (target == null || layer < 0)
            return;

        target.layer = layer;

        foreach (Transform child in target.transform)
        {
            if (child != null)
                SetLayerRecursively(child.gameObject, layer);
        }
    }

    private void AddPointToActiveStroke()
    {
        if (_activeStroke == null || settings == null)
            return;

        // Keep in-progress stroke synced with shared settings while drawing.
        _activeStroke.radius = settings.brushRadius;
        MeshRenderer strokeRenderer = _activeStroke.GetComponent<MeshRenderer>();
        if (strokeRenderer != null)
        {
            strokeRenderer.material.color = settings.activeColor;

            if (uiController != null && strokeRenderer.material.HasProperty("_StretchAmount"))
                strokeRenderer.material.SetFloat("_StretchAmount", uiController.CurrentStretchAmount);
        }

        _activeStroke.AddPoint(brushTip.position, brushTip.rotation);
        SyncParticleToMesh(_currentStrokeInstance);
    }

    private void FinishActiveStroke()
    {
        if (_currentStrokeInstance == null)
            return;

        if (_activeStroke == null)
        {
            ParticleSystem particleSystem = GetStrokeParticleSystem(_currentStrokeInstance);
            if (particleSystem != null)
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            _currentStrokeInstance = null;
            return;
        }

        _activeStroke.FinalizeStrokeCollider();
        SyncParticleToMesh(_currentStrokeInstance);

        if (historyManager != null)
            historyManager.RecordAction(new StrokeAction(_activeStroke.gameObject, StrokeActionType.CreateStroke));

        _activeStroke = null;
        _currentStrokeInstance = null;
    }

    public void UndoLastStroke()
    {
        if (historyManager != null)
            historyManager.Undo();
    }

    public void RedoLastStroke()
    {
        if (historyManager != null)
            historyManager.Redo();
    }

    public void ClearAllStrokes()
    {
        if (selectionManager != null)
            selectionManager.ClearSelection();

        if (selectionManager != null)
            selectionManager.ClearAllContainers();

        if (historyManager != null)
            historyManager.ClearAll();

        _activeStroke = null;
        _currentStrokeInstance = null;
        _currentPS = null;
        _isParticleBrushActive = false;
    }

    private void PerformErase()
    {
        if (brushTip == null || settings == null)
            return;

        float eraserRadius = settings.brushRadius * Mathf.Max(0f, eraserRadiusMultiplier);
        if (eraserRadius <= 0f)
            return;

        int hitCount = Physics.OverlapSphereNonAlloc(
            brushTip.position,
            eraserRadius,
            _eraseHits,
            eraseLayerMask,
            eraseTriggerInteraction);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _eraseHits[i];
            if (hit == null)
                continue;

            Transform hitTransform = hit.transform;
            if (hitTransform != null && hitTransform.name.Contains("[Group_Container]"))
            {
                Destroy(hitTransform.gameObject);
                continue;
            }

            ProBrushStroke stroke = hit.GetComponentInParent<ProBrushStroke>();
            if (stroke == null)
                continue;

            GameObject strokeObject = stroke.gameObject;
            if (!strokeObject.activeSelf)
                continue;

            Transform strokeParent = strokeObject.transform.parent;
            bool parentIsGroup = strokeParent != null && strokeParent.name.Contains("[Group_Container]");

            strokeObject.SetActive(false);

            if (parentIsGroup && strokeParent.childCount <= 1)
                Destroy(strokeParent.gameObject);

            if (historyManager != null)
                historyManager.RecordAction(new StrokeAction(strokeObject, StrokeActionType.DeleteStroke));
        }
    }

    private void UpdateEraserVisual()
    {
        if (eraserVisual == null)
            return;

        bool show = currentTool == ToolMode.Erase;
        if (eraserVisual.activeSelf != show)
            eraserVisual.SetActive(show);

        if (!show || brushTip == null || settings == null)
            return;

        eraserVisual.transform.SetPositionAndRotation(brushTip.position, brushTip.rotation);
        float diameter = settings.brushRadius * Mathf.Max(0f, eraserRadiusMultiplier) * 2f;
        eraserVisual.transform.localScale = Vector3.one * diameter;
    }

    public void SetEraserMode(bool isErasing)
    {
        currentTool = isErasing ? ToolMode.Erase : ToolMode.Draw;
        Debug.Log("Switched to: " + currentTool);
    }

    // UI Button helper: 0 = Draw, 1 = Erase
    public void SetToolMode(int modeIndex)
    {
        if (modeIndex <= 0)
            currentTool = ToolMode.Draw;
        else
            currentTool = ToolMode.Erase;

        Debug.Log("Switched to: " + currentTool);
    }

    // Checkbox helper: checked = draw, unchecked = erase
    public void SetDrawModeActive(bool isDrawActive)
    {
        currentTool = isDrawActive ? ToolMode.Draw : ToolMode.Erase;
        Debug.Log("Switched to: " + currentTool);
    }

    public void SetDrawMode()
    {
        SetEraserMode(false);
    }

    // UI Toggle/Checkbox helper: true = Draw, false = Erase
    public void SetDrawMode(bool isDrawing)
    {
        currentTool = isDrawing ? ToolMode.Draw : ToolMode.Erase;
        Debug.Log("Switched to: " + currentTool);
    }

    public void SetEraseMode()
    {
        SetEraserMode(true);
    }

    // 1. TRIGGER PRESSED (Start)
    public void StartDrawing()
    {
        GameObject activePrefab = _activeBrushPrefab != null ? _activeBrushPrefab : (StandardBrushPrefab != null ? StandardBrushPrefab : strokePrefab);
        if (activePrefab == null || brushTip == null)
        {
            Debug.LogWarning("DrawingManager is missing an active brush prefab or brushTip.");
            return;
        }

        // Check if we are using fire or standard
        _isParticleBrushActive = activePrefab == fireBrushPrefab;

        _pointHistory.Clear();
        _activeStroke = null;
        _currentStrokeInstance = Instantiate(activePrefab, brushTip.position, brushTip.rotation, artworkContainer != null ? artworkContainer : transform);
        SetLayerRecursively(_currentStrokeInstance, LayerMask.NameToLayer("Drawing"));

        if (_isParticleBrushActive)
        {
            _currentPS = GetStrokeParticleSystem(_currentStrokeInstance);
            if (_currentPS == null)
                return;

            // Set initial size from slider
            var main = _currentPS.main;
            main.startSize = brushSizeSlider != null ? brushSizeSlider.value : settings != null ? settings.brushRadius : main.startSize.constant;

            var emission = _currentPS.emission;
            emission.enabled = true;
            _currentPS.Play(true);
        }
        else
        {
            _activeStroke = _currentStrokeInstance.GetComponent<ProBrushStroke>();
            if (_activeStroke != null && brushMaterial != null && settings != null)
            {
                _activeStroke.Initialize(brushMaterial, settings);
                SyncParticleToMesh(_currentStrokeInstance);
            }
        }
    }

    // 2. TRIGGER HELD (Continuous - MUST be in Update)
    public void UpdateDrawing()
    {
        if (_currentStrokeInstance == null) return;

        if (_isParticleBrushActive)
        {
            // THIS IS THE FIX: Physically drag the emitter through the air
            _currentStrokeInstance.transform.position = brushTip.position;
            _currentStrokeInstance.transform.rotation = brushTip.rotation;

            if (_currentPS != null && brushSizeSlider != null)
            {
                var main = _currentPS.main;
                main.startSize = brushSizeSlider.value;
            }

            // Safety: If you stop moving, Rate Over Distance stops spawning.
            // If you want it to always spawn while still, set main.startSize here too.
        }
        else
        {
            // Standard LineRenderer brush logic could go here
        }
    }

    // 3. TRIGGER RELEASED (End)
    public void StopDrawing()
    {
        if (_isParticleBrushActive && _currentPS != null)
        {
            // Stop emitting so the trail doesn't follow your hand to the UI
            var emission = _currentPS.emission;
            emission.enabled = false;
        }

        _currentStrokeInstance = null;
        _currentPS = null;
        _isParticleBrushActive = false;
    }

    // --- Bridge for your ColorPicker.cs ---
    public void OpenColorPicker()
    {
        if (settings == null)
        {
            Debug.LogWarning("DrawingManager settings reference is missing.");
            return;
        }

        ColorPicker.Create(settings.activeColor, "Choose Brush Color", SetColor, OnColorSelected, false);
    }

    private void SetColor(Color c)
    {
        if (settings != null)
            settings.activeColor = c;

        SyncBrushMaterialColor(c);
    }

    private void OnColorSelected(Color c)
    {
        if (settings != null)
            settings.activeColor = c;

        SyncBrushMaterialColor(c);
        Debug.Log("Color Selection Finalized");
    }

    private void SyncBrushMaterialColor(Color color)
    {
        if (brushMaterial == null)
            return;

        brushMaterial.color = color;

        if (brushMaterial.HasProperty("_BaseColor"))
            brushMaterial.SetColor("_BaseColor", color);

        if (brushMaterial.HasProperty("_Color"))
            brushMaterial.SetColor("_Color", color);
    }

    // Optional UI hooks for EventTrigger on Color Picker panel.
    public void OnColorPickerPointerEnter()
    {
        canDraw = false;
    }

    public void OnColorPickerPointerExit()
    {
        canDraw = true;
    }

    private void OnDrawGizmosSelected()
    {
        if (brushTip == null || settings == null || currentTool != ToolMode.Erase)
            return;

        Gizmos.color = new Color(0f, 0f, 0f, 0.35f);
        float radius = settings.brushRadius * Mathf.Max(0f, eraserRadiusMultiplier);
        Gizmos.DrawSphere(brushTip.position, radius);
    }
}
