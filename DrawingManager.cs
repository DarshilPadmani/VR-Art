using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class DrawingManager : MonoBehaviour
{
    [Header("Setup")]
    public GameObject strokePrefab;
    public Transform brushTip; // The point on your right controller
    public Material brushMaterial;

    [Header("Input Actions")]
    public InputActionProperty drawAction; // Map to Right Controller Trigger/Press
    [Tooltip("If drawAction is not assigned, use Meta right joystick click as fallback.")]
    public bool useFallbackRightJoystickClick = true;

    [Header("Shared Settings")]
    public BrushSettings settings;

    [Header("UI Interaction")]
    public EventSystem eventSystem;

    [Header("Smoothing")]
    [Min(1)] public int smoothingWindowSize = 6;

    private ProBrushStroke _activeStroke;
    private readonly List<Vector3> _pointHistory = new List<Vector3>();
    private InputAction _fallbackDrawAction;

    private InputAction ActiveDrawAction => drawAction.action != null ? drawAction.action : _fallbackDrawAction;

    private void Awake()
    {
        if (drawAction.action == null && useFallbackRightJoystickClick)
        {
            _fallbackDrawAction = new InputAction(
                "Draw",
                InputActionType.Button,
                "<XRController>{RightHand}/primary2DAxisClick");
        }
    }

    private void OnEnable()
    {
        if (ActiveDrawAction != null)
            ActiveDrawAction.Enable();
    }

    private void OnDisable()
    {
        if (ActiveDrawAction != null)
            ActiveDrawAction.Disable();
    }

    private void OnDestroy()
    {
        if (_fallbackDrawAction != null)
            _fallbackDrawAction.Dispose();
    }

    void Update()
    {
        InputAction action = ActiveDrawAction;
        if (action == null)
            return;

        // Read the trigger value directly so analog trigger input is handled consistently.
        float triggerValue = action.ReadValue<float>();
        bool isOverUI = eventSystem != null && eventSystem.IsPointerOverGameObject(-1);

        // Start drawing if trigger is pressed more than halfway and pointer is not on UI.
        if (triggerValue > 0.5f && _activeStroke == null && !isOverUI)
        {
            StartStroke();
        }

        // Continue drawing.
        else if (triggerValue > 0.5f && _activeStroke != null)
        {
            AddPointToActiveStroke();
        }

        // Stop drawing.
        else if (triggerValue < 0.1f && _activeStroke != null)
        {
            _activeStroke = null;
        }
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
            strokeRenderer.material.color = settings.activeColor;

        _activeStroke.AddPoint(brushTip.position, brushTip.rotation);
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
}