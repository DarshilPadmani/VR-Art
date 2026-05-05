using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class SelectionManager : MonoBehaviour
{
    [Header("UI Controls - Rotation")]
    public Slider rotationSlider;
    public TMP_Text rotationValueText;
    public Toggle xToggle;
    public Toggle yToggle;
    public Toggle zToggle;

    [Header("UI Controls - Distance")]
    public Slider distanceSlider;
    public TMP_Text distanceValueText;

    [Header("UI Controls - Pivot")]
    public Toggle pivotCenterToggle;
    public Toggle pivotEndToggle;

    [Header("Core References")]
    public GameObject rightHandSphere;
    public DrawingHistoryManager historyManager;
    public Transform artworkContainer;

    [Header("Grouping Config")]
    public GameObject groupCubePrefab;
    public InputActionProperty leftPrimaryButton;
    public float requiredHoldTime = 1.0f;
    public float spawnScale = 0.37f;

    [Header("Animation & Defaults")]
    public float pulseAmount = 0.015f;
    public float pulseSpeed = 8f;
    public InputActionProperty primaryButtonRight;
    public InputActionProperty secondaryButtonRight;

    private readonly List<GameObject> _selectedStrokes = new List<GameObject>();
    private readonly Dictionary<GameObject, Vector3> _originalScales = new Dictionary<GameObject, Vector3>();

    private readonly HashSet<GameObject> _grabbedObjects = new HashSet<GameObject>();
    private readonly Dictionary<GrabInteractable, System.Action<InteractableStateChangeArgs>> _grabSubscriptions = new Dictionary<GrabInteractable, System.Action<InteractableStateChangeArgs>>();

    private Coroutine _pulseRoutine;
    private float _aButtonPressTime;
    private bool _isAButtonHeld;
    private bool _handledDirectGrabThisPress;
    private float _leftButtonPressTime;
    private bool _isLeftButtonPressed;

    public bool isSphereModeActive = false;

    private bool IsSphereSelectionActive()
    {
        return rightHandSphere != null ? rightHandSphere.activeSelf : isSphereModeActive;
    }

    private void Start()
    {
        if (rightHandSphere != null)
            rightHandSphere.SetActive(false);

        UpdateRotationText(rotationSlider != null ? rotationSlider.value : 0f);
        UpdateDistanceText(GetSpawnDistance());
        StartPulseRoutine();
    }

    private void OnEnable()
    {
        if (primaryButtonRight.action != null)
            primaryButtonRight.action.Enable();

        if (secondaryButtonRight.action != null)
            secondaryButtonRight.action.Enable();

        if (leftPrimaryButton.action != null)
            leftPrimaryButton.action.Enable();

        BindUiListeners();
        RefreshGrabSubscriptions();
        StartPulseRoutine();
    }

    private void OnDisable()
    {
        if (primaryButtonRight.action != null)
            primaryButtonRight.action.Disable();

        if (secondaryButtonRight.action != null)
            secondaryButtonRight.action.Disable();

        if (leftPrimaryButton.action != null)
            leftPrimaryButton.action.Disable();

        UnbindUiListeners();
        StopPulseRoutine();
        ClearGrabSubscriptions();
    }

    private void Update()
    {
        UpdateSphereModeFromSecondaryButton();
        HandlePrimaryButton();
        HandleGroupingHold();
    }

    private void HandleGroupingHold()
    {
        if (leftPrimaryButton.action == null)
            return;

        if (leftPrimaryButton.action.WasPressedThisFrame())
        {
            _leftButtonPressTime = Time.time;
            _isLeftButtonPressed = true;
        }

        if (_isLeftButtonPressed)
        {
            float holdDuration = Time.time - _leftButtonPressTime;
            if (holdDuration >= requiredHoldTime)
            {
                if (_selectedStrokes.Count > 0)
                    CreateGroupFromSelection();

                _isLeftButtonPressed = false;
            }
        }

        if (leftPrimaryButton.action.WasReleasedThisFrame())
            _isLeftButtonPressed = false;
    }

    private void BindUiListeners()
    {
        if (rotationSlider != null)
        {
            rotationSlider.onValueChanged.RemoveListener(UpdateRotationText);
            rotationSlider.onValueChanged.AddListener(UpdateRotationText);
            UpdateRotationText(rotationSlider.value);
        }

        if (distanceSlider != null)
        {
            distanceSlider.onValueChanged.RemoveListener(UpdateDistanceTextFromSlider);
            distanceSlider.onValueChanged.AddListener(UpdateDistanceTextFromSlider);
            UpdateDistanceText(GetSpawnDistance());
        }
    }

    private void UnbindUiListeners()
    {
        if (rotationSlider != null)
            rotationSlider.onValueChanged.RemoveListener(UpdateRotationText);

        if (distanceSlider != null)
            distanceSlider.onValueChanged.RemoveListener(UpdateDistanceTextFromSlider);
    }

    private void UpdateRotationText(float value)
    {
        if (rotationValueText != null)
            rotationValueText.text = value.ToString("F0") + "°";
    }

    public void OnRotationSliderChanged(float value)
    {
        UpdateRotationText(value);
    }

    private void UpdateDistanceTextFromSlider(float _)
    {
        UpdateDistanceText(GetSpawnDistance());
    }

    private void UpdateDistanceText(float value)
    {
        if (distanceValueText != null)
            distanceValueText.text = value.ToString("F2") + "m";
    }

    public void OnDistanceSliderChanged(float value)
    {
        UpdateDistanceText(value);
    }

    private void HandlePrimaryButton()
    {
        if (primaryButtonRight.action == null)
            return;

        if (primaryButtonRight.action.WasPressedThisFrame())
        {
            _aButtonPressTime = Time.time;
            _isAButtonHeld = true;
            _handledDirectGrabThisPress = false;

            if (!IsSphereSelectionActive() && _selectedStrokes.Count == 0 && TryDirectGrabDuplicate())
            {
                _handledDirectGrabThisPress = true;
                return;
            }
        }

        if (_isAButtonHeld && primaryButtonRight.action.WasReleasedThisFrame())
        {
            _isAButtonHeld = false;

            if (_handledDirectGrabThisPress)
                return;

            if (Time.time - _aButtonPressTime >= 1f)
            {
                ClearAllSelection();
                return;
            }

            ProcessDuplication();
        }
    }

    public void ProcessSphereTouch(GameObject stroke)
    {
        if (stroke == null || !IsSphereSelectionActive())
            return;

        AddObjectToSelection(stroke);
    }

    public void ProcessEraserTouch(GameObject stroke)
    {
        if (stroke == null || rightHandSphere == null)
            return;

        ProBrushStroke brushStroke = stroke.GetComponent<ProBrushStroke>();
        if (brushStroke == null)
            return;

        // Get brush settings from the manager or right hand sphere
        BrushSettings settings = FindObjectOfType<BrushUIController>()?.settings;
        
        if (settings == null)
        {
            // Fallback: Try to get from SelectionTrigger
            SelectionTrigger trigger = FindObjectOfType<SelectionTrigger>();
            if (trigger != null)
                settings = trigger.brushSettings;
        }

        if (settings == null)
            return;

        if (settings.eraseOnlyTouchedPoints)
        {
            // PARTIAL ERASE: Modify the mesh by erasing only the touched portion
            // Record undo action to capture mesh state before modification
            MeshModifyAction modifyAction = null;
            if (historyManager != null)
            {
                modifyAction = new MeshModifyAction(brushStroke);
            }

            float eraserRadius = rightHandSphere.transform.localScale.x / 2f;
            brushStroke.EraseAtPosition(rightHandSphere.transform.position, eraserRadius);

            // Capture state after modification for proper redo support
            if (modifyAction != null)
            {
                modifyAction.CaptureStateAfter();
                historyManager.RecordAction(modifyAction);
            }

            Debug.Log("<color=cyan>[Eraser]</color> Partial erase applied to stroke.");
        }
        else
        {
            // FULL ERASE: Destroy the entire stroke (original behavior)
            if (historyManager != null)
            {
                historyManager.RecordAction(new StrokeAction(stroke, StrokeActionType.DeleteStroke));
            }

            Destroy(stroke);
            Debug.Log("<color=cyan>[Eraser]</color> Full stroke erased and destroyed.");
        }
    }

    public void AutoSelectOnTouch(GameObject stroke)
    {
        ProcessSphereTouch(stroke);
    }

    public void ClearSelection()
    {
        ClearAllSelection();
    }

    private void ProcessDuplication()
    {
        List<GameObject> targets = new List<GameObject>();

        GameObject grabbed = GetAnyGrabbedObject();
        if (grabbed != null)
        {
            targets.Add(grabbed);
        }
        else if (_selectedStrokes.Count > 0)
        {
            targets = new List<GameObject>(_selectedStrokes);
        }

        if (targets.Count == 0)
            return;

        float spawnDistance = GetSpawnDistance();
        Vector3 rotationEuler = GetRotationEuler();
        int pivotMode = GetPivotMode();

        DuplicateAction action = new DuplicateAction(targets, artworkContainer, spawnDistance, rotationEuler, pivotMode);
        historyManager.RecordAction(action);

        ClearAllSelection();
        foreach (GameObject clone in action.GetClones())
            AddObjectToSelection(clone);
    }

    private bool TryDirectGrabDuplicate()
    {
        GameObject grabbedObject = GetAnyGrabbedObject();
        if (grabbedObject == null)
            return false;

        DuplicateAction action = new DuplicateAction(new List<GameObject> { grabbedObject }, artworkContainer, GetSpawnDistance(), GetRotationEuler(), GetPivotMode());
        historyManager.RecordAction(action);
        return true;
    }

    private GameObject GetAnyGrabbedObject()
    {
        foreach (GameObject grabbed in _grabbedObjects)
        {
            if (grabbed != null)
                return grabbed;
        }

        return null;
    }

    private void AddObjectToSelection(GameObject obj)
    {
        if (obj == null || _selectedStrokes.Contains(obj))
            return;

        _selectedStrokes.Add(obj);
        if (!_originalScales.ContainsKey(obj))
            _originalScales.Add(obj, obj.transform.localScale);
    }

    public void ClearAllSelection()
    {
        foreach (var item in _originalScales)
        {
            if (item.Key != null)
                item.Key.transform.localScale = item.Value;
        }

        _selectedStrokes.Clear();
        _originalScales.Clear();
    }

    private void CreateGroupFromSelection()
    {
        if (_selectedStrokes.Count == 0)
            return;

        if (groupCubePrefab == null || artworkContainer == null)
        {
            Debug.LogWarning("[Group] Missing groupCubePrefab or artworkContainer.");
            return;
        }

        Vector3 centerSum = Vector3.zero;
        int count = 0;
        foreach (var obj in _selectedStrokes)
        {
            if (obj == null)
                continue;

            Renderer renderer = obj.GetComponent<Renderer>();
            centerSum += renderer != null ? renderer.bounds.center : obj.transform.position;
            count++;
        }

        if (count == 0)
            return;

        Vector3 finalCenter = centerSum / count;

        GameObject newGroup = Instantiate(groupCubePrefab, finalCenter, Quaternion.identity, artworkContainer);
        newGroup.name = "[Group_Container]";
        newGroup.transform.localScale = new Vector3(spawnScale, spawnScale, spawnScale);

        foreach (GameObject stroke in _selectedStrokes)
        {
            if (stroke == null)
                continue;

            if (_originalScales.ContainsKey(stroke))
                stroke.transform.localScale = _originalScales[stroke];

            stroke.transform.SetParent(newGroup.transform);
        }

        Debug.Log($"<color=magenta>[Group]</color> Successfully grouped {count} items.");
        ClearAllSelection();
    }

    public void ClearAllContainers()
    {
        if (artworkContainer == null)
            return;

        List<GameObject> toDestroy = new List<GameObject>();
        foreach (Transform child in artworkContainer)
        {
            if (child != null && child.name.Contains("[Group_Container]"))
                toDestroy.Add(child.gameObject);
        }

        foreach (GameObject group in toDestroy)
            Destroy(group);
    }

    private float GetSpawnDistance()
    {
        if (distanceSlider == null)
            return 0f;

        return distanceSlider.value;
    }

    private Vector3 GetRotationEuler()
    {
        float degree = rotationSlider != null ? rotationSlider.value : 0f;
        UpdateRotationText(degree);

        return new Vector3(
            xToggle != null && xToggle.isOn ? degree : 0f,
            yToggle != null && yToggle.isOn ? degree : 0f,
            zToggle != null && zToggle.isOn ? degree : 0f);
    }

    private int GetPivotMode()
    {
        if (pivotCenterToggle != null && pivotCenterToggle.isOn)
            return 1;

        if (pivotEndToggle != null && pivotEndToggle.isOn)
            return 2;

        return 0;
    }

    private void RefreshGrabSubscriptions()
    {
        ClearGrabSubscriptions();

        GrabInteractable[] interactables = FindObjectsOfType<GrabInteractable>(true);
        foreach (GrabInteractable interactable in interactables)
        {
            if (interactable == null || _grabSubscriptions.ContainsKey(interactable))
                continue;

            System.Action<InteractableStateChangeArgs> handler = args => HandleGrabStateChanged(interactable, args);
            _grabSubscriptions.Add(interactable, handler);
            interactable.WhenStateChanged += handler;
        }
    }

    private void ClearGrabSubscriptions()
    {
        foreach (KeyValuePair<GrabInteractable, System.Action<InteractableStateChangeArgs>> entry in _grabSubscriptions)
        {
            if (entry.Key != null)
                entry.Key.WhenStateChanged -= entry.Value;
        }

        _grabSubscriptions.Clear();
        _grabbedObjects.Clear();
    }

    private void HandleGrabStateChanged(GrabInteractable grabInteractable, InteractableStateChangeArgs args)
    {
        if (grabInteractable == null)
            return;

        ProBrushStroke stroke = grabInteractable.gameObject.GetComponentInParent<ProBrushStroke>();
        GameObject grabbedObject = stroke != null ? stroke.gameObject : grabInteractable.gameObject;

        if (args.NewState == InteractableState.Select)
        {
            _grabbedObjects.Add(grabbedObject);
            return;
        }

        if (args.PreviousState == InteractableState.Select)
            _grabbedObjects.Remove(grabbedObject);
    }

    private void UpdateSphereModeFromSecondaryButton()
    {
        if (secondaryButtonRight.action == null)
            return;

        bool isHoldingSecondary = secondaryButtonRight.action.ReadValue<float>() > 0.1f;

        if (rightHandSphere != null)
            rightHandSphere.SetActive(isHoldingSecondary);

        isSphereModeActive = isHoldingSecondary;
    }

    private void StartPulseRoutine()
    {
        if (_pulseRoutine != null)
            return;

        _pulseRoutine = StartCoroutine(PulseSelectionRoutine());
    }

    private void StopPulseRoutine()
    {
        if (_pulseRoutine == null)
            return;

        StopCoroutine(_pulseRoutine);
        _pulseRoutine = null;
    }

    private IEnumerator PulseSelectionRoutine()
    {
        while (true)
        {
            if (_selectedStrokes.Count > 0)
            {
                float pulse = 1f + (Mathf.Sin(Time.time * pulseSpeed) * pulseAmount);
                foreach (GameObject stroke in _selectedStrokes)
                {
                    if (stroke != null && _originalScales.ContainsKey(stroke))
                        stroke.transform.localScale = _originalScales[stroke] * pulse;
                }
            }

            yield return null;
        }
    }
}