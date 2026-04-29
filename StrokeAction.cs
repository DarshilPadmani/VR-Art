using UnityEngine;

public enum StrokeActionType
{
    CreateStroke,
    DeleteStroke
}

public sealed class StrokeAction : IUndoAction
{
    private readonly GameObject _stroke;
    private readonly StrokeActionType _type;

    public StrokeAction(GameObject stroke, StrokeActionType type)
    {
        _stroke = stroke;
        _type = type;
    }

    public void Undo()
    {
        if (_stroke == null)
            return;

        bool shouldBeActive = _type == StrokeActionType.DeleteStroke;
        _stroke.SetActive(shouldBeActive);
    }

    public void Redo()
    {
        if (_stroke == null)
            return;

        bool shouldBeActive = _type == StrokeActionType.CreateStroke;
        _stroke.SetActive(shouldBeActive);
    }
}
