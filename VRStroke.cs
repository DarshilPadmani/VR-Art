using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class VRStroke : MonoBehaviour
{
    private Mesh _mesh;
    private List<Vector3> _points = new List<Vector3>();
    private List<int> _triangles = new List<int>();
    private List<Vector3> _vertices = new List<Vector3>();
    private List<Vector2> _uvs = new List<Vector2>();

    private float _width;
    private int _radialSegments;
    private Color _color;

    public void Initialize(float width, int segments, Color color, Material material)
    {
        _mesh = new Mesh();
        _mesh.name = "StrokeMesh";
        GetComponent<MeshFilter>().mesh = _mesh;
        GetComponent<MeshRenderer>().material = material;
        GetComponent<MeshRenderer>().material.color = color;

        _width = width;
        _radialSegments = segments;
        _color = color;
    }

    public void AddPoint(Vector3 position, Quaternion rotation)
    {
        _points.Add(position);
        GenerateTubeSegment(position, rotation);
        UpdateMesh();
    }

    private void GenerateTubeSegment(Vector3 position, Quaternion rotation)
    {
        int vertIndex = _vertices.Count;

        for (int i = 0; i <= _radialSegments; i++)
        {
            float angle = (float)i / _radialSegments * Mathf.PI * 2f;
            Vector3 offset = new Vector3(Mathf.Cos(angle) * _width, Mathf.Sin(angle) * _width, 0);
            _vertices.Add(position + (rotation * offset));
            
            float u = (float)i / _radialSegments;
            float v = _points.Count * 0.1f; 
            _uvs.Add(new Vector2(u, v));

            if (_points.Count > 1 && i < _radialSegments)
            {
                int nextVert = vertIndex + i;
                int prevVert = nextVert - (_radialSegments + 1);

                _triangles.Add(prevVert);
                _triangles.Add(nextVert + 1);
                _triangles.Add(nextVert);

                _triangles.Add(prevVert);
                _triangles.Add(prevVert + 1);
                _triangles.Add(nextVert + 1);
            }
        }
    }

    private void UpdateMesh()
    {
        _mesh.Clear();
        _mesh.SetVertices(_vertices);
        _mesh.SetTriangles(_triangles, 0);
        _mesh.SetUVs(0, _uvs);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }
}