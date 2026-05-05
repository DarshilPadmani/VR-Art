using UnityEngine;

[DisallowMultipleComponent]
public class SelectionTrigger : MonoBehaviour
{
    public SelectionManager manager;
    public BrushSettings brushSettings;

    private void Awake()
    {
        if (manager == null)
            manager = FindObjectOfType<SelectionManager>();

        if (manager != null)
            Debug.Log("<color=white>[Physics]</color> Trigger bound to SelectionManager ID=" + manager.GetInstanceID());
    }

    private void OnTriggerEnter(Collider other)
    {
        if (manager == null || other == null)
            return;

        if (other.gameObject.layer == LayerMask.NameToLayer("Drawing"))
        {
            Debug.Log("<color=white>[Physics]</color> SUCCESS: Sphere touched drawing layer.");

            // Check if eraser mode is active
            if (brushSettings != null && brushSettings.isEraserMode)
            {
                manager.ProcessEraserTouch(other.gameObject);
            }
            else
            {
                manager.ProcessSphereTouch(other.gameObject);
            }
        }
    }
}