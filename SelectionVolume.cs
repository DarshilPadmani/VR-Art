using UnityEngine;

public class SelectionVolume : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        ProBrushStroke stroke = other.GetComponentInParent<ProBrushStroke>();
        if (stroke == null)
            return;

        Transform container = transform.parent;
        if (container == null)
            return;

        stroke.transform.SetParent(container, true);
    }
}
