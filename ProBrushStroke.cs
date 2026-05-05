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
    
    [Header("Eraser Settings")]
    private float _lastEraseTime = 0f;

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
        ApplyBaseColor(dynamicMat, settings.activeColor);

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

    private static void ApplyBaseColor(Material material, Color color)
    {
        if (material == null)
            return;

        material.color = color;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
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
        
        float currentStrokePercentage = Mathf.Clamp01(_totalStrokeLength / _estimatedMaxStrokeLength);
        float taperMultiplier = _cachedSettings != null ? _cachedSettings.edgeProfile.Evaluate(currentStrokePercentage) : 1f;
        float finalRadius = radius * taperMultiplier;

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
        // Constrain height to prevent overly thick flat brushes
        float height = Mathf.Min(radiusMultiplied, radiusMultiplied * 0.3f);
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
        if (_mesh == null) return;

        _mesh.Clear(); // CRITICAL: This wipes old indices so it doesn't create streaks
        _mesh.SetVertices(_vertices);
        _mesh.SetTriangles(_triangles, 0);
        _mesh.SetUVs(0, _uvs);
        _mesh.RecalculateNormals(); 
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

    /// <summary>
    /// Erase the stroke at the given position with the specified radius.
    /// Supports partial erasure with cooldown and optional tube capping for solid ends.
    /// </summary>
    public void EraseAtPosition(Vector3 worldPoint, float radius)
    {
        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        float localRadius = radius / transform.lossyScale.x;

        List<int> pointsToRemove = new List<int>();
        for (int i = 0; i < _points.Count; i++)
        {
            if (Vector3.Distance(transform.InverseTransformPoint(_points[i]), localPoint) <= localRadius)
            {
                pointsToRemove.Add(i);
            }
        }

        if (pointsToRemove.Count == 0) return;

        // 1. Identify valid segments (Split the list of points into multiple lists)
        List<List<Vector3>> newSegments = new List<List<Vector3>>();
        List<Vector3> currentSegment = new List<Vector3>();

        for (int i = 0; i < _points.Count; i++)
        {
            if (pointsToRemove.Contains(i))
            {
                // Gap found! If the segment we just built has enough points to be a line, save it.
                if (currentSegment.Count >= 2)
                    newSegments.Add(new List<Vector3>(currentSegment));
                currentSegment.Clear();
            }
            else
            {
                currentSegment.Add(_points[i]);
            }
        }

        // Add the final remaining piece
        if (currentSegment.Count >= 2) newSegments.Add(currentSegment);

        // 2. Realistic Split Application
        if (newSegments.Count == 0)
        {
            gameObject.SetActive(false); // The whole line was inside the eraser
        }
        else
        {
            // Keep the first segment on the ORIGINAL object
            _points = new List<Vector3>(newSegments[0]);
            UpdateMeshGeometry(); // Helper to refresh vertices and clear old streaks

            // Every OTHER segment becomes a NEW independent GameObject
            for (int i = 1; i < newSegments.Count; i++)
            {
                SpawnSplitPiece(newSegments[i]);
            }
        }
    }

    public void UpdateMeshGeometry()
    {
        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();

        if (_meshFilter == null)
            return;

        if (_mesh == null)
        {
            _mesh = new Mesh();
            _mesh.name = "SplitStroke";
            _mesh.MarkDynamic();
            _meshFilter.mesh = _mesh;
        }

        _mesh.Clear(); // CRITICAL: This stops the blue lines from stretching across the gap
        RebuildMeshFromPoints(GetVerticesPerRing());
        UpdateMesh();
        FinalizeStrokeCollider();
    }

    private void SpawnSplitPiece(List<Vector3> segmentPoints)
    {
        GameObject newObj = Instantiate(gameObject, transform.parent);
        ProBrushStroke newStroke = newObj.GetComponent<ProBrushStroke>();
        if (newStroke == null)
            return;

        newStroke._points = new List<Vector3>(segmentPoints);
        newStroke.UpdateMeshGeometry();
    }

    /// <summary>
    /// Rebuild mesh data from remaining points with proper topology.
    /// Creates end caps at the beginning and end of the remaining mesh.
    /// </summary>
    private void RebuildMeshFromPoints(int verticesPerRing)
    {
        _vertices.Clear();
        _triangles.Clear();
        _uvs.Clear();

        if (_points.Count < 2)
        {
            // Not enough points to render a line
            return;
        }

        // Regenerate vertices and triangles from scratch
        float cumulativeDistance = 0f;
        
        for (int i = 0; i < _points.Count; i++)
        {
            Vector3 position = _points[i];
            
            // Estimate rotation from stroke direction
            Quaternion rotation = Quaternion.identity;
            if (i < _points.Count - 1)
            {
                Vector3 direction = (_points[i + 1] - _points[i]).normalized;
                if (direction.sqrMagnitude > 0.001f)
                    rotation = Quaternion.LookRotation(direction);
            }
            else if (i > 0)
            {
                Vector3 direction = (_points[i] - _points[i - 1]).normalized;
                if (direction.sqrMagnitude > 0.001f)
                    rotation = Quaternion.LookRotation(direction);
            }

            // Calculate taper multiplier for tapered strokes
            float strokePercentage = Mathf.Clamp01(i / Mathf.Max(1f, _points.Count - 1f));
            float taperMultiplier = _cachedSettings != null ? _cachedSettings.edgeProfile.Evaluate(strokePercentage) : 1f;
            float finalRadius = radius * taperMultiplier;

            // Generate vertices for this ring based on brush shape
            GenerateRingVertices(position, rotation, finalRadius, verticesPerRing, cumulativeDistance);

            // Update cumulative distance for UV scrolling
            if (i > 0)
                cumulativeDistance += Vector3.Distance(position, _points[i - 1]);
        }

        // Connect rings with triangles
        for (int i = 0; i < _points.Count - 1; i++)
        {
            ConnectRings(i, verticesPerRing);
        }

        // Cap the tube ends for solid appearance
        CapTubeEnds(verticesPerRing);
    }

    /// <summary>
    /// Generate vertices for a single ring at the given position.
    /// </summary>
    private void GenerateRingVertices(Vector3 center, Quaternion rotation, float radiusMultiplied, int verticesPerRing, float vCoord)
    {
        if (_currentShape == BrushSettings.BrushShape.Round)
        {
            // Round tube vertices
            for (int i = 0; i < verticesPerRing; i++)
            {
                float angle = i * Mathf.PI * 2 / verticesPerRing;
                Vector3 offset = new Vector3(
                    Mathf.Cos(angle) * radiusMultiplied,
                    Mathf.Sin(angle) * radiusMultiplied,
                    0
                );
                Vector3 vertexPos = center + (rotation * offset);
                _vertices.Add(transform.InverseTransformPoint(vertexPos));
                _uvs.Add(new Vector2((float)i / verticesPerRing, vCoord * (_cachedSettings != null ? _cachedSettings.tiling.y : 1f)));
            }
        }
        else
        {
            // For other shapes, we'll regenerate based on stored shape data
            // This is a simplified fallback to round vertices
            for (int i = 0; i < verticesPerRing; i++)
            {
                float angle = i * Mathf.PI * 2 / verticesPerRing;
                Vector3 offset = new Vector3(
                    Mathf.Cos(angle) * radiusMultiplied,
                    Mathf.Sin(angle) * radiusMultiplied,
                    0
                );
                Vector3 vertexPos = center + (rotation * offset);
                _vertices.Add(transform.InverseTransformPoint(vertexPos));
                _uvs.Add(new Vector2((float)i / verticesPerRing, vCoord * (_cachedSettings != null ? _cachedSettings.tiling.y : 1f)));
            }
        }
    }

    /// <summary>
    /// Connect two adjacent rings with triangles.
    /// </summary>
    private void ConnectRings(int ringIndex, int verticesPerRing)
    {
        int baseIdx = ringIndex * verticesPerRing;
        int nextIdx = (ringIndex + 1) * verticesPerRing;

        for (int i = 0; i < verticesPerRing; i++)
        {
            int next = (i + 1) % verticesPerRing;

            // Two triangles per side quad
            _triangles.Add(baseIdx + i);
            _triangles.Add(baseIdx + next);
            _triangles.Add(nextIdx + i);

            _triangles.Add(baseIdx + next);
            _triangles.Add(nextIdx + next);
            _triangles.Add(nextIdx + i);
        }
    }

    /// <summary>
    /// Create capped ends on the tube mesh to keep it solid.
    /// This prevents seeing inside the tube at cut points.
    /// </summary>
    private void CapTubeEnds(int verticesPerRing)
    {
        if (_points.Count < 2)
            return;

        // Cap the start (first ring)
        int startCenterIdx = _vertices.Count;
        Vector3 startCenter = transform.InverseTransformPoint(_points[0]);
        _vertices.Add(startCenter);
        _uvs.Add(new Vector2(0.5f, 0f));

        for (int i = 0; i < verticesPerRing; i++)
        {
            int next = (i + 1) % verticesPerRing;
            _triangles.Add(startCenterIdx);
            _triangles.Add(i);
            _triangles.Add(next);
        }

        // Cap the end (last ring)
        int endRingStartIdx = (_points.Count - 1) * verticesPerRing;
        int endCenterIdx = _vertices.Count;
        Vector3 endCenter = transform.InverseTransformPoint(_points[_points.Count - 1]);
        _vertices.Add(endCenter);
        _uvs.Add(new Vector2(0.5f, 1f));

        for (int i = 0; i < verticesPerRing; i++)
        {
            int next = (i + 1) % verticesPerRing;
            _triangles.Add(endCenterIdx);
            _triangles.Add(endRingStartIdx + next);
            _triangles.Add(endRingStartIdx + i);
        }
    }

    /// <summary>
    /// Get the number of vertices per ring based on the current brush shape.
    /// </summary>
    private int GetVerticesPerRing()
    {
        if (_currentShape == BrushSettings.BrushShape.Flat)
            return 4;
        else if (_currentShape == BrushSettings.BrushShape.Oil)
            return 6; // 3 layers * 2 vertices
        else if (_currentShape == BrushSettings.BrushShape.Ribbon)
            return 2;
        else
            return tubeSegments; // Round and others
    }

    /// <summary>
    /// Capture the current mesh state for undo/redo support.
    /// Returns a snapshot of vertices, triangles, and UVs.
    /// </summary>
    public MeshState CaptureMeshState()
    {
        return new MeshState(_points, _vertices, _triangles, _uvs, _totalStrokeLength, _cumulativeDistance);
    }

    /// <summary>
    /// Restore the mesh to a previously captured state.
    /// Used by the undo/redo system.
    /// </summary>
    public void RestoreMeshState(MeshState state)
    {
        if (state == null)
            return;

        _points = new List<Vector3>(state.points);
        _vertices = new List<Vector3>(state.vertices);
        _triangles = new List<int>(state.triangles);
        _uvs = new List<Vector2>(state.uvs);
        _totalStrokeLength = state.totalStrokeLength;
        _cumulativeDistance = state.cumulativeDistance;

        UpdateMesh();
        FinalizeStrokeCollider();
    }

    /// <summary>
    /// Represents a snapshot of mesh data for undo/redo operations.
    /// </summary>
    public class MeshState
    {
        public List<Vector3> points;
        public List<Vector3> vertices;
        public List<int> triangles;
        public List<Vector2> uvs;
        public float totalStrokeLength;
        public float cumulativeDistance;

        public MeshState(List<Vector3> pts, List<Vector3> verts, List<int> tris, List<Vector2> uvCoords, float strokeLen, float cumDist)
        {
            points = new List<Vector3>(pts);
            vertices = new List<Vector3>(verts);
            triangles = new List<int>(tris);
            uvs = new List<Vector2>(uvCoords);
            totalStrokeLength = strokeLen;
            cumulativeDistance = cumDist;
        }
    }
}