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
