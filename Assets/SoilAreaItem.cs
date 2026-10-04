using UnityEngine;

[DisallowMultipleComponent]
public sealed class SoilAreaItem : MonoBehaviour
{
    public enum EffectKind { Raincloud, Wind, Sunlight }
    [SerializeField] private EffectKind effect;
    [SerializeField, Min(0.01f)] private float lifetime = 30f;
    [SerializeField, Min(0.01f)] private float radius = 1f;
    [SerializeField] private Transform rayOrigin;
    [SerializeField, Min(0f)] private float rayStartOffset = 0.1f;
    [SerializeField, Min(0.01f)] private float rayDistance = 10f;
    [SerializeField] private LayerMask soilLayers = 1 << 3;
    [Header("Big tree strength")]
    [Tooltip("Optional TreeState from the tree-type prefab used as the balance reference. Uses its Big-stage Alive Soil Rates, regardless of its current stage.")]
    [SerializeField] private TreeState bigTreeReference;
    [Tooltip("Fallback TOTAL RGB units/second when no reference is assigned. X roughness, Y nutrients, Z water. Signs are ignored.")]
    [SerializeField] private Vector3 fallbackBigTreeRates = new Vector3(0.005f, 0.01f, 0.02f);
    [SerializeField, Range(0f, 50f)] private float strengthMultiplier = 1f;

    [Header("Active lifetime glow")]
    [Tooltip("Assign only the glow-outline mesh renderers. Hidden in shop stock and while pooled; visible during the purchased lifetime.")]
    [SerializeField] private Renderer[] lifecycleGlowRenderers = new Renderer[0];

    private void Awake() { SetLifetimeGlow(IsPurchased); }
    private void OnEnable() { SetLifetimeGlow(IsPurchased); }

    private void SetLifetimeGlow(bool active)
    {
        if (lifecycleGlowRenderers == null) return;
        foreach (Renderer outline in lifecycleGlowRenderers)
            if (outline != null) outline.enabled = active;
    }

    private SoilManager soilManager;
    private TreeManager gameManager;
    private ItemPool pool;
    private GameObject pooledObject;
    private double expiresAt, lastEffectTime;
    public bool IsPurchased { get; private set; }
    public float RemainingSeconds => IsPurchased ? Mathf.Max(0f, (float)(expiresAt - Time.timeAsDouble)) : 0f;

    public void SetPoolContext(ItemPool owner, GameObject instance, SoilManager soil, TreeManager game)
    {
        StopEffect();
        pool = owner;
        pooledObject = instance;
        soilManager = soil;
        gameManager = game;
    }

    public void BeginPurchased()
    {
        if (IsPurchased) return;
        if (soilManager == null || pool == null)
        {
            Debug.LogError("SoilAreaItem: assign SoilManager on the ItemPool.", this);
            return;
        }
        IsPurchased = true;
        lastEffectTime = Time.timeAsDouble;
        expiresAt = lastEffectTime + lifetime;
        soilManager.TickStarting += PrepareTick;
        SetLifetimeGlow(true);
    }

    private void PrepareTick(float ignored) { ApplyUntil(Time.timeAsDouble); }

    private void ApplyUntil(double now)
    {
        if (!IsPurchased || soilManager == null) return;
        double end = System.Math.Min(now, expiresAt);
        float elapsed = (float)(end - lastEffectTime);
        lastEffectTime = end;
        if (elapsed <= 0f || (gameManager != null && !gameManager.IsGameRunning)) return;
        Vector3 origin = (rayOrigin != null ? rayOrigin.position : transform.position) + Vector3.up * rayStartOffset;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
            rayDistance + rayStartOffset, soilLayers, QueryTriggerInteraction.Ignore)) return;
        // Never paint an unrelated mesh that happens to share the terrain layer.
        if (hit.collider.GetComponentInParent<SoilManager>() != soilManager) return;
        Vector3 rates = bigTreeReference != null ? bigTreeReference.BigTreeSoilRates : fallbackBigTreeRates;
        rates = new Vector3(Mathf.Abs(rates.x), Mathf.Abs(rates.y), Mathf.Abs(rates.z)) * Mathf.Clamp(strengthMultiplier, 0f, 5f);
        soilManager.QueueAreaEffect(hit.point, radius, effect, elapsed, rates.z, rates.y, rates.x);
    }

    private void Update()
    {
        if (!IsPurchased) return;
        if (gameManager != null && !gameManager.IsGameRunning) { ReturnToPool(); return; }
        if (Time.timeAsDouble < expiresAt) return;
        // Queue the final partial interval; upload still happens only on a soil tick.
        ApplyUntil(expiresAt);
        ReturnToPool();
    }

    private void ReturnToPool()
    {
        StopEffect();
        if (pool != null) pool.Return(pooledObject);
        else if (pooledObject != null) pooledObject.SetActive(false);
    }

    private void StopEffect()
    {
        if (soilManager != null) soilManager.TickStarting -= PrepareTick;
        IsPurchased = false;
        SetLifetimeGlow(false);
    }

    private void OnDisable() { StopEffect(); }
    private void OnDrawGizmosSelected()
    {
        Vector3 origin = (rayOrigin != null ? rayOrigin.position : transform.position) + Vector3.up * rayStartOffset;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, origin + Vector3.down * (rayDistance + rayStartOffset));
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayDistance + rayStartOffset,
            soilLayers, QueryTriggerInteraction.Ignore)) Gizmos.DrawWireSphere(hit.point, radius);
    }
}
