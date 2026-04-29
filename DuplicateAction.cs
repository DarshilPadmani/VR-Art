using System.Collections.Generic;
using UnityEngine;

public sealed class DuplicateAction : IUndoAction
{
    private readonly List<GameObject> _originals = new List<GameObject>();
    private readonly List<GameObject> _clones = new List<GameObject>();

    private readonly Transform _container;
    private readonly float _dist;
    private readonly Vector3 _rotEuler;
    private readonly int _pivotMode;

    public DuplicateAction(List<GameObject> originals, Transform container, float dist, Vector3 rotEuler, int pivotMode)
    {
        if (originals == null)
            return;

        // Store parameters so we can recreate clones on Redo after they were destroyed on Undo.
        _container = container;
        _dist = dist;
        _rotEuler = rotEuler;
        _pivotMode = pivotMode;

        foreach (GameObject obj in originals)
        {
            if (obj == null)
                continue;

            _originals.Add(obj);
        }

        CreateClones();
    }

    private void CreateClones()
    {
        foreach (GameObject obj in _originals)
        {
            if (obj == null)
                continue;

            GameObject clone = Object.Instantiate(obj, _container);
            clone.transform.localScale = obj.transform.localScale;

            Vector3 pivotPoint = GetCalculatedPivot(obj, _pivotMode);

            clone.transform.position = obj.transform.position + (obj.transform.forward * _dist);
            clone.transform.RotateAround(pivotPoint, obj.transform.right, _rotEuler.x);
            clone.transform.RotateAround(pivotPoint, obj.transform.up, _rotEuler.y);
            clone.transform.RotateAround(pivotPoint, obj.transform.forward, _rotEuler.z);

            clone.transform.localScale = obj.transform.localScale;
            _clones.Add(clone);
        }
    }

    private static Vector3 GetCalculatedPivot(GameObject obj, int pivotMode)
    {
        if (obj == null)
            return Vector3.zero;

        Vector3 pivotPoint = obj.transform.position;

        if (pivotMode == 1)
        {
            Renderer renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
                return renderer.bounds.center;
        }

        if (pivotMode == 2)
        {
            LineRenderer lineRenderer = obj.GetComponent<LineRenderer>();
            if (lineRenderer != null && lineRenderer.positionCount > 0)
                return obj.transform.TransformPoint(lineRenderer.GetPosition(lineRenderer.positionCount - 1));

            MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null && meshFilter.sharedMesh.vertexCount > 0)
            {
                Vector3 lastVertex = meshFilter.sharedMesh.vertices[meshFilter.sharedMesh.vertexCount - 1];
                return obj.transform.TransformPoint(lastVertex);
            }
        }

        return pivotPoint;
    }

    public List<GameObject> GetClones() => _clones;

    public void Undo()
    {
        // Destroy created clones so they are removed from scene and selection state.
        for (int i = _clones.Count - 1; i >= 0; i--)
        {
            GameObject clone = _clones[i];
            if (clone != null)
                Object.Destroy(clone);
        }

        _clones.Clear();
    }

    public void Redo()
    {
        // If clones were destroyed by Undo, recreate them.
        if (_clones.Count == 0)
            CreateClones();
        else
        {
            // Otherwise simply ensure they are active.
            foreach (GameObject clone in _clones)
            {
                if (clone != null)
                    clone.SetActive(true);
            }
        }
    }
}