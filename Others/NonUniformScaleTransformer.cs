using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// Improved Non-Uniform Scale Transformer
/// 
/// Features:
/// - Scale X and Y axes independently based on two-hand grab distance
/// - Min/max scale constraints per axis
/// - Smooth scaling with optional easing
/// - Better error handling and validation
/// - Support for 1D and 2D scaling modes
/// - Maintains aspect ratio option
/// - Debug visualization
/// 
/// How it works:
/// The further apart your hands are, the larger the object scales.
/// Movement is based on the distance between grab points in X and Y axes.
/// </summary>
public class NonUniformScaleTransformer : MonoBehaviour, ITransformer
{
    #region ========== SERIALIZED FIELDS ==========

    [Header("Scale Constraints")]
    [SerializeField] private Vector3 minScale = new Vector3(0.1f, 0.1f, 0.1f);
    [SerializeField] private Vector3 maxScale = new Vector3(5f, 5f, 5f);

    [Header("Scaling Behavior")]
    [SerializeField] private bool enableXAxisScaling = true;
    [SerializeField] private bool enableYAxisScaling = true;
    [SerializeField] private bool enableZAxisScaling = false;
    [SerializeField] private bool maintainAspectRatio = false;
    [SerializeField] private float scaleSensitivity = 1f;

    [Header("Smoothing")]
    [SerializeField] private bool enableSmoothing = true;
    [SerializeField] private float smoothingFactor = 0.1f;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;
    [SerializeField] private bool visualizeGrabPoints = false;

    #endregion

    #region ========== PRIVATE FIELDS ==========

    private IGrabbable _grabbable;
    private Vector3 _initialLocalScale;
    private Vector3 _targetScale;
    private Vector3 _currentScale;

    // Grab point tracking
    private float _initialDistanceX;
    private float _initialDistanceY;
    private float _initialDistanceZ;

    // State
    private bool _isTransforming = false;
    private int _lastGrabPointCount = 0;

    #endregion

    #region ========== INITIALIZATION ==========

    public void Initialize(IGrabbable grabbable)
    {
        _grabbable = grabbable;

        if (_grabbable == null)
        {
            Debug.LogError("[NonUniformScaleTransformer] Grabbable is null!", gameObject);
            return;
        }

        _initialLocalScale = _grabbable.Transform.localScale;
        _currentScale = _initialLocalScale;
        _targetScale = _initialLocalScale;

        // Validate constraints
        ValidateConstraints();
    }

    #endregion

    #region ========== TRANSFORM LIFECYCLE ==========

    public void BeginTransform()
    {
        if (_grabbable == null || _grabbable.GrabPoints.Count < 2)
        {
            _isTransforming = false;
            return;
        }

        _isTransforming = true;
        _initialLocalScale = _grabbable.Transform.localScale;
        _currentScale = _initialLocalScale;
        _targetScale = _initialLocalScale;

        // Capture initial distances between grab points
        CaptureInitialDistances();

        if (showDebugInfo)
        {
            Debug.Log($"[NonUniformScaleTransformer] BeginTransform: {_grabbable.GrabPoints.Count} grab points");
            Debug.Log($"[NonUniformScaleTransformer] Initial Scale: {_initialLocalScale}");
            Debug.Log($"[NonUniformScaleTransformer] Initial Distances - X: {_initialDistanceX:F3}, Y: {_initialDistanceY:F3}, Z: {_initialDistanceZ:F3}");
        }
    }

    public void UpdateTransform()
    {
        if (!_isTransforming || _grabbable == null || _grabbable.GrabPoints.Count < 2)
            return;

        // Calculate scale factors based on current grab point distances
        CalculateAndApplyScale();

        // Apply smoothing if enabled
        if (enableSmoothing)
        {
            _currentScale = Vector3.Lerp(_currentScale, _targetScale, smoothingFactor);
        }
        else
        {
            _currentScale = _targetScale;
        }

        // Apply the calculated scale
        _grabbable.Transform.localScale = _currentScale;
    }

    public void EndTransform()
    {
        _isTransforming = false;
        _lastGrabPointCount = 0;

        if (showDebugInfo)
            Debug.Log("[NonUniformScaleTransformer] EndTransform");
    }

    #endregion

    #region ========== SCALING LOGIC ==========

    /// <summary>
    /// Captures the initial distances between grab points on each axis.
    /// </summary>
    private void CaptureInitialDistances()
    {
        if (_grabbable.GrabPoints.Count < 2)
            return;

        Vector3 p1 = _grabbable.GrabPoints[0].position;
        Vector3 p2 = _grabbable.GrabPoints[1].position;

        _initialDistanceX = Mathf.Abs(p1.x - p2.x);
        _initialDistanceY = Mathf.Abs(p1.y - p2.y);
        _initialDistanceZ = Mathf.Abs(p1.z - p2.z);

        // Prevent division by zero
        if (_initialDistanceX == 0) _initialDistanceX = 0.01f;
        if (_initialDistanceY == 0) _initialDistanceY = 0.01f;
        if (_initialDistanceZ == 0) _initialDistanceZ = 0.01f;
    }

    /// <summary>
    /// Calculates scale factors based on current grab point distances and applies constraints.
    /// </summary>
    private void CalculateAndApplyScale()
    {
        Vector3 p1 = _grabbable.GrabPoints[0].position;
        Vector3 p2 = _grabbable.GrabPoints[1].position;

        float currentDistX = Mathf.Abs(p1.x - p2.x);
        float currentDistY = Mathf.Abs(p1.y - p2.y);
        float currentDistZ = Mathf.Abs(p1.z - p2.z);

        // Calculate scale factors
        float scaleFactorX = currentDistX / _initialDistanceX;
        float scaleFactorY = currentDistY / _initialDistanceY;
        float scaleFactorZ = currentDistZ / _initialDistanceZ;

        // Apply sensitivity
        scaleFactorX = 1f + (scaleFactorX - 1f) * scaleSensitivity;
        scaleFactorY = 1f + (scaleFactorY - 1f) * scaleSensitivity;
        scaleFactorZ = 1f + (scaleFactorZ - 1f) * scaleSensitivity;

        // Calculate new scale
        Vector3 newScale = _initialLocalScale;

        if (enableXAxisScaling)
            newScale.x = _initialLocalScale.x * scaleFactorX;

        if (enableYAxisScaling)
            newScale.y = _initialLocalScale.y * scaleFactorY;

        if (enableZAxisScaling)
            newScale.z = _initialLocalScale.z * scaleFactorZ;

        // Apply aspect ratio constraint
        if (maintainAspectRatio && enableXAxisScaling && enableYAxisScaling)
        {
            float avgScaleFactor = (scaleFactorX + scaleFactorY) * 0.5f;
            newScale.x = _initialLocalScale.x * avgScaleFactor;
            newScale.y = _initialLocalScale.y * avgScaleFactor;
        }

        // Apply min/max constraints
        newScale = ClampScale(newScale);

        _targetScale = newScale;

        if (showDebugInfo)
        {
            Debug.Log($"[NonUniformScaleTransformer] Scale Factors - X: {scaleFactorX:F3}, Y: {scaleFactorY:F3}, Z: {scaleFactorZ:F3}");
            Debug.Log($"[NonUniformScaleTransformer] Target Scale: {_targetScale}");
        }
    }

    /// <summary>
    /// Clamps scale values to min/max constraints.
    /// </summary>
    private Vector3 ClampScale(Vector3 scale)
    {
        return new Vector3(
            Mathf.Clamp(scale.x, minScale.x, maxScale.x),
            Mathf.Clamp(scale.y, minScale.y, maxScale.y),
            Mathf.Clamp(scale.z, minScale.z, maxScale.z)
        );
    }

    #endregion

    #region ========== VALIDATION & SAFETY ==========

    /// <summary>
    /// Validates min/max scale constraints to prevent invalid values.
    /// </summary>
    private void ValidateConstraints()
    {
        // Ensure min is always less than max
        for (int i = 0; i < 3; i++)
        {
            if (minScale[i] >= maxScale[i])
            {
                maxScale[i] = minScale[i] + 1f;
                Debug.LogWarning($"[NonUniformScaleTransformer] Min scale >= max scale on axis {i}. Corrected max scale to {maxScale[i]}");
            }

            // Ensure positive scales
            if (minScale[i] <= 0)
            {
                minScale[i] = 0.01f;
                Debug.LogWarning($"[NonUniformScaleTransformer] Min scale must be positive. Set to 0.01");
            }
        }

        // Ensure smoothing factor is valid
        smoothingFactor = Mathf.Clamp01(smoothingFactor);

        // Ensure sensitivity is reasonable
        scaleSensitivity = Mathf.Max(0.1f, scaleSensitivity);
    }

    /// <summary>
    /// Checks if the transformer is in a valid state.
    /// </summary>
    private bool IsValid()
    {
        return _grabbable != null && _grabbable.Transform != null && _grabbable.GrabPoints != null;
    }

    #endregion

    #region ========== PUBLIC API ==========

    /// <summary>
    /// Sets the min and max scale constraints for all axes.
    /// </summary>
    public void SetScaleConstraints(Vector3 newMin, Vector3 newMax)
    {
        minScale = newMin;
        maxScale = newMax;
        ValidateConstraints();

        if (showDebugInfo)
            Debug.Log($"[NonUniformScaleTransformer] Scale constraints updated. Min: {minScale}, Max: {maxScale}");
    }

    /// <summary>
    /// Sets the scale constraint for a specific axis.
    /// </summary>
    public void SetAxisScaleConstraints(int axis, float min, float max)
    {
        if (axis < 0 || axis > 2)
        {
            Debug.LogError("[NonUniformScaleTransformer] Axis must be 0 (X), 1 (Y), or 2 (Z)");
            return;
        }

        minScale[axis] = min;
        maxScale[axis] = max;
        ValidateConstraints();
    }

    /// <summary>
    /// Enables or disables scaling on a specific axis.
    /// </summary>
    public void SetAxisScalingEnabled(int axis, bool enabled)
    {
        switch (axis)
        {
            case 0: // X
                enableXAxisScaling = enabled;
                break;
            case 1: // Y
                enableYAxisScaling = enabled;
                break;
            case 2: // Z
                enableZAxisScaling = enabled;
                break;
            default:
                Debug.LogError("[NonUniformScaleTransformer] Axis must be 0 (X), 1 (Y), or 2 (Z)");
                break;
        }
    }

    /// <summary>
    /// Gets the current scale of the grabbable object.
    /// </summary>
    public Vector3 GetCurrentScale()
    {
        return IsValid() ? _grabbable.Transform.localScale : Vector3.one;
    }

    /// <summary>
    /// Gets the initial scale before transformation started.
    /// </summary>
    public Vector3 GetInitialScale()
    {
        return _initialLocalScale;
    }

    /// <summary>
    /// Gets the scale factor for a specific axis (current / initial).
    /// </summary>
    public float GetScaleFactor(int axis)
    {
        if (!IsValid() || _initialLocalScale[axis] == 0)
            return 1f;

        return GetCurrentScale()[axis] / _initialLocalScale[axis];
    }

    /// <summary>
    /// Checks if the transformer is currently transforming (grabbing).
    /// </summary>
    public bool IsTransforming => _isTransforming;

    /// <summary>
    /// Gets the number of grab points.
    /// </summary>
    public int GrabPointCount => _grabbable != null ? _grabbable.GrabPoints.Count : 0;

    #endregion

    #region ========== DEBUG VISUALIZATION ==========

    private void OnDrawGizmosSelected()
    {
        if (!visualizeGrabPoints)
            return;

        // This is called in editor, only visualize if we have grabbable
        Grabbable grabbable = GetComponent<Grabbable>();
        if (grabbable == null || grabbable.GrabPoints.Count < 2)
            return;

        // Draw lines between grab points
        Vector3 p1 = grabbable.GrabPoints[0].position;
        Vector3 p2 = grabbable.GrabPoints[1].position;

        Gizmos.color = Color.green;
        Gizmos.DrawLine(p1, p2);

        Gizmos.color = Color.red;
        Gizmos.DrawSphere(p1, 0.02f);
        Gizmos.DrawSphere(p2, 0.02f);
    }

    #endregion

    #region ========== EDITOR DEBUG INFO ==========

    /// <summary>
    /// Returns debug information as a string.
    /// </summary>
    public string GetDebugInfo()
    {
        if (!IsValid())
            return "Transformer not initialized";

        string info = "";
        info += $"Status: {(_isTransforming ? "TRANSFORMING" : "IDLE")}\n";
        info += $"Grab Points: {GrabPointCount}\n";
        info += $"Current Scale: {GetCurrentScale()}\n";
        info += $"Initial Scale: {GetInitialScale()}\n";
        info += $"X Axis Enabled: {enableXAxisScaling}\n";
        info += $"Y Axis Enabled: {enableYAxisScaling}\n";
        info += $"Z Axis Enabled: {enableZAxisScaling}\n";
        info += $"Min Scale: {minScale}\n";
        info += $"Max Scale: {maxScale}\n";

        return info;
    }

    #endregion
}