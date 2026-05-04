using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// All-in-One Directional Scaling System
/// 
/// Features:
/// - Switch between Uniform Scaling (all directions) and Directional Scaling (X/Y/Z axis)
/// - Works with custom NonUniformScaleTransformer based on two-hand grab distances
/// - Auto-detect scaling axis based on two-hand grab movement
/// - Apply axis constraints in real-time
/// - Visual debug display for mode and axis information
/// 
/// Usage:
/// 1. Assign Grabbable, GrabFreeTransformer, and NonUniformScaleTransformer in Inspector
/// 2. Long press Secondary Button (Button Two) on Right Controller to toggle modes
/// 3. In Directional Mode, use two hands to scale along specific axis
/// </summary>
public class TransformerSwitcher : MonoBehaviour
{
    #region ========== SERIALIZED FIELDS ==========

    [Header("Core References")]
    [SerializeField] private Grabbable grabbable;
    [SerializeField] private GrabFreeTransformer uniformTransformer;
    [SerializeField] private NonUniformScaleTransformer stretchTransformer;

    [Header("Mode Switching")]
    [SerializeField] private float longPressDuration = 0.5f;

    [Header("Directional Scaling")]
    [SerializeField] private float directionThreshold = 0.05f;
    [SerializeField] private bool snapToNearestAxis = true;

    [Header("Axis Constraint")]
    [SerializeField] private bool enableAxisConstraint = true;
    [SerializeField] private ScalingAxis scalingAxis = ScalingAxis.XYZ;
    [SerializeField] private bool autoDetectAxis = true;

    [Header("Debug Display")]
    [SerializeField] private bool showDebugConsole = true;
    [SerializeField] private bool showDebugUI = false;
    [SerializeField] private UnityEngine.UI.Text debugUIText;

    #endregion

    #region ========== ENUMS ==========

    public enum ScalingAxis
    {
        XYZ,    // Free scaling in all directions
        X,      // Scale only on X-axis
        Y,      // Scale only on Y-axis
        Z,      // Scale only on Z-axis
        XY,     // Scale on X and Y (2D plane)
        XZ,     // Scale on X and Z (2D plane)
        YZ      // Scale on Y and Z (2D plane)
    }

    #endregion

    #region ========== PRIVATE FIELDS ==========

    // Mode & Toggle State
    private float _buttonPressedTime;
    private bool _isUniformMode = true;
    private bool _wasButtonPressed;

    // Two-Hand Tracking (for NonUniformScaleTransformer)
    private Vector3 _grabStartPosLeft;
    private Vector3 _grabStartPosRight;
    private Vector3 _lastGrabPosLeft;
    private Vector3 _lastGrabPosRight;
    private bool _isGrabbing = false;
    private int _grabPointCount = 0;

    // Scale Constraint
    private Vector3 _scaleAtGrabStart;

    // Cached References
    private OVRCameraRig _cameraRig;

    #endregion

    #region ========== UNITY LIFECYCLE ==========

    private void Start()
    {
        // Cache camera rig reference
        _cameraRig = FindObjectOfType<OVRCameraRig>();

        if (grabbable == null)
            Debug.LogError("[TransformerSwitcher] Grabbable component not assigned!", gameObject);

        if (uniformTransformer == null)
            Debug.LogError("[TransformerSwitcher] GrabFreeTransformer not assigned!", gameObject);

        if (stretchTransformer == null)
            Debug.LogError("[TransformerSwitcher] NonUniformScaleTransformer not assigned!", gameObject);

        // Initialize with uniform mode
        ActivateUniformMode();
    }

    private void Update()
    {
        // Get right controller SECONDARY button input for mode toggle (Button Two)
        bool isButtonPressed = OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch);

        // ===== MODE TOGGLE LOGIC =====
        // Detect button press start
        if (isButtonPressed && !_wasButtonPressed)
        {
            _buttonPressedTime = Time.time;
        }
        // Detect long press
        else if (isButtonPressed && _wasButtonPressed)
        {
            if (Time.time - _buttonPressedTime >= longPressDuration)
            {
                ToggleMode();
                _buttonPressedTime = float.MaxValue; // Prevent repeated toggles
            }
        }

        // ===== GRAB TRACKING LOGIC (Multi-Hand) =====
        // Track grab points from Grabbable component
        if (grabbable != null && grabbable.GrabPoints.Count > 0)
        {
            _grabPointCount = grabbable.GrabPoints.Count;

            if (!_isGrabbing)
            {
                _isGrabbing = true;
                _scaleAtGrabStart = transform.localScale;
                CaptureGrabStartPositions();

                if (showDebugConsole)
                    Debug.Log($"[TransformerSwitcher] Grab started with {_grabPointCount} hand(s)");
            }

            UpdateGrabPositions();

            // Auto-detect axis based on grab point movement
            if (autoDetectAxis && !_isUniformMode && _grabPointCount >= 2)
            {
                AutoDetectScalingAxis();
            }
        }
        else if (_isGrabbing)
        {
            _isGrabbing = false;
            if (showDebugConsole)
                Debug.Log("[TransformerSwitcher] Grab ended");
        }

        _wasButtonPressed = isButtonPressed;

        // ===== APPLY CONSTRAINTS & DEBUG =====
        if (enableAxisConstraint && !_isUniformMode && _isGrabbing && _grabPointCount >= 2)
        {
            ApplyAxisConstraint();
        }

        if (showDebugConsole && _isGrabbing)
        {
            DebugLog();
        }

        if (showDebugUI && debugUIText != null)
        {
            UpdateDebugUI();
        }
    }

    private void LateUpdate()
    {
        // Ensure constraint is applied after all scaling calculations
        if (enableAxisConstraint && !_isUniformMode && _isGrabbing && _grabPointCount >= 2)
        {
            ApplyAxisConstraint();
        }
    }

    #endregion

    #region ========== MODE SWITCHING ==========

    private void ToggleMode()
    {
        if (grabbable == null) return;

        _isUniformMode = !_isUniformMode;

        if (_isUniformMode)
        {
            ActivateUniformMode();
        }
        else
        {
            ActivateDirectionalMode();
        }

        if (showDebugConsole)
        {
            Debug.Log($"[TransformerSwitcher] ========== MODE SWITCHED ==========");
            Debug.Log($"[TransformerSwitcher] Current Mode: {(_isUniformMode ? "UNIFORM SCALING" : "DIRECTIONAL SCALING")}");
            Debug.Log($"[TransformerSwitcher] =====================================");
        }
    }

    private void ActivateUniformMode()
    {
        if (uniformTransformer == null) return;

        ITransformer selectedTransformer = (ITransformer)uniformTransformer;

        // Re-initialize the transformer
        grabbable.InjectOptionalTwoGrabTransformer(selectedTransformer);
        selectedTransformer.Initialize(grabbable);

        if (showDebugConsole)
            Debug.Log("[TransformerSwitcher] ✓ Uniform Mode: Scale uniformly in all directions");
    }

    private void ActivateDirectionalMode()
    {
        if (stretchTransformer == null) return;

        ITransformer selectedTransformer = (ITransformer)stretchTransformer;

        // Re-initialize the transformer
        grabbable.InjectOptionalTwoGrabTransformer(selectedTransformer);
        selectedTransformer.Initialize(grabbable);

        if (showDebugConsole)
            Debug.Log("[TransformerSwitcher] ✓ Directional Mode: Scale along hand movement direction (X/Y/Z)");
    }

    #endregion

    #region ========== GRAB POINT TRACKING ==========

    /// <summary>
    /// Captures the initial grab point positions from the Grabbable component.
    /// Stores them for comparing movement and detecting scaling axis.
    /// </summary>
    private void CaptureGrabStartPositions()
    {
        if (grabbable.GrabPoints.Count >= 1)
        {
            _grabStartPosLeft = grabbable.GrabPoints[0].position;
            _lastGrabPosLeft = _grabStartPosLeft;
        }

        if (grabbable.GrabPoints.Count >= 2)
        {
            _grabStartPosRight = grabbable.GrabPoints[1].position;
            _lastGrabPosRight = _grabStartPosRight;
        }
    }

    /// <summary>
    /// Updates current grab point positions for tracking hand movement.
    /// </summary>
    private void UpdateGrabPositions()
    {
        if (grabbable.GrabPoints.Count >= 1)
        {
            _lastGrabPosLeft = grabbable.GrabPoints[0].position;
        }

        if (grabbable.GrabPoints.Count >= 2)
        {
            _lastGrabPosRight = grabbable.GrabPoints[1].position;
        }
    }

    /// <summary>
    /// Gets the average position between two grab points (two-hand grab center).
    /// </summary>
    private Vector3 GetGrabCenterPosition()
    {
        if (_grabPointCount < 2)
            return _lastGrabPosLeft;

        return (_lastGrabPosLeft + _lastGrabPosRight) * 0.5f;
    }

    /// <summary>
    /// Gets the distance between the two grab points.
    /// </summary>
    private float GetGrabPointDistance()
    {
        if (_grabPointCount < 2)
            return 0f;

        return Vector3.Distance(_lastGrabPosLeft, _lastGrabPosRight);
    }

    #endregion

    #region ========== SCALING DIRECTION DETECTION ==========

    /// <summary>
    /// Calculates the primary axis of hand movement based on two grab points.
    /// Works with the custom NonUniformScaleTransformer.
    /// </summary>
    public Vector3 GetScalingDirection()
    {
        if (_grabPointCount < 2)
            return Vector3.one;

        // Calculate movement of each hand
        Vector3 leftMovement = _lastGrabPosLeft - _grabStartPosLeft;
        Vector3 rightMovement = _lastGrabPosRight - _grabStartPosRight;

        // Calculate distance changes
        float initialDistance = Vector3.Distance(_grabStartPosLeft, _grabStartPosRight);
        float currentDistance = Vector3.Distance(_lastGrabPosLeft, _lastGrabPosRight);
        float distanceChange = Mathf.Abs(currentDistance - initialDistance);

        // Check axis-specific distances (matching NonUniformScaleTransformer logic)
        float initialDistanceX = Mathf.Abs(_grabStartPosLeft.x - _grabStartPosRight.x);
        float currentDistanceX = Mathf.Abs(_lastGrabPosLeft.x - _lastGrabPosRight.x);
        float changeX = Mathf.Abs(currentDistanceX - initialDistanceX);

        float initialDistanceY = Mathf.Abs(_grabStartPosLeft.y - _grabStartPosRight.y);
        float currentDistanceY = Mathf.Abs(_lastGrabPosLeft.y - _lastGrabPosRight.y);
        float changeY = Mathf.Abs(currentDistanceY - initialDistanceY);

        float initialDistanceZ = Mathf.Abs(_grabStartPosLeft.z - _grabStartPosRight.z);
        float currentDistanceZ = Mathf.Abs(_lastGrabPosLeft.z - _lastGrabPosRight.z);
        float changeZ = Mathf.Abs(currentDistanceZ - initialDistanceZ);

        // If movement is too small, return neutral
        if (distanceChange < directionThreshold)
        {
            return Vector3.one;
        }

        if (snapToNearestAxis)
        {
            // Snap to the axis with the largest distance change
            if (changeX > changeY && changeX > changeZ)
                return new Vector3(1, 0, 0); // X-axis scaling

            else if (changeY > changeX && changeY > changeZ)
                return new Vector3(0, 1, 0); // Y-axis scaling

            else if (changeZ > changeX && changeZ > changeY)
                return new Vector3(0, 0, 1); // Z-axis scaling
            else
                return Vector3.one;
        }
        else
        {
            // Return normalized movement direction
            Vector3 avgMovement = (leftMovement + rightMovement) * 0.5f;
            if (avgMovement.magnitude > directionThreshold)
                return avgMovement.normalized;
            else
                return Vector3.one;
        }
    }

    /// <summary>
    /// Auto-detect scaling axis based on two-hand grab point movement.
    /// Updates the ScalingAxis enum value.
    /// </summary>
    private void AutoDetectScalingAxis()
    {
        // Calculate distance changes on each axis
        float initialDistanceX = Mathf.Abs(_grabStartPosLeft.x - _grabStartPosRight.x);
        float currentDistanceX = Mathf.Abs(_lastGrabPosLeft.x - _lastGrabPosRight.x);
        float changeX = Mathf.Abs(currentDistanceX - initialDistanceX);

        float initialDistanceY = Mathf.Abs(_grabStartPosLeft.y - _grabStartPosRight.y);
        float currentDistanceY = Mathf.Abs(_lastGrabPosLeft.y - _lastGrabPosRight.y);
        float changeY = Mathf.Abs(currentDistanceY - initialDistanceY);

        float initialDistanceZ = Mathf.Abs(_grabStartPosLeft.z - _grabStartPosRight.z);
        float currentDistanceZ = Mathf.Abs(_lastGrabPosLeft.z - _lastGrabPosRight.z);
        float changeZ = Mathf.Abs(currentDistanceZ - initialDistanceZ);

        float maxChange = Mathf.Max(changeX, changeY, changeZ);

        if (maxChange < directionThreshold)
            return;

        ScalingAxis newAxis = ScalingAxis.XYZ;

        // Determine dominant axis
        if (changeX > changeY && changeX > changeZ)
        {
            newAxis = ScalingAxis.X;
        }
        else if (changeY > changeX && changeY > changeZ)
        {
            newAxis = ScalingAxis.Y;
        }
        else if (changeZ > changeX && changeZ > changeY)
        {
            newAxis = ScalingAxis.Z;
        }

        if (newAxis != scalingAxis)
        {
            scalingAxis = newAxis;
            if (showDebugConsole)
                Debug.Log($"[TransformerSwitcher] Auto-Detected Axis: {GetAxisName(newAxis)}");
        }
    }

    #endregion

    #region ========== AXIS CONSTRAINT ==========

    /// <summary>
    /// Constrains the object's scale to the specified axis.
    /// Locks other axes at their grab-start values.
    /// </summary>
    private void ApplyAxisConstraint()
    {
        Vector3 currentScale = transform.localScale;
        Vector3 constrainedScale = currentScale;

        switch (scalingAxis)
        {
            case ScalingAxis.X:
                constrainedScale.y = _scaleAtGrabStart.y;
                constrainedScale.z = _scaleAtGrabStart.z;
                break;

            case ScalingAxis.Y:
                constrainedScale.x = _scaleAtGrabStart.x;
                constrainedScale.z = _scaleAtGrabStart.z;
                break;

            case ScalingAxis.Z:
                constrainedScale.x = _scaleAtGrabStart.x;
                constrainedScale.y = _scaleAtGrabStart.y;
                break;

            case ScalingAxis.XY:
                constrainedScale.z = _scaleAtGrabStart.z;
                break;

            case ScalingAxis.XZ:
                constrainedScale.y = _scaleAtGrabStart.y;
                break;

            case ScalingAxis.YZ:
                constrainedScale.x = _scaleAtGrabStart.x;
                break;

            case ScalingAxis.XYZ:
            default:
                // No constraint
                break;
        }

        transform.localScale = constrainedScale;
    }

    /// <summary>
    /// Manually set the scaling axis.
    /// </summary>
    public void SetScalingAxis(ScalingAxis axis)
    {
        scalingAxis = axis;
        if (showDebugConsole)
            Debug.Log($"[TransformerSwitcher] Scaling axis set to: {axis}");
    }

    /// <summary>
    /// Get the current scaling axis.
    /// </summary>
    public ScalingAxis GetCurrentAxis()
    {
        return scalingAxis;
    }

    #endregion

    #region ========== DEBUG & UTILITIES ==========

    /// <summary>
    /// Returns a human-readable string of the current mode and axis.
    /// </summary>
    public string GetModeInfo()
    {
        string modeText = _isUniformMode ? "UNIFORM" : "DIRECTIONAL";
        string directionText = "";

        if (!_isUniformMode)
        {
            Vector3 scalingDir = GetScalingDirection();
            if (scalingDir == Vector3.one)
                directionText = " (No clear direction)";
            else if (scalingDir == new Vector3(1, 0, 0))
                directionText = " (X-Axis)";
            else if (scalingDir == new Vector3(0, 1, 0))
                directionText = " (Y-Axis)";
            else if (scalingDir == new Vector3(0, 0, 1))
                directionText = " (Z-Axis)";
        }

        return $"Mode: {modeText}{directionText}";
    }

    /// <summary>
    /// Convert ScalingAxis enum to readable string.
    /// </summary>
    private string GetAxisName(ScalingAxis axis)
    {
        return axis switch
        {
            ScalingAxis.X => "X-AXIS",
            ScalingAxis.Y => "Y-AXIS",
            ScalingAxis.Z => "Z-AXIS",
            ScalingAxis.XY => "XY-PLANE",
            ScalingAxis.XZ => "XZ-PLANE",
            ScalingAxis.YZ => "YZ-PLANE",
            ScalingAxis.XYZ => "FREE (XYZ)",
            _ => "UNKNOWN"
        };
    }

    /// <summary>
    /// Format Vector3 for debug display.
    /// </summary>
    private string FormatVector3(Vector3 vec)
    {
        return $"({vec.x:F2}, {vec.y:F2}, {vec.z:F2})";
    }

    /// <summary>
    /// Console debug logging (called every frame if enabled).
    /// </summary>
    private void DebugLog()
    {
        // Only log when grabbing or mode changes to reduce spam
        if (!_isGrabbing)
            return;

        // You can add periodic debug logs here if needed
        // Currently relies on key events logged elsewhere
    }

    /// <summary>
    /// Update UI debug display (called every frame if enabled).
    /// </summary>
    private void UpdateDebugUI()
    {
        if (debugUIText == null) return;

        string debugInfo = "";

        // Mode Information
        debugInfo += $"<b>Mode: {(_isUniformMode ? "UNIFORM" : "DIRECTIONAL")}</b>\n";

        // Grab Point Count
        debugInfo += $"Hands: {_grabPointCount}\n";

        if (_isGrabbing && _grabPointCount >= 2)
        {
            // Scaling Direction
            Vector3 scalingDir = GetScalingDirection();
            debugInfo += $"Direction: {FormatVector3(scalingDir)}\n";

            // Axis Detection
            string axisName = GetAxisName(scalingAxis);
            debugInfo += $"Axis: {axisName}\n";

            // Grab Distance
            float grabDist = GetCurrentGrabDistance();
            debugInfo += $"Distance: {grabDist:F3}m\n";
        }

        debugInfo += "\n";

        // Button Status
        bool isButtonPressed = OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch);
        debugInfo += $"Secondary Button: {(isButtonPressed ? "PRESSED" : "RELEASED")}\n";
        debugInfo += $"Grabbing: {(_isGrabbing ? "YES" : "NO")}\n";

        debugInfo += "\n<color=yellow><i>Long press Secondary Button (Btn2) to toggle</i></color>";

        debugUIText.text = debugInfo;
    }

    #endregion

    #region ========== INSPECTOR HELPERS ==========

    /// <summary>
    /// Public getter to check if currently in uniform mode.
    /// </summary>
    public bool IsUniformMode => _isUniformMode;

    /// <summary>
    /// Public getter to check if currently grabbing.
    /// </summary>
    public bool IsGrabbing => _isGrabbing;

    /// <summary>
    /// Public getter to check number of grab points (hands).
    /// </summary>
    public int GrabPointCount => _grabPointCount;

    /// <summary>
    /// Gets the hand movement vector (for single hand or average of two hands).
    /// </summary>
    public Vector3 GetHandMovement()
    {
        if (_grabPointCount < 2)
            return _lastGrabPosLeft - _grabStartPosLeft;
        
        Vector3 leftMovement = _lastGrabPosLeft - _grabStartPosLeft;
        Vector3 rightMovement = _lastGrabPosRight - _grabStartPosRight;
        return (leftMovement + rightMovement) * 0.5f;
    }

    /// <summary>
    /// Gets the distance between two grab points.
    /// </summary>
    public float GetCurrentGrabDistance()
    {
        return GetGrabPointDistance();
    }

    /// <summary>
    /// Gets the center position between two grab points.
    /// </summary>
    public Vector3 GetGrabCenter()
    {
        return GetGrabCenterPosition();
    }

    #endregion
}