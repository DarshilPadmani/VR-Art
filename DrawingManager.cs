using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class DrawingManager : MonoBehaviour
{
    public enum ToolMode { Draw, Erase }

    [Header("Setup")]
    public GameObject strokePrefab;
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

        // Start drawing only when not blocked by UI and drawing is allowed.
        if (triggerValue > 0.5f && _activeStroke == null && canDraw && !isOverUI)
        {
            StartStroke();
        }

        // Continue drawing while trigger is held and drawing is allowed.
        else if (triggerValue > 0.5f && _activeStroke != null && canDraw)
        {
            AddPointToActiveStroke();
        }

        // Stop drawing.
        else if (triggerValue < 0.1f && _activeStroke != null)
        {
            FinishActiveStroke();
        }

        // Extra safety for digital button-style bindings.
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
        if (strokePrefab == null || brushTip == null || brushMaterial == null || settings == null)
        {
            Debug.LogWarning("DrawingManager is missing strokePrefab, brushTip, brushMaterial, or settings.");
            return;
        }

        _pointHistory.Clear();
        GameObject strokeObj = Instantiate(strokePrefab, Vector3.zero, Quaternion.identity);

        // Parent every stroke under a shared container for clean hierarchy management.
        Transform container = artworkContainer != null ? artworkContainer : transform;
        strokeObj.transform.SetParent(container, true);

        _activeStroke = strokeObj.GetComponent<ProBrushStroke>();
        if (_activeStroke == null)
        {
            Debug.LogWarning("The strokePrefab does not contain a ProBrushStroke component.");
            return;
        }
        _activeStroke.Initialize(brushMaterial, settings);
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
    }

    private void FinishActiveStroke()
    {
        if (_activeStroke == null)
            return;

        _activeStroke.FinalizeStrokeCollider();

        if (historyManager != null)
            historyManager.RecordAction(new StrokeAction(_activeStroke.gameObject, StrokeActionType.CreateStroke));

        _activeStroke = null;
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

        if (historyManager != null)
            historyManager.ClearAll();

        _activeStroke = null;
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

            ProBrushStroke stroke = hit.GetComponentInParent<ProBrushStroke>();
            if (stroke == null)
                continue;

            GameObject strokeObject = stroke.gameObject;
            if (!strokeObject.activeSelf)
                continue;

            strokeObject.SetActive(false);

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
    }

    private void OnColorSelected(Color c)
    {
        if (settings != null)
            settings.activeColor = c;
        Debug.Log("Color Selection Finalized");
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