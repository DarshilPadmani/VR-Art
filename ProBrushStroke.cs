using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProBrushStroke : MonoBehaviour
{
    private Mesh _mesh;
    private MeshFilter _meshFilter;
    private List<Vector3> _points = new List<Vector3>();
    private List<Vector3> _vertices = new List<Vector3>();
    private List<int> _triangles = new List<int>();
    private List<Vector2> _uvs = new List<Vector2>();

    [Header("Professional Settings")]
    public float minDistance = 0.005f; 
    public int tubeSegments = 8; 
    public float radius = 0.02f;

    public void Initialize(Material baseMaterial, BrushSettings settings)
    {
        if (baseMaterial == null || settings == null)
        {
            Debug.LogWarning("ProBrushStroke.Initialize called with missing material or settings.");
            return;
        }

        _meshFilter = GetComponent<MeshFilter>();
        _mesh = new Mesh();
        _mesh.name = "ArtStroke";
        // Mark as dynamic for CPU-to-GPU performance
        _mesh.MarkDynamic(); 
        _meshFilter.mesh = _mesh;

        var renderer = GetComponent<MeshRenderer>();
        Material dynamicMat = new Material(baseMaterial);
        dynamicMat.color = settings.activeColor;

        // URP Lit shader property names.
        dynamicMat.SetFloat("_Smoothness", settings.smoothness);
        dynamicMat.SetFloat("_Metallic", settings.metallic);

        if (settings.emissionIntensity > 0f)
        {
            dynamicMat.EnableKeyword("_EMISSION");
            dynamicMat.SetColor("_EmissionColor", settings.emissionColor * settings.emissionIntensity);
        }
        else
        {
            dynamicMat.DisableKeyword("_EMISSION");
            dynamicMat.SetColor("_EmissionColor", Color.black);
        }

        renderer.material = dynamicMat;
        radius = settings.brushRadius;
    }

    public void AddPoint(Vector3 position, Quaternion rotation)
    {
        if (_points.Count > 0 && Vector3.Distance(position, _points[_points.Count - 1]) < minDistance)
            return;

        _points.Add(position);
        
        // Industry Secret: We use the controller's rotation to orient the "ring" 
        // so the stroke doesn't twist unnaturally.
        GenerateRing(position, rotation);
        UpdateMesh();
    }

    private void GenerateRing(Vector3 center, Quaternion rotation)
    {
        for (int i = 0; i < tubeSegments; i++)
        {
            float angle = i * Mathf.PI * 2 / tubeSegments;
            // Calculate vertex position based on the controller's rotation
            Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0);
            Vector3 vertexPos = center + (rotation * offset);
            
            _vertices.Add(transform.InverseTransformPoint(vertexPos));
            
            // Generate UVs so you can apply textures/gradients later
            _uvs.Add(new Vector2((float)i / tubeSegments, (float)_points.Count / 10f));
        }

        if (_points.Count > 1)
        {
            int baseIdx = (_points.Count - 2) * tubeSegments;
            int currIdx = (_points.Count - 1) * tubeSegments;

            for (int i = 0; i < tubeSegments; i++)
            {
                int next = (i + 1) % tubeSegments;
                _triangles.Add(baseIdx + i);
                _triangles.Add(baseIdx + next);
                _triangles.Add(currIdx + i);

                _triangles.Add(baseIdx + next);
                _triangles.Add(currIdx + next);
                _triangles.Add(currIdx + i);
            }
        }
    }

    private void UpdateMesh()
    {
        _mesh.SetVertices(_vertices);
        _mesh.SetTriangles(_triangles, 0);
        _mesh.SetUVs(0, _uvs);
        _mesh.RecalculateNormals(); // Essential for URP Lighting
    }
}