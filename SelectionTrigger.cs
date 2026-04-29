using UnityEngine;

[DisallowMultipleComponent]
public class SelectionTrigger : MonoBehaviour
{
    public SelectionManager manager;

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
            manager.ProcessSphereTouch(other.gameObject);
        }
    }
}