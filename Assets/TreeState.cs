using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public sealed class TreeState : MonoBehaviour
{
    public enum GrowthStage { Seed, Sapling, Small, Big }

    [Serializable]
    private sealed class StageSettings
    {
        public Vector3 center = Vector3.zero;
        public Vector3 size = Vector3.one;
        public Vector3 localScale = Vector3.one;
        public Vector2 decalWidthHeight = Vector2.one;
        [Header("Roots and signed soil effects")]
        [Min(0.01f)] public float rootRadius = 0.5f;
        [Min(0.01f)] public float rootFalloff = 2f;
        [Tooltip("X roughness, Y nutrients, Z water. Positive adds, negative removes. Total units/second.")]
        public Vector3 aliveSoilRates = new Vector3(-0.005f, -0.01f, -0.02f);
        public Vector3 deadSoilRates = new Vector3(0f, 0.005f, 0f);
        [Header("Growth requirements — alpha ignored")]
        public Color minimumSoil = new Color(0f, 0.2f, 0.2f, 1f);
        public Color maximumSoil = new Color(0.7f, 1f, 0.9f, 1f);
        [Min(0f)] public float growthPerSecond = 1f;
        [Min(0.01f)] public float growthToNextStage = 60f;
        [Header("Health")]
        [Min(0.01f)] public float maximumHealth = 100f;
        [Min(0f)] public float healthLossPerSecond = 1f;
        [Min(0f)] public float healthRecoveryPerSecond = 0f;
        [Tooltip("Health granted when entering this stage through growth.")]
        [Min(0f)] public float healthGainOnEntry = 20f;
    }

    [Header("State")]
    [SerializeField] private GrowthStage stage = GrowthStage.Sapling;
    [SerializeField] private bool isAlive = true;
    [SerializeField, Min(0f)] private float growth;
    [SerializeField] private float health = 50f;
    [Header("Soil simulation")]
    [SerializeField] private SoilManager soilManager;
    [SerializeField] private TreePlanter treePlanter;
    [Tooltip("Position used to cache roots. Assign the same trunk-base marker used for planting.")]
    [SerializeField] private Transform rootPoint;
    [Tooltip("Use for stationary trees without a TreePlanter. Ignored when a planter is assigned.")]
    [SerializeField] private bool plantedWithoutPlanter;
    [Header("Visual pools")]
    [SerializeField] private TreeMeshPool bigPool;
    [SerializeField] private TreeMeshPool smallPool;
    [SerializeField] private TreeMeshPool saplingPool;
    [SerializeField] private TreeMeshPool seedPool;
    [SerializeField] private Transform visualParent;
    [Header("Shared grabbable components")]
    [SerializeField] private BoxCollider grabCollider;
    [SerializeField] private Transform grabbableTransform;
    [SerializeField] private DecalProjector decalProjector;
    [Header("Stage settings")]
    [SerializeField] private StageSettings bigCollider = new StageSettings();
    [SerializeField] private StageSettings smallCollider = new StageSettings();
    [SerializeField] private StageSettings saplingCollider = new StageSettings();
    [SerializeField] private StageSettings seedCollider = new StageSettings();

    public GrowthStage Stage => stage;
    public bool IsAlive => isAlive;
    public float Growth => growth;
    public float Health => health;
    public Color LastSoil { get; private set; }
    public bool MeetsGrowthRequirements { get; private set; }
    public SoilManager.RootInfluence RootInfluence => influence;

    private GameObject currentVisual;
    private TreeMeshPool currentPool;
    private TreeDeadAlive currentDeadAlive;
    private GrowthStage displayedStage;
    private SoilManager.RootInfluence influence;
    private bool registrationAttempted;
    private SoilManager subscribedManager;
    private bool IsPlanted => treePlanter != null ? treePlanter.IsPlanted : plantedWithoutPlanter;

    private void Reset()
    {
        grabCollider = GetComponent<BoxCollider>();
        grabbableTransform = transform;
        treePlanter = GetComponent<TreePlanter>();
        decalProjector = GetComponentInChildren<DecalProjector>(true);
    }

    private void OnEnable()
    {
        if (treePlanter == null) treePlanter = GetComponent<TreePlanter>();
        StageSettings settings = GetSettings(stage);
        if (settings != null) health = Mathf.Min(health, settings.maximumHealth);
        ApplyStageSettings();
        RefreshVisual();
        subscribedManager = soilManager;
        if (subscribedManager != null) subscribedManager.TickCompleted += OnSoilTick;
    }

    public void SetMeshPools(TreeMeshPool big, TreeMeshPool small,
        TreeMeshPool sapling, TreeMeshPool seed)
    {
        bool changed = bigPool != big || smallPool != small ||
            saplingPool != sapling || seedPool != seed;
        bigPool = big;
        smallPool = small;
        saplingPool = sapling;
        seedPool = seed;
        if (changed && isActiveAndEnabled) RefreshVisual();
    }

    public void SetSoilManager(SoilManager manager)
    {
        if (soilManager == manager &&
            (!isActiveAndEnabled || subscribedManager == manager)) return;
        if (subscribedManager != null) subscribedManager.TickCompleted -= OnSoilTick;
        subscribedManager = null;
        UnregisterRoots(); // Uses the previous manager before replacing it.
        soilManager = manager;
        registrationAttempted = false;
        if (!isActiveAndEnabled) return;
        subscribedManager = manager;
        if (subscribedManager != null) subscribedManager.TickCompleted += OnSoilTick;
        SyncRegistration();
    }

    private void Update() { SyncRegistration(); }

    private void SyncRegistration()
    {
        if (!IsPlanted)
        {
            UnregisterRoots();
            registrationAttempted = false;
            MeetsGrowthRequirements = false;
            return;
        }
        if (influence != null || registrationAttempted || soilManager == null) return;
        registrationAttempted = true;
        StageSettings settings = GetSettings(stage);
        if (settings == null) return;
        Vector3 position = rootPoint != null ? rootPoint.position : transform.position;
        influence = soilManager.RegisterSource(position, settings.rootRadius,
            isAlive ? settings.aliveSoilRates : settings.deadSoilRates, settings.rootFalloff);
        if (influence == null)
            Debug.LogWarning("TreeState: root radius contains no soil vertices, or soil is unavailable. Increase radius and call RefreshRootInfluence().", this);
    }

    public void RefreshRootInfluence()
    {
        UnregisterRoots();
        registrationAttempted = false;
        if (isActiveAndEnabled) SyncRegistration();
    }

    private void UnregisterRoots()
    {
        if (influence != null && soilManager != null) soilManager.UnregisterSource(influence);
        influence = null;
    }

    public void SetPlanted(bool planted)
    {
        plantedWithoutPlanter = planted;
        if (isActiveAndEnabled) SyncRegistration();
    }

    private void OnSoilTick(float elapsedSeconds)
    {
        if (!IsPlanted || influence == null) return;
        LastSoil = influence.SoilBeforeTick;
        StageSettings settings = GetSettings(stage);
        if (settings == null) return;
        MeetsGrowthRequirements = MeetsBounds(LastSoil, settings.minimumSoil, settings.maximumSoil);
        if (!isAlive) return;
        if (!MeetsGrowthRequirements)
        {
            health -= settings.healthLossPerSecond * elapsedSeconds;
            if (health < 0f) SetAlive(false);
            return;
        }
        health = Mathf.Min(settings.maximumHealth,
            health + settings.healthRecoveryPerSecond * elapsedSeconds);
        // Big is the final stage; it still checks soil and can lose health.
        if (stage == GrowthStage.Big) return;
        growth += settings.growthPerSecond * elapsedSeconds;
        if (growth >= Mathf.Max(0.01f, settings.growthToNextStage))
            SetStage((GrowthStage)((int)stage + 1));
    }

    private static bool MeetsBounds(Color value, Color lower, Color upper)
    {
        // Inclusive independent channel checks; alpha has no gameplay meaning.
        return value.r >= lower.r && value.r <= upper.r
            && value.g >= lower.g && value.g <= upper.g
            && value.b >= lower.b && value.b <= upper.b;
    }

    public void SetStage(GrowthStage newStage)
    {
        if (!Enum.IsDefined(typeof(GrowthStage), newStage)) return;
        if (stage == newStage) { ApplyStageSettings(); return; }
        bool advancing = (int)newStage > (int)stage;
        stage = newStage;
        growth = 0f; // Each stage has its own progress threshold; no carryover.
        StageSettings settings = GetSettings(stage);
        if (settings != null)
            health = Mathf.Min(settings.maximumHealth,
                health + (advancing && isAlive ? settings.healthGainOnEntry : 0f));
        ApplyStageSettings();
        if (isActiveAndEnabled) RefreshVisual();
        RefreshRootInfluence();
    }

    public void SetAlive(bool alive)
    {
        isAlive = alive;
        // Explicit revival restores health if death took it below zero.
        StageSettings settings = GetSettings(stage);
        if (alive && health <= 0f && settings != null) health = settings.maximumHealth;
        ApplyAliveState();
        if (soilManager != null && influence != null && settings != null)
            soilManager.SetSourceRates(influence, alive ? settings.aliveSoilRates : settings.deadSoilRates);
    }
    public void MakeAlive() => SetAlive(true);
    public void MakeDead() => SetAlive(false);

    private StageSettings GetSettings(GrowthStage value)
    {
        switch (value)
        {
            case GrowthStage.Big: return bigCollider;
            case GrowthStage.Small: return smallCollider;
            case GrowthStage.Sapling: return saplingCollider;
            case GrowthStage.Seed: return seedCollider;
            default: return null;
        }
    }

    private void ApplyStageSettings()
    {
        StageSettings settings = GetSettings(stage);
        if (settings == null) return;
        if (grabbableTransform != null) grabbableTransform.localScale = settings.localScale;
        if (grabCollider != null)
        {
            grabCollider.center = settings.center;
            grabCollider.size = new Vector3(Mathf.Max(0.001f, settings.size.x),
                Mathf.Max(0.001f, settings.size.y), Mathf.Max(0.001f, settings.size.z));
        }
        if (decalProjector != null)
        {
            Vector3 size = decalProjector.size;
            size.x = Mathf.Max(0.001f, settings.decalWidthHeight.x);
            size.y = Mathf.Max(0.001f, settings.decalWidthHeight.y);
            decalProjector.size = size;
        }
    }

    private TreeMeshPool GetPool(GrowthStage value)
    {
        switch (value)
        {
            case GrowthStage.Big: return bigPool;
            case GrowthStage.Small: return smallPool;
            case GrowthStage.Sapling: return saplingPool;
            case GrowthStage.Seed: return seedPool;
            default: return null;
        }
    }

    private void RefreshVisual()
    {
        TreeMeshPool pool = GetPool(stage);
        if (pool == null) { Debug.LogWarning("TreeState: missing visual pool for " + stage, this); return; }
        if (currentVisual != null && currentPool == pool && displayedStage == stage)
        { ApplyAliveState(); return; }
        Transform parent = visualParent != null ? visualParent : transform;
        GameObject next = pool.Get(parent.position, parent.rotation, parent);
        if (next == null) return;
        ReturnVisual();
        currentVisual = next; currentPool = pool; displayedStage = stage;
        currentVisual.transform.localPosition = Vector3.zero;
        currentVisual.transform.localRotation = Quaternion.identity;
        currentDeadAlive = currentVisual.GetComponentInChildren<TreeDeadAlive>(true);
        ApplyAliveState();
    }

    private void ApplyAliveState()
    {
        if (currentDeadAlive == null) return;
        currentDeadAlive.Bind(this);
        currentDeadAlive.ApplyVisuals(isAlive);
    }

    private void ReturnVisual()
    {
        if (currentDeadAlive != null) currentDeadAlive.Bind(null);
        if (currentVisual != null)
        {
            if (currentPool != null) currentPool.Return(currentVisual);
            else Destroy(currentVisual);
        }
        currentVisual = null; currentPool = null; currentDeadAlive = null;
    }

    private void OnDisable()
    {
        if (subscribedManager != null) subscribedManager.TickCompleted -= OnSoilTick;
        subscribedManager = null;
        UnregisterRoots();
        registrationAttempted = false;
        ReturnVisual();
    }
}
