using System.Collections.Generic;
using UnityEngine;

public interface IUndoAction
{
    void Undo();
    void Redo();
}

public sealed class CompositeUndoAction : IUndoAction
{
    private readonly List<IUndoAction> _actions = new List<IUndoAction>();

    public int Count => _actions.Count;

    public void AddAction(IUndoAction action)
    {
        if (action != null)
            _actions.Add(action);
    }

    public void Undo()
    {
        for (int i = _actions.Count - 1; i >= 0; i--)
            _actions[i].Undo();
    }

    public void Redo()
    {
        for (int i = 0; i < _actions.Count; i++)
            _actions[i].Redo();
    }
}

/// <summary>
/// Records a mesh modification (partial erase) for undo/redo support.
/// Captures the state before erasure and can restore it.
/// </summary>
public sealed class MeshModifyAction : IUndoAction
{
    private readonly ProBrushStroke _stroke;
    private readonly ProBrushStroke.MeshState _stateBefore;
    private ProBrushStroke.MeshState _stateAfter;

    public MeshModifyAction(ProBrushStroke stroke)
    {
        _stroke = stroke;
        // Capture state before modification
        _stateBefore = stroke != null ? stroke.CaptureMeshState() : null;
        _stateAfter = null;
    }

    public void CaptureStateAfter()
    {
        if (_stroke == null)
            return;
        _stateAfter = _stroke.CaptureMeshState();
    }

    public void Undo()
    {
        if (_stroke == null || _stateBefore == null)
            return;

        _stroke.RestoreMeshState(_stateBefore);
        Debug.Log("<color=yellow>[History]</color> Undo partial erase.");
    }

    public void Redo()
    {
        if (_stroke == null || _stateAfter == null)
            return;

        _stroke.RestoreMeshState(_stateAfter);
        Debug.Log("<color=green>[History]</color> Redo partial erase.");
    }
}

public sealed class PartialEraseAction : IUndoAction
{
    private readonly GameObject _originalStroke;
    private readonly List<GameObject> _splitPieces = new List<GameObject>();

    public PartialEraseAction(GameObject originalStroke, List<GameObject> splitPieces)
    {
        _originalStroke = originalStroke;

        if (splitPieces == null)
            return;

        foreach (GameObject piece in splitPieces)
        {
            if (piece != null)
                _splitPieces.Add(piece);
        }
    }

    public void Undo()
    {
        if (_originalStroke != null)
            _originalStroke.SetActive(true);

        foreach (GameObject piece in _splitPieces)
        {
            if (piece != null)
                piece.SetActive(false);
        }
    }

    public void Redo()
    {
        if (_originalStroke != null)
            _originalStroke.SetActive(false);

        foreach (GameObject piece in _splitPieces)
        {
            if (piece != null)
                piece.SetActive(true);
        }
    }
}
