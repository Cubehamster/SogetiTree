using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(300)]
[DisallowMultipleComponent]
public sealed class TreeSoilDisplay : MonoBehaviour
{
    [Tooltip("Enable on hand displays controlled by HandSoilInspector.")]
    [SerializeField] private bool externalInput;
    [SerializeField] private bool showExternalRequirements;
    private bool externalVisible;
    private Color externalSoil;
    private Vector3 externalPosition;

    public void ShowExternalSoil(Color soil, Vector3 worldPosition)
    {
        externalSoil = soil; externalPosition = worldPosition; externalVisible = true;
    }
    public void HideExternalSoil() { externalVisible = false; }

    [SerializeField] private TreePlanter treePlanter;
    [SerializeField] private TreeState treeState;
    [Header("Display position")]
    [SerializeField] private bool followVisualTop = true;
    [SerializeField, Min(0f)] private float gapAboveVisual = 0.1f;
    [Tooltip("Used only when Follow Visual Top is disabled.")]
    [SerializeField] private Transform anchor;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1.5f, 0f);
    [SerializeField, Min(0.01f)] private float cubeSize = 0.3f;
    [SerializeField, Min(0.0001f)] private float lineThickness = 0.002f;
    [SerializeField, Min(0.0001f)] private float cornerRadius = 0.009f;
    [SerializeField, Min(0.0001f)] private float soilDotRadius = 0.018f;
    [SerializeField, Range(2,32)] private int gradientSegments = 12;
    [SerializeField] private float rotationDegreesPerSecond = 12f;
    [SerializeField] private bool showWhilePlanted = true;
    [SerializeField] private Color validSoilDotColor = Color.green;
    [Tooltip("Assign TreePlanting/Soil Display shader explicitly for builds.")]
    [SerializeField] private Shader displayShader;

    [SerializeField, Tooltip("Runtime diagnostic: explains whether the display has a soil reading.")]
    private string displayStatus = "Not initialized";

    public bool IsInsideRequirements { get; private set; }
    private GameObject visualRoot, dot;
    private MeshRenderer frameRenderer, dotRenderer;
    private Mesh frameMesh, dotMesh;
    private Material material;
    private MaterialPropertyBlock dotProperties;
    private Color lastMinimum, lastMaximum;
    private float yaw;
    private bool built;
    private GameObject cachedTreeVisual;
    private Renderer[] treeRenderers = new Renderer[0];
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<int> triangles = new List<int>();

    private void Awake()
    {
        if (treeState == null) treeState = GetComponentInParent<TreeState>();
        if (treePlanter == null) treePlanter = GetComponentInParent<TreePlanter>();
        if (displayShader == null) displayShader = Shader.Find("TreePlanting/Soil Display");
        if ((!externalInput && treeState == null) || displayShader == null)
        {
            displayStatus = treeState == null ? "Missing TreeState reference" : "Missing Soil Display shader";
            Debug.LogError("TreeSoilDisplay: " + displayStatus, this);
            enabled = false; return;
        }
        material = new Material(displayShader);
        visualRoot = new GameObject("Tree Soil Display (Runtime)");
        visualRoot.transform.SetParent(transform, false);
        visualRoot.SetActive(false);
        frameMesh = new Mesh { name = "Soil bounds" };
        frameRenderer = AddRenderer(visualRoot, frameMesh);
        dot = new GameObject("Current Soil");
        dot.transform.SetParent(visualRoot.transform, false);
        dotMesh = new Mesh { name = "Soil dot" };
        ClearGeometry();
        AddSphere(Vector3.zero, soilDotRadius, Color.white);
        Upload(dotMesh);
        dotRenderer = AddRenderer(dot, dotMesh);
        dotProperties = new MaterialPropertyBlock();
        displayStatus = "Waiting for a valid soil reading";
    }

    private MeshRenderer AddRenderer(GameObject obj, Mesh mesh)
    {
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = obj.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    private void LateUpdate()
    {
        if (visualRoot == null) return;
        Color soil;
        bool hasReading = false;
        soil = default;
        if (externalInput) { soil = externalSoil; hasReading = externalVisible; }
        else if (treePlanter != null && treePlanter.CanPlant && treePlanter.HasSoilSample)
        { soil = treePlanter.AverageColor; hasReading = true; }
        else if (showWhilePlanted)
            hasReading = treeState.TryGetCurrentSoil(out soil);
        if (!hasReading)
        {
            IsInsideRequirements = false;
            displayStatus = treePlanter == null ? "No TreePlanter / no planted soil reading"
                : !treePlanter.CanPlant ? "No valid planting hit: check ray distance, layer 3 and upright orientation"
                : !treePlanter.HasSoilSample ? "Planting hit found, but no SoilManager sample in radius"
                : "No planted root reading";
            visualRoot.SetActive(false);
            return;
        }
        Color minimum = treeState != null ? treeState.MinimumSoil : Color.black;
        Color maximum = treeState != null ? treeState.MaximumSoil : Color.white;
        // Invalid bounds cannot meaningfully describe a box.
        if (minimum.r > maximum.r || minimum.g > maximum.g || minimum.b > maximum.b)
        { displayStatus = "Invalid soil bounds: minimum exceeds maximum"; visualRoot.SetActive(false); return; }
        if (!built || minimum != lastMinimum || maximum != lastMaximum)
        {
            BuildFrame(minimum, maximum);
            lastMinimum = minimum; lastMaximum = maximum; built = true;
        }
        IsInsideRequirements = soil.r >= minimum.r && soil.r <= maximum.r
            && soil.g >= minimum.g && soil.g <= maximum.g
            && soil.b >= minimum.b && soil.b <= maximum.b;
        yaw = Mathf.Repeat(yaw + rotationDegreesPerSecond * Time.deltaTime, 360f);
        visualRoot.transform.SetPositionAndRotation(
            externalInput ? externalPosition :
            GetTreeDisplayPosition(),
            Quaternion.Euler(0f, yaw, 0f));
        // Compensate parent growth scale. Uniform parent scales are recommended.
        Vector3 scale = transform.lossyScale;
        visualRoot.transform.localScale = new Vector3(
            1f / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
        dot.transform.localPosition = Position(soil);
        dotProperties.SetColor("_Tint", DisplayColor(
            IsInsideRequirements && (!externalInput || showExternalRequirements) ? validSoilDotColor : soil));
        dotRenderer.SetPropertyBlock(dotProperties);
        displayStatus = "Visible — " + (IsInsideRequirements ? "soil in range" : "soil outside requirements");
        visualRoot.SetActive(true);
    }

    private Vector3 GetTreeDisplayPosition()
    {
        if (!followVisualTop || treeState == null)
            return (anchor != null ? anchor.position : transform.position) + worldOffset;
        GameObject current = treeState.CurrentVisual;
        if (current != cachedTreeVisual)
        {
            cachedTreeVisual = current;
            // Cache renderer references when the pooled growth-stage visual changes.
            treeRenderers = current != null ? current.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
        }
        bool found = false;
        Bounds bounds = default;
        foreach (Renderer renderer in treeRenderers)
        {
            // Mesh bounds only; do not include the soil UI, decals or particles.
            if (renderer == null || (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        Vector3 position = found ? new Vector3(bounds.center.x, bounds.max.y, bounds.center.z) : treeState.Location;
        // Raise the bottom of the upright cube clear of the visual, not just its centre.
        position.y += gapAboveVisual + cubeSize * 0.5f;
        position.x += worldOffset.x;
        position.z += worldOffset.z;
        return position;
    }

    private Vector3 Position(Color c) =>
        (new Vector3(c.r, c.g, c.b) - Vector3.one * 0.5f) * cubeSize;

    private Color Corner(Color minimum, Color maximum, int index) => new Color(
        (index & 1) == 0 ? minimum.r : maximum.r,
        (index & 2) == 0 ? minimum.g : maximum.g,
        (index & 4) == 0 ? minimum.b : maximum.b, 1f);

    private void BuildFrame(Color minimum, Color maximum)
    {
        ClearGeometry();
        for (int i = 0; i < 8; i++)
        {
            Color outer = Corner(Color.black, Color.white, i);
            AddSphere(Position(outer), cornerRadius, DisplayColor(outer));
            for (int axis = 0; axis < 3; axis++)
            {
                int bit = 1 << axis;
                if ((i & bit) != 0) continue;
                int other = i | bit;
                AddTube(Position(outer), Position(Corner(Color.black, Color.white, other)),
                    lineThickness, Color.white, Color.white);
                if (externalInput && !showExternalRequirements) continue;
                Color a = Corner(minimum, maximum, i);
                Color b = Corner(minimum, maximum, other);
                for (int j = 0; j < gradientSegments; j++)
                {
                    float t0 = (float)j / gradientSegments;
                    float t1 = (float)(j + 1) / gradientSegments;
                    AddTube(Vector3.Lerp(Position(a), Position(b), t0),
                        Vector3.Lerp(Position(a), Position(b), t1), lineThickness,
                        OklabGradient(a, b, t0), OklabGradient(a, b, t1));
                }
            }
        }
        Upload(frameMesh);
    }

    private void ClearGeometry() { vertices.Clear(); colors.Clear(); triangles.Clear(); }
    private void Upload(Mesh mesh)
    {
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
    }

    private void AddTube(Vector3 a, Vector3 b, float width, Color ca, Color cb)
    {
        Vector3 direction = b - a;
        if (direction.sqrMagnitude < 1e-12f) return;
        Vector3 u = Vector3.Cross(direction.normalized,
            Mathf.Abs(direction.normalized.y) < 0.9f ? Vector3.up : Vector3.right).normalized * width * 0.5f;
        Vector3 v = Vector3.Cross(direction.normalized, u).normalized * width * 0.5f;
        int start = vertices.Count;
        for (int end = 0; end < 2; end++)
            for (int k = 0; k < 4; k++)
            {
                float angle = k * Mathf.PI * 0.5f;
                vertices.Add((end == 0 ? a : b) + u * Mathf.Cos(angle) + v * Mathf.Sin(angle));
                colors.Add(end == 0 ? ca : cb);
            }
        for (int k = 0; k < 4; k++)
        {
            int n = (k + 1) % 4;
            triangles.Add(start+k); triangles.Add(start+n); triangles.Add(start+k+4);
            triangles.Add(start+n); triangles.Add(start+n+4); triangles.Add(start+k+4);
        }
    }

    private void AddSphere(Vector3 center, float radius, Color color)
    {
        const int rows = 8, columns = 12;
        int start = vertices.Count;
        for (int y = 0; y <= rows; y++)
        {
            float latitude = Mathf.PI * y / rows;
            for (int x = 0; x <= columns; x++)
            {
                float longitude = 2f * Mathf.PI * x / columns;
                vertices.Add(center + radius * new Vector3(Mathf.Sin(latitude)*Mathf.Cos(longitude),
                    Mathf.Cos(latitude), Mathf.Sin(latitude)*Mathf.Sin(longitude)));
                colors.Add(color);
            }
        }
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                int a = start + y * (columns+1) + x, b = a + columns+1;
                triangles.Add(a); triangles.Add(b); triangles.Add(a+1);
                triangles.Add(a+1); triangles.Add(b); triangles.Add(b+1);
            }
    }

    // Soil data is interpreted as sRGB for display, independently of RGB positions.
    private static Color DisplayColor(Color srgb)
    {
        Color c = QualitySettings.activeColorSpace == ColorSpace.Linear ? srgb.linear : srgb;
        c.a = 1f; return c;
    }
    private static Vector3 ToLab(Color srgb)
    {
        Color c = srgb.linear;
        float l = Mathf.Pow(0.4122214708f*c.r + 0.5363325363f*c.g + 0.0514459929f*c.b, 1f/3f);
        float m = Mathf.Pow(0.2119034982f*c.r + 0.6806995451f*c.g + 0.1073969566f*c.b, 1f/3f);
        float s = Mathf.Pow(0.0883024619f*c.r + 0.2817188376f*c.g + 0.6299787005f*c.b, 1f/3f);
        return new Vector3(0.2104542553f*l + 0.793617785f*m - 0.0040720468f*s,
            1.9779984951f*l - 2.428592205f*m + 0.4505937099f*s,
            0.0259040371f*l + 0.7827717662f*m - 0.808675766f*s);
    }
    private static Color OklabGradient(Color a, Color b, float t)
    {
        Vector3 lab = Vector3.Lerp(ToLab(a), ToLab(b), t);
        float l = lab.x + 0.3963377774f*lab.y + 0.2158037573f*lab.z;
        float m = lab.x - 0.1055613458f*lab.y - 0.0638541728f*lab.z;
        float s = lab.x - 0.0894841775f*lab.y - 1.291485548f*lab.z;
        l=l*l*l; m=m*m*m; s=s*s*s;
        Color linear = new Color(Mathf.Clamp01(4.0767416621f*l - 3.3077115913f*m + 0.2309699292f*s),
            Mathf.Clamp01(-1.2684380046f*l + 2.6097574011f*m - 0.3413193965f*s),
            Mathf.Clamp01(-0.0041960863f*l - 0.7034186147f*m + 1.707614701f*s), 1f);
        return QualitySettings.activeColorSpace == ColorSpace.Linear ? linear : linear.gamma;
    }

    private void OnDisable() { externalVisible = false; if (visualRoot != null) visualRoot.SetActive(false); }
    private void OnDestroy()
    {
        if (visualRoot != null) Destroy(visualRoot);
        if (frameMesh != null) Destroy(frameMesh);
        if (dotMesh != null) Destroy(dotMesh);
        if (material != null) Destroy(material);
    }
}
