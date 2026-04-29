using System.Collections.Generic;
using UnityEngine;

public class DrawingHistoryManager : MonoBehaviour
{
    private readonly Stack<IUndoAction> undoStack = new Stack<IUndoAction>();
    private readonly Stack<IUndoAction> redoStack = new Stack<IUndoAction>();

    public int UndoCount => undoStack.Count;
    public int RedoCount => redoStack.Count;

    public void RecordAction(IUndoAction action)
    {
        if (action == null)
            return;

        undoStack.Push(action);
        redoStack.Clear();
        Debug.Log($"<color=cyan>[History]</color> Action Recorded. Undo Count: {undoStack.Count}");
    }

    // Compatibility helper for existing call sites.
    public void RecordStroke(GameObject stroke)
    {
        RecordAction(new StrokeAction(stroke, StrokeActionType.CreateStroke));
    }

    // Compatibility helper for existing call sites.
    public void RecordDeletion(GameObject stroke)
    {
        RecordAction(new StrokeAction(stroke, StrokeActionType.DeleteStroke));
    }

    public void Undo()
    {
        if (undoStack.Count == 0)
            return;

        IUndoAction action = undoStack.Pop();
        action.Undo();
        redoStack.Push(action);
        Debug.Log("<color=yellow>[History]</color> Undo Performed.");
    }

    public void Redo()
    {
        if (redoStack.Count == 0)
            return;

        IUndoAction action = redoStack.Pop();
        action.Redo();
        undoStack.Push(action);
        Debug.Log("<color=green>[History]</color> Redo Performed.");
    }

    public void ClearAll()
    {
        // Remove all stroke objects currently in the scene.
        ProBrushStroke[] allStrokes = FindObjectsOfType<ProBrushStroke>(true);
        foreach (ProBrushStroke stroke in allStrokes)
        {
            if (stroke != null)
                Destroy(stroke.gameObject);
        }

        undoStack.Clear();
        redoStack.Clear();
    }
}
