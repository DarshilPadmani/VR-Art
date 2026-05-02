using UnityEngine;

public class Billboard : MonoBehaviour
{
    private Transform _mainCameraTransform;

    [Header("Settings")]
    [SerializeField] private bool _useVerticalAlignment = true;

    void Start()
    {
        // Cache the main camera transform for performance
        if (Camera.main != null)
        {
            _mainCameraTransform = Camera.main.transform;
        }
    }

    void LateUpdate()
    {
        if (_mainCameraTransform == null) return;

        // Calculate the direction from the canvas to the camera
        Vector3 targetDirection = transform.position - _mainCameraTransform.position;

        // If you don't want the UI to tilt up/down (keep it vertical)
        if (!_useVerticalAlignment)
        {
            targetDirection.y = 0;
        }

        // Rotate the canvas to face the calculated direction
        if (targetDirection != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(targetDirection);
        }
    }
}