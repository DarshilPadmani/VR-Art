using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProBrushStroke : MonoBehaviour
{
    private Mesh _mesh;
    private MeshFilter _meshFilter;
    private List<Vector3> _points = new List<Vector3>();
    private List<Vector3> _vertices = new List<Vector3>();
    private List<int> _triangles = new List<int>();
    private List<Vector2> _uvs = new List<Vector2>();
    private BrushSettings _cachedSettings;
    private BrushSettings.BrushShape _currentShape = BrushSettings.BrushShape.Round;
    private float _cumulativeDistance = 0f;
    private Vector3 _lastPointPosition;

    [Header("Professional Settings")]
    public float minDistance = 0.005f; 
    public int tubeSegments = 8; 
    public float radius = 0.02f;
    
    [Header("Artist Stroke Tracking")]
    private float _totalStrokeLength = 0f;
    private float _estimatedMaxStrokeLength = 1f; // Updated as stroke progresses

    public void Initialize(Material baseMaterial, BrushSettings settings)
    {
        if (baseMaterial == null || settings == null)
        {
            Debug.LogWarning("ProBrushStroke.Initialize called with missing material or settings.");
            return;
        }

        _cachedSettings = settings;
        _currentShape = settings.activeShape;
        _meshFilter = GetComponent<MeshFilter>();
        _mesh = new Mesh();
        _mesh.name = "ArtStroke";
        // Mark as dynamic for CPU-to-GPU performance
        _mesh.MarkDynamic(); 
        _meshFilter.mesh = _mesh;

        var renderer = GetComponent<MeshRenderer>();
        Material dynamicMat = new Material(baseMaterial);
        dynamicMat.color = settings.activeColor;

        if (dynamicMat.HasProperty("_BaseMap"))
        {
            if (settings.HasTexture)
            {
                dynamicMat.SetTexture("_BaseMap", settings.brushTexture);
                dynamicMat.SetTextureScale("_BaseMap", settings.tiling);
                dynamicMat.EnableKeyword("_USE_TEXTURE_ON");
            }
            else
            {
                dynamicMat.SetTexture("_BaseMap", null);
                dynamicMat.DisableKeyword("_USE_TEXTURE_ON");
            }
        }

        // 1) Metallic workflow or Specular workflow.
        if (settings.workflow == WorkflowMode.Metallic)
        {
            dynamicMat.SetFloat("_Metallic", settings.metallic);
            dynamicMat.DisableKeyword("_SPECULAR_SETUP");
        }
        else
        {
            dynamicMat.SetColor("_SpecColor", Color.white * settings.metallic);
            dynamicMat.EnableKeyword("_SPECULAR_SETUP");
        }

        // 2) Opaque or Transparent surface setup for URP Lit.
        if (settings.surface == SurfaceMode.Transparent)
        {
            dynamicMat.SetFloat("_Surface", 1f);
            dynamicMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            dynamicMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            dynamicMat.SetInt("_ZWrite", 0);
            dynamicMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            dynamicMat.renderQueue = (int)RenderQueue.Transparent;
        }
        else
        {
            dynamicMat.SetFloat("_Surface", 0f);
            dynamicMat.SetInt("_SrcBlend", (int)BlendMode.One);
            dynamicMat.SetInt("_DstBlend", (int)BlendMode.Zero);
            dynamicMat.SetInt("_ZWrite", 1);
            dynamicMat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            dynamicMat.renderQueue = (int)RenderQueue.Geometry;
        }

        dynamicMat.SetFloat("_Smoothness", settings.smoothness);

        if (dynamicMat.HasProperty("_Cull"))
            dynamicMat.SetFloat("_Cull", (float)settings.renderFace);

        if (settings.isElectric)
        {
            dynamicMat.EnableKeyword("_EMISSION");
            dynamicMat.SetColor("_EmissionColor", settings.emissionColor * Mathf.LinearToGammaSpace(settings.emissionIntensity));
        }
        else
        {
            dynamicMat.DisableKeyword("_EMISSION");
            dynamicMat.SetColor("_EmissionColor", Color.black);
        }

        renderer.material = dynamicMat;
        radius = settings.brushRadius;
    }

    public void EnsureUniqueMaterialInstance()
    {
        MeshRenderer renderer = GetComponent<MeshRenderer>();
        if (renderer == null || renderer.sharedMaterial == null)
            return;

        renderer.material = new Material(renderer.material);
    }

    public void SetSelectionHighlight(bool active)
    {
        MeshRenderer renderer = GetComponent<MeshRenderer>();
        if (renderer == null)
            return;

        Material runtimeMaterial = renderer.material;
        if (runtimeMaterial == null)
            return;

        if (runtimeMaterial.HasProperty("_SelectionGlow"))
            runtimeMaterial.SetFloat("_SelectionGlow", active ? 1.0f : 0.0f);
    }

    public void AddPoint(Vector3 position, Quaternion rotation)
    {
        if (_points.Count > 0 && Vector3.Distance(position, _points[_points.Count - 1]) < minDistance)
            return;

        if (_points.Count > 0)
        {
            _cumulativeDistance += Vector3.Distance(position, _lastPointPosition);
        }

        _lastPointPosition = position;

        // Track stroke progression for taper calculation
        if (_points.Count > 0)
        {
            float segmentLength = Vector3.Distance(position, _points[_points.Count - 1]);
            _totalStrokeLength += segmentLength;
            _estimatedMaxStrokeLength = Mathf.Max(_estimatedMaxStrokeLength, _totalStrokeLength * 1.5f);
        }

        _points.Add(position);
        
        // Artist Logic: Calculate taper multiplier based on stroke progress
        float currentStrokePercentage = Mathf.Clamp01(_totalStrokeLength / _estimatedMaxStrokeLength);
        float taperMultiplier = _cachedSettings != null ? _cachedSettings.edgeProfile.Evaluate(currentStrokePercentage) : 1f;
        float finalRadius = radius * taperMultiplier;

        // Generate geometry based on active brush shape
        if (_cachedSettings != null)
        {
            switch (_currentShape)
            {
                case BrushSettings.BrushShape.Flat:
                    GenerateFlatVertices(position, rotation, finalRadius);
                    break;
                case BrushSettings.BrushShape.Ribbon:
                    GenerateRibbonVertices(position, rotation, finalRadius);
                    break;
                case BrushSettings.BrushShape.Oil:
                    GenerateOilVertices(position, rotation, finalRadius);
                    break;
                case BrushSettings.BrushShape.Tapered:
                case BrushSettings.BrushShape.Calligraphy:
                    GenerateRoundVertices(position, rotation, finalRadius);
                    break;
                case BrushSettings.BrushShape.Round:
                default:
                    GenerateRoundVertices(position, rotation, finalRadius);
                    break;
            }
        }
        else
        {
            GenerateRoundVertices(position, rotation, finalRadius);
        }

        UpdateMesh();
    }

    /// <summary>
    /// Generate a two-vertex ribbon strip for brush strokes that should feel flatter and wider.
    /// </summary>
    private void GenerateRibbonVertices(Vector3 center, Quaternion rotation, float radiusMultiplied)
    {
        float halfWidth = radiusMultiplied * Mathf.Max(0.1f, _cachedSettings != null ? _cachedSettings.flatBrushAspectRatio : 2f) * 0.5f;
        float vCoord = GetScrollVCoord();

        Vector3 right = rotation * Vector3.right * halfWidth;
        Vector3 up = rotation * Vector3.up * radiusMultiplied * 0.35f;

        _vertices.Add(transform.InverseTransformPoint(center + right + up));
        _vertices.Add(transform.InverseTransformPoint(center - right + up));

        _uvs.Add(new Vector2(0f, vCoord));
        _uvs.Add(new Vector2(1f, vCoord));

        if (_points.Count > 1)
        {
            int baseIdx = (_points.Count - 2) * 2;
            int currIdx = (_points.Count - 1) * 2;

            _triangles.Add(baseIdx + 0);
            _triangles.Add(baseIdx + 1);
            _triangles.Add(currIdx + 0);

            _triangles.Add(baseIdx + 1);
            _triangles.Add(currIdx + 1);
            _triangles.Add(currIdx + 0);
        }
    }

    /// <summary>
    /// Generate multiple overlapping ribbons to mimic oil paint depth and bristle breakup.
    /// </summary>
    private void GenerateOilVertices(Vector3 center, Quaternion rotation, float radiusMultiplied)
    {
        const int oilLayers = 3;
        const float layerAngleStep = 60f;

        float vCoord = GetScrollVCoord();
        float halfWidth = radiusMultiplied;

        for (int i = 0; i < oilLayers; i++)
        {
            Quaternion layerRotation = rotation * Quaternion.Euler(0f, 0f, i * layerAngleStep);
            Vector3 right = layerRotation * Vector3.right * halfWidth;

            _vertices.Add(transform.InverseTransformPoint(center + right));
            _vertices.Add(transform.InverseTransformPoint(center - right));

            _uvs.Add(new Vector2(0f, vCoord));
            _uvs.Add(new Vector2(1f, vCoord));
        }

        if (_points.Count > 1)
        {
            int verticesPerRing = oilLayers * 2;
            int baseIdx = (_points.Count - 2) * verticesPerRing;
            int currIdx = (_points.Count - 1) * verticesPerRing;

            for (int layer = 0; layer < oilLayers; layer++)
            {
                int layerOffset = layer * 2;

                _triangles.Add(baseIdx + layerOffset + 0);
                _triangles.Add(baseIdx + layerOffset + 1);
                _triangles.Add(currIdx + layerOffset + 0);

                _triangles.Add(baseIdx + layerOffset + 1);
                _triangles.Add(currIdx + layerOffset + 1);
                _triangles.Add(currIdx + layerOffset + 0);
            }
        }
    }

    /// <summary>
    /// Generate a circular ring of vertices for Round and Tapered brush shapes.
    /// </summary>
    private void GenerateRoundVertices(Vector3 center, Quaternion rotation, float radiusMultiplied)
    {
        float vCoord = GetScrollVCoord();

        for (int i = 0; i < tubeSegments; i++)
        {
            float angle = i * Mathf.PI * 2 / tubeSegments;
            // Calculate vertex position based on the controller's rotation
            Vector3 offset = new Vector3(Mathf.Cos(angle) * radiusMultiplied, Mathf.Sin(angle) * radiusMultiplied, 0);
            Vector3 vertexPos = center + (rotation * offset);
            
            _vertices.Add(transform.InverseTransformPoint(vertexPos));
            
            // Generate UVs so you can apply textures/gradients later
            _uvs.Add(new Vector2((float)i / tubeSegments, vCoord));
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

    /// <summary>
    /// Generate a flat ribbon for Flat brush shape (like a real flat/angular brush).
    /// Uses rotation to determine the orientation of the flat edge.
    /// </summary>
    private void GenerateFlatVertices(Vector3 center, Quaternion rotation, float radiusMultiplied)
    {
        // For a flat brush, we create a rectangle instead of a circle
        // The number of vertices per ring is 4 (corners of a rectangle)
        float width = radiusMultiplied * (_cachedSettings != null ? _cachedSettings.flatBrushAspectRatio : 2f);
        float height = radiusMultiplied;
        float vCoord = GetScrollVCoord();

        // Create 4 corner vertices in local space
        Vector3[] corners = new Vector3[4]
        {
            new Vector3(-width / 2, -height / 2, 0),
            new Vector3(width / 2, -height / 2, 0),
            new Vector3(width / 2, height / 2, 0),
            new Vector3(-width / 2, height / 2, 0)
        };

        // Transform corners by controller rotation and position
        for (int i = 0; i < 4; i++)
        {
            Vector3 rotatedCorner = center + (rotation * corners[i]);
            _vertices.Add(transform.InverseTransformPoint(rotatedCorner));
            _uvs.Add(new Vector2((float)i / 4f, vCoord));
        }

        // Create triangles for the flat rectangle
        if (_points.Count > 1)
        {
            int baseIdx = (_points.Count - 2) * 4;
            int currIdx = (_points.Count - 1) * 4;

            // Two triangles per quad side
            _triangles.Add(baseIdx + 0);
            _triangles.Add(baseIdx + 1);
            _triangles.Add(currIdx + 0);

            _triangles.Add(baseIdx + 1);
            _triangles.Add(currIdx + 1);
            _triangles.Add(currIdx + 0);

            _triangles.Add(baseIdx + 1);
            _triangles.Add(baseIdx + 2);
            _triangles.Add(currIdx + 1);

            _triangles.Add(baseIdx + 2);
            _triangles.Add(currIdx + 2);
            _triangles.Add(currIdx + 1);

            _triangles.Add(baseIdx + 2);
            _triangles.Add(baseIdx + 3);
            _triangles.Add(currIdx + 2);

            _triangles.Add(baseIdx + 3);
            _triangles.Add(currIdx + 3);
            _triangles.Add(currIdx + 2);

            _triangles.Add(baseIdx + 3);
            _triangles.Add(baseIdx + 0);
            _triangles.Add(currIdx + 3);

            _triangles.Add(baseIdx + 0);
            _triangles.Add(currIdx + 0);
            _triangles.Add(currIdx + 3);
        }
    }

    private void UpdateMesh()
    {
        _mesh.SetVertices(_vertices);
        _mesh.SetTriangles(_triangles, 0);
        _mesh.SetUVs(0, _uvs);
        _mesh.RecalculateNormals(); // Essential for URP Lighting
    }

    private float GetScrollVCoord()
    {
        float tilingY = _cachedSettings != null ? _cachedSettings.tiling.y : 1f;
        return _cumulativeDistance * tilingY;
    }

    public void FinalizeStrokeCollider()
    {
        if (_mesh == null || _vertices.Count < 3 || _triangles.Count < 3)
            return;

        MeshCollider meshCollider = GetComponent<MeshCollider>();
        if (meshCollider == null)
            meshCollider = gameObject.AddComponent<MeshCollider>();

        // Re-assign shared mesh so physics picks up the latest generated geometry.
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = _mesh;
    }
}