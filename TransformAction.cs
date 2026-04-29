using UnityEngine;

public sealed class TransformAction : IUndoAction
{
    private readonly Transform _target;
    private readonly Vector3 _oldPos;
    private readonly Vector3 _newPos;
    private readonly Quaternion _oldRot;
    private readonly Quaternion _newRot;
    private readonly Vector3 _oldScale;
    private readonly Vector3 _newScale;

    public TransformAction(
        Transform target,
        Vector3 oldPos,
        Vector3 newPos,
        Quaternion oldRot,
        Quaternion newRot,
        Vector3 oldScale,
        Vector3 newScale)
    {
        _target = target;
        _oldPos = oldPos;
        _newPos = newPos;
        _oldRot = oldRot;
        _newRot = newRot;
        _oldScale = oldScale;
        _newScale = newScale;
    }

    public void Undo()
    {
        SetTransform(_oldPos, _oldRot, _oldScale);
    }

    public void Redo()
    {
        SetTransform(_newPos, _newRot, _newScale);
    }

    private void SetTransform(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        if (_target == null)
            return;

        _target.position = position;
        _target.rotation = rotation;
        _target.localScale = scale;
    }
}
