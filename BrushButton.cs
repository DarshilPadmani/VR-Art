using UnityEngine;

public class BrushButton : MonoBehaviour
{
    public DrawingManager drawingManager;

    [Header("Brush Data")]
    public Material brushMaterial;
    public string brushName;

    public void OnSelectBrush()
    {
        if (drawingManager == null)
        {
            Debug.LogWarning("BrushButton is missing a DrawingManager reference.");
            return;
        }

        if (brushMaterial != null)
            drawingManager.brushMaterial = brushMaterial;

        if (!string.IsNullOrWhiteSpace(brushName))
            drawingManager.ChangeBrushType(brushName);
    }
}
