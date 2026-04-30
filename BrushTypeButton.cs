using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class BrushTypeButton : MonoBehaviour
{
    private static readonly List<BrushTypeButton> Instances = new List<BrushTypeButton>();

    [Header("Brush Target")]
    public DrawingManager drawingManager;
    public string brushType = "standard";

    [Header("Visuals")]
    public Image targetImage;
    public Color selectedColor = Color.white;
    public Color unselectedColor = new Color(0.75f, 0.75f, 0.75f, 1f);

    private void Awake()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();
    }

    private void OnEnable()
    {
        if (!Instances.Contains(this))
            Instances.Add(this);
    }

    private void OnDisable()
    {
        Instances.Remove(this);
    }

    public void SelectBrush()
    {
        if (drawingManager != null)
            drawingManager.ChangeBrushType(brushType);

        SetSelectedInstance(this);
    }

    public void SetSelected(bool isSelected)
    {
        if (targetImage != null)
            targetImage.color = isSelected ? selectedColor : unselectedColor;
    }

    private static void SetSelectedInstance(BrushTypeButton selectedButton)
    {
        for (int index = 0; index < Instances.Count; index++)
        {
            BrushTypeButton button = Instances[index];
            if (button != null)
                button.SetSelected(button == selectedButton);
        }
    }
}
