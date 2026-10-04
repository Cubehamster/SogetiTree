using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
public sealed class SoilManager : MonoBehaviour
{
    [Serializable] public sealed class TickEvent : UnityEvent { }
    [SerializeField, Min(0.1f)] private float ticksPerSecond = 2f;
    [SerializeField] private Color defaultSoil = new Color(0.7f, 0.3f, 0.1f, 1f);
    [SerializeField] private TickEvent onTickCompleted = new TickEvent();

    // Signed total RGB units/second distributed across cached vertices.
    // Positive adds to soil; negative removes. Geometry/transform must stay fixed.
    public sealed class RootInfluence
    {
        internal SoilManager owner;
        internal int[] indices;
        internal float[] weights;
        internal float totalWeight;
        internal bool registered;
        public int VertexCount => indices.Length;
        // Signed total roughness change per second: negative loosens, positive firms.
        public float RoughnessPerSecond { get; set; }
        public float WaterPerSecond { get; set; }
        public float NutrientsPerSecond { get; set; }
        public Color SoilBeforeTick { get; internal set; }
        public float WaterReceived { get; internal set; }
        public float NutrientsReceived { get; internal set; }
    }

    // Moving purchased items queue their contributions here, before the shared upload.
    public event Action<float> TickStarting;
    public event Action<float> TickCompleted;
    public Mesh BaselineMesh => originalMesh;
    public int VertexCount => soil == null ? 0 : soil.Length;
    public int RegisteredTreeCount => roots.Count;
    public bool IsInitialized => mesh != null;

    private MeshFilter filter;
    private Mesh mesh, originalMesh;
    private Vector3[] worldVertices;
    private Color[] soil;
    private Vector3[] pendingChanges;
    private float[] waterDemand, nutrientDemand, roughnessChanges;
    private Vector3[] sourceAdditions;
    private readonly List<RootInfluence> roots = new List<RootInfluence>();
    private double lastTickTime;
    private bool ticking, preparingTick;

    private void Awake() { Initialize(); }
    private void OnEnable() { lastTickTime = Time.timeAsDouble; }

    public bool Initialize()
    {
        if (mesh != null) return true;
        filter = GetComponent<MeshFilter>();
        originalMesh = filter.sharedMesh;
        if (originalMesh == null || !originalMesh.isReadable || originalMesh.vertexCount == 0)
        {
            Debug.LogError("SoilManager needs a readable, nonempty MeshFilter mesh.", this);
            enabled = false;
            return false;
        }
        mesh = Instantiate(originalMesh);
        mesh.name = originalMesh.name + " (Managed Soil)";
        mesh.MarkDynamic();
        Vector3[] positions = mesh.vertices;
        worldVertices = new Vector3[positions.Length];
        for (int i = 0; i < positions.Length; i++)
            worldVertices[i] = transform.TransformPoint(positions[i]);
        soil = mesh.colors;
        if (soil.Length != positions.Length)
        {
            soil = new Color[positions.Length];
            for (int i = 0; i < soil.Length; i++) soil[i] = defaultSoil;
        }
        for (int i = 0; i < soil.Length; i++) soil[i] = Clamp(soil[i]);
        pendingChanges = new Vector3[soil.Length];
        sourceAdditions = new Vector3[soil.Length];
        roughnessChanges = new float[soil.Length];
        waterDemand = new float[soil.Length];
        nutrientDemand = new float[soil.Length];
        mesh.colors = soil;
        filter.sharedMesh = mesh;
        // Collider geometry stays unchanged: painting only changes colors.
        lastTickTime = Time.timeAsDouble;
        return true;
    }

    private static Color Clamp(Color c) => new Color(
        Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);

    private void Update()
    {
        if (!IsInitialized) return;
        double now = Time.timeAsDouble;
        double elapsed = now - lastTickTime;
        if (elapsed < 1.0 / Mathf.Max(0.1f, ticksPerSecond)) return;
        lastTickTime = now;
        SimulateTick((float)elapsed);
    }

    // Scan once at planting; cache indices and falloff weights. No grid required.
    public RootInfluence CreateInfluence(Vector3 center, float radius, float falloffPower = 2f)
    {
        if (!Initialize() || radius <= 0f) return null;
        var indices = new List<int>();
        var weights = new List<float>();
        float total = 0f, squaredRadius = radius * radius;
        for (int i = 0; i < worldVertices.Length; i++)
        {
            float squaredDistance = (worldVertices[i] - center).sqrMagnitude;
            if (squaredDistance >= squaredRadius) continue;
            float weight = Mathf.Pow(1f - Mathf.Sqrt(squaredDistance) / radius,
                Mathf.Max(0.01f, falloffPower));
            indices.Add(i); weights.Add(weight); total += weight;
        }
        return new RootInfluence {
            owner = this, indices = indices.ToArray(), weights = weights.ToArray(),
            totalWeight = total
        };
    }

    public RootInfluence RegisterTree(Vector3 center, float radius,
        float waterPerSecond, float nutrientsPerSecond, float falloffPower = 2f,
        float roughnessPerSecond = 0f)
    {
        if (ticking) throw new InvalidOperationException("Register trees outside the simulation tick.");
        RootInfluence root = CreateInfluence(center, radius, falloffPower);
        if (root == null || root.VertexCount == 0) return null;
        root.RoughnessPerSecond = roughnessPerSecond;
        root.WaterPerSecond = waterPerSecond;
        root.NutrientsPerSecond = nutrientsPerSecond;
        root.registered = true;
        roots.Add(root);
        return root;
    }

    public void UnregisterTree(RootInfluence root)
    {
        if (ticking) throw new InvalidOperationException("Unregister trees outside the simulation tick.");
        if (root == null || root.owner != this) return;
        roots.Remove(root);
        root.registered = false;
    }

    public bool TrySample(RootInfluence influence, out Color average)
    {
        average = default;
        if (influence == null || influence.owner != this || influence.totalWeight <= 0f) return false;
        Vector3 sum = Vector3.zero;
        for (int j = 0; j < influence.indices.Length; j++)
        {
            Color c = soil[influence.indices[j]];
            sum += new Vector3(c.r, c.g, c.b) * influence.weights[j];
        }
        sum /= influence.totalWeight;
        average = new Color(sum.x, sum.y, sum.z, 1f);
        return true;
    }

    // Allocation-free full scan for up to two held-tree previews.
    // Returns false when the radius contains no vertices.
    public bool TrySampleRadius(Vector3 center, float radius, out Color average,
        float falloffPower = 2f)
    {
        average = default;
        if (!Initialize() || radius <= 0f) return false;
        Vector3 sum = Vector3.zero;
        float total = 0f, squaredRadius = radius * radius;
        for (int i = 0; i < worldVertices.Length; i++)
        {
            float squaredDistance = (worldVertices[i] - center).sqrMagnitude;
            if (squaredDistance >= squaredRadius) continue;
            float weight = Mathf.Pow(1f - Mathf.Sqrt(squaredDistance) / radius,
                Mathf.Max(0.01f, falloffPower));
            Color c = soil[i];
            sum += new Vector3(c.r, c.g, c.b) * weight;
            total += weight;
        }
        if (total <= 0f) return false;
        sum /= total;
        average = new Color(sum.x, sum.y, sum.z, 1f);
        return true;
    }

    // Signed RGB change PER VERTEX at the brush center, with distance falloff.
    // Queue once for an action, or multiply a continuous rate by elapsed seconds.
    // These requests accumulate until the next tick and don't upload the mesh.
    public int QueueBrush(Vector3 center, float radius, Vector3 rgbChange,
        float falloffPower = 2f)
    {
        if (!Initialize() || radius <= 0f) return 0;
        int affected = 0;
        float squaredRadius = radius * radius;
        for (int i = 0; i < worldVertices.Length; i++)
        {
            float squaredDistance = (worldVertices[i] - center).sqrMagnitude;
            if (squaredDistance >= squaredRadius) continue;
            float weight = Mathf.Pow(1f - Mathf.Sqrt(squaredDistance) / radius,
                Mathf.Max(0.01f, falloffPower));
            pendingChanges[i] += rgbChange * weight;
            affected++;
        }
        return affected;
    }

    // Set blending is ordered among queued brush requests. Tree effects apply afterwards.
    public int QueueSetBrush(Vector3 center, float radius, int channel,
        float target, float amount, float falloffPower = 2f)
    {
        if (!Initialize() || radius <= 0f || amount <= 0f || channel < 0 || channel > 2) return 0;
        int affected = 0;
        float squaredRadius = radius * radius;
        for (int i = 0; i < worldVertices.Length; i++)
        {
            float squaredDistance = (worldVertices[i] - center).sqrMagnitude;
            if (squaredDistance >= squaredRadius) continue;
            float weight = Mathf.Pow(1f - Mathf.Sqrt(squaredDistance) / radius,
                Mathf.Max(0.01f, falloffPower));
            float current = Mathf.Clamp01(soil[i][channel] + pendingChanges[i][channel]);
            float next = Mathf.Lerp(current, Mathf.Clamp01(target), Mathf.Clamp01(amount * weight));
            Vector3 pending = pendingChanges[i];
            pending[channel] = next - soil[i][channel];
            pendingChanges[i] = pending;
            affected++;
        }
        return affected;
    }

    // Generic cached source: rain, sunlight, erosion, trees, etc.
    public RootInfluence RegisterSource(Vector3 center, float radius,
        Vector3 rgbPerSecond, float falloffPower = 2f)
    {
        return RegisterTree(center, radius, rgbPerSecond.z, rgbPerSecond.y,
            falloffPower, rgbPerSecond.x);
    }

    public void SetSourceRates(RootInfluence source, Vector3 rgbPerSecond)
    {
        if (source == null || source.owner != this) return;
        source.RoughnessPerSecond = rgbPerSecond.x;
        source.NutrientsPerSecond = rgbPerSecond.y;
        source.WaterPerSecond = rgbPerSecond.z;
    }

    public void UnregisterSource(RootInfluence source) => UnregisterTree(source);

    // For moving sources, replace the cache after their position/radius changes.
    public RootInfluence ReplaceSource(RootInfluence oldSource, Vector3 center,
        float radius, Vector3 rgbPerSecond, float falloffPower = 2f)
    {
        UnregisterSource(oldSource);
        return RegisterSource(center, radius, rgbPerSecond, falloffPower);
    }

    // Rates are TOTAL absolute RGB units/second across the affected area.
    // Falloff distributes that budget; adding vertices or increasing radius never
    // multiplies the total effect. All requests read the same pre-tick snapshot.
    public int QueueAreaEffect(Vector3 center, float radius, SoilAreaItem.EffectKind kind,
        float elapsedSeconds, float waterRate, float nutrientRate, float roughnessRate)
    {
        if (!Initialize() || radius <= 0f || elapsedSeconds <= 0f) return 0;
        float radiusSquared = radius * radius;
        double nutrientSum = 0d;
        float totalWeight = 0f;
        int count = 0;
        for (int i = 0; i < soil.Length; i++)
        {
            float distanceSquared = (worldVertices[i] - center).sqrMagnitude;
            if (distanceSquared >= radiusSquared) continue;
            totalWeight += 1f - Mathf.Sqrt(distanceSquared) / radius;
            nutrientSum += soil[i].g;
            count++;
        }
        if (count == 0 || totalWeight <= 0f) return 0;
        float averageNutrients = (float)(nutrientSum / count);
        float waterBudget = Mathf.Max(0f, waterRate) * elapsedSeconds;
        float nutrientBudget = Mathf.Max(0f, nutrientRate) * elapsedSeconds;
        float roughnessBudget = Mathf.Max(0f, roughnessRate) * elapsedSeconds;
        float nutrientBlend = 0f, roughnessBlend = 0f;
        if (kind == SoilAreaItem.EffectKind.Raincloud)
        {
            float nutrientError = 0f, weightedRoughnessError = 0f;
            for (int i = 0; i < soil.Length; i++)
            {
                float distanceSquared = (worldVertices[i] - center).sqrMagnitude;
                if (distanceSquared >= radiusSquared) continue;
                float weight = 1f - Mathf.Sqrt(distanceSquared) / radius;
                nutrientError += Mathf.Abs(averageNutrients - soil[i].g);
                weightedRoughnessError += Mathf.Abs(0.5f - soil[i].r) * weight;
            }
            // Common blend keeps the unweighted nutrient average conserved.
            // Nutrient budget counts the absolute changes on both donor and recipient.
            nutrientBlend = nutrientError > 0f ? Mathf.Min(1f, nutrientBudget / nutrientError) : 0f;
            roughnessBlend = weightedRoughnessError > 0f
                ? Mathf.Min(1f, roughnessBudget / weightedRoughnessError) : 0f;
        }
        for (int i = 0; i < soil.Length; i++)
        {
            float distanceSquared = (worldVertices[i] - center).sqrMagnitude;
            if (distanceSquared >= radiusSquared) continue;
            float weight = 1f - Mathf.Sqrt(distanceSquared) / radius;
            float share = weight / totalWeight;
            Color c = soil[i];
            Vector3 delta = Vector3.zero;
            switch (kind)
            {
                case SoilAreaItem.EffectKind.Raincloud:
                    delta.z = waterBudget * share;
                    delta.y = (averageNutrients - c.g) * nutrientBlend; // No distance falloff.
                    delta.x = (0.5f - c.r) * weight * roughnessBlend;
                    break;
                case SoilAreaItem.EffectKind.Wind:
                    // Wet soil resists erosion; do not redistribute that unused budget.
                    delta.x = roughnessBudget * share * (1f - c.b);
                    delta.y = -nutrientBudget * share;
                    break;
                case SoilAreaItem.EffectKind.Sunlight:
                    delta.z = -waterBudget * share;
                    break;
            }
            pendingChanges[i] += delta;
        }
        return count;
    }

    public void SimulateTick(float elapsedSeconds)
    {
        if (ticking || preparingTick || elapsedSeconds <= 0f || !Initialize()) return;
        preparingTick = true;
        try { TickStarting?.Invoke(elapsedSeconds); }
        finally { preparingTick = false; }
        ticking = true;
        try
        {
            Array.Clear(waterDemand, 0, waterDemand.Length);
            Array.Clear(nutrientDemand, 0, nutrientDemand.Length);
            Array.Clear(roughnessChanges, 0, roughnessChanges.Length);
            Array.Clear(sourceAdditions, 0, sourceAdditions.Length);
            foreach (RootInfluence source in roots)
            {
                TrySample(source, out Color average);
                source.SoilBeforeTick = average;
                source.WaterReceived = source.NutrientsReceived = 0f;
                Vector3 delta = new Vector3(source.RoughnessPerSecond,
                    source.NutrientsPerSecond, source.WaterPerSecond) * elapsedSeconds;
                for (int j = 0; j < source.indices.Length; j++)
                {
                    int i = source.indices[j];
                    float share = source.weights[j] / source.totalWeight;
                    roughnessChanges[i] += delta.x * share;
                    sourceAdditions[i].y += Mathf.Max(0f, delta.y) * share;
                    sourceAdditions[i].z += Mathf.Max(0f, delta.z) * share;
                    nutrientDemand[i] += Mathf.Max(0f, -delta.y) * share;
                    waterDemand[i] += Mathf.Max(0f, -delta.z) * share;
                }
            }
            foreach (RootInfluence source in roots)
            {
                float requestedWater = Mathf.Max(0f, -source.WaterPerSecond) * elapsedSeconds;
                float requestedNutrients = Mathf.Max(0f, -source.NutrientsPerSecond) * elapsedSeconds;
                for (int j = 0; j < source.indices.Length; j++)
                {
                    int i = source.indices[j];
                    float share = source.weights[j] / source.totalWeight;
                    float water = Mathf.Clamp01(soil[i].b + pendingChanges[i].z + sourceAdditions[i].z);
                    float nutrients = Mathf.Clamp01(soil[i].g + pendingChanges[i].y + sourceAdditions[i].y);
                    source.WaterReceived += requestedWater * share *
                        (waterDemand[i] > 0f ? Mathf.Min(1f, water / waterDemand[i]) : 0f);
                    source.NutrientsReceived += requestedNutrients * share *
                        (nutrientDemand[i] > 0f ? Mathf.Min(1f, nutrients / nutrientDemand[i]) : 0f);
                }
            }
            bool dirty = false;
            for (int i = 0; i < soil.Length; i++)
            {
                Color old = soil[i];
                Color next = new Color(
                    Mathf.Clamp01(old.r + pendingChanges[i].x + roughnessChanges[i]),
                    Mathf.Max(0f, Mathf.Clamp01(old.g + pendingChanges[i].y + sourceAdditions[i].y) - nutrientDemand[i]),
                    Mathf.Max(0f, Mathf.Clamp01(old.b + pendingChanges[i].z + sourceAdditions[i].z) - waterDemand[i]), 1f);
                if (old.r != next.r || old.g != next.g || old.b != next.b) dirty = true;
                soil[i] = next;
                pendingChanges[i] = Vector3.zero;
            }
            if (dirty) mesh.colors = soil;
        }
        finally { ticking = false; }
        // Snapshot reads and uptake are complete. Sources may now change rates/caches.
        TickCompleted?.Invoke(elapsedSeconds);
        onTickCompleted.Invoke();
    }

    private void OnDestroy()
    {
        if (filter != null && filter.sharedMesh == mesh) filter.sharedMesh = originalMesh;
        foreach (RootInfluence root in roots) { root.owner = null; root.registered = false; }
        roots.Clear();
        if (mesh != null) Destroy(mesh);
    }
}
