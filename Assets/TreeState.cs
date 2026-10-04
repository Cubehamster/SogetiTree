using System;
using System.Collections.Generic;
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
        [Min(0)] public int pointsPerGrowth = 1;
        [Min(0.01f)] public float growthToNextStage = 60f;
        [Header("Neighbour competition")]
        [Min(0f)] public float blockedGrowthPenalty = 10f;
        [Min(0f)] public float blockedHealthPenalty = 10f;
        [Header("Health")]
        [Min(0.01f)] public float maximumHealth = 100f;
        [Min(0f)] public float healthLossPerSecond = 1f;
        [Tooltip("Dead trees lose health at this rate regardless of soil quality.")]
        [Min(0f)] public float deadHealthLossPerSecond = 1f;
        [Min(0f)] public float healthRecoveryPerSecond = 0f;
        [Tooltip("Health granted when entering this stage through growth.")]
        [Min(0f)] public float healthGainOnEntry = 20f;
    }

    [Header("State")]
    [SerializeField] private GrowthStage stage = GrowthStage.Sapling;
    [SerializeField] private bool isAlive = true;
    [SerializeField, Min(0f)] private float growth;
    [SerializeField] private float health = 50f;
    [SerializeField] private TreeManager treeManager;
    public Vector3 Location => transform.position;
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
    [Header("Big tree seed production")]
    [Tooltip("Automatically assigned by the TreeObjectPool that supplies this tree.")]
    [SerializeField] private TreeObjectPool objectPool;
    [SerializeField] private Transform seedSpawnPoint;
    [SerializeField, Min(0f)] private float seedSpawnHeight = 0.5f;
    [SerializeField, Min(0f)] private float seedSpawnRadius = 0.5f;
    [SerializeField, Min(0f)] private float seedHealthCost = 25f;
    [Tooltip("Optional explicit grab behaviours to disable for Big trees. Empty discovers Meta grab components automatically. Do not include TreePlanter.")]
    [SerializeField] private Behaviour[] grabBehaviours = new Behaviour[0];
    private readonly Dictionary<Behaviour, bool> stageBehaviourDefaults = new Dictionary<Behaviour, bool>();
    private bool stageBehavioursCached;
    private GameObject pooledTreeObject;

    [Header("Stage settings")]
    [SerializeField] private StageSettings bigCollider = new StageSettings { pointsPerGrowth = 15 };
    [SerializeField] private StageSettings smallCollider = new StageSettings { pointsPerGrowth = 5 };
    [SerializeField] private StageSettings saplingCollider = new StageSettings { pointsPerGrowth = 2 };
    [SerializeField] private StageSettings seedCollider = new StageSettings();

    // Total signed RGB units/second used by a living Big tree.
    public Vector3 BigTreeSoilRates => bigCollider.aliveSoilRates;
    public float RootRadius => GetSettings(stage).rootRadius;
    public float RootFalloff => GetSettings(stage).rootFalloff;
    public Color MinimumSoil => GetSettings(stage).minimumSoil;
    public Color MaximumSoil => GetSettings(stage).maximumSoil;
    public bool TryGetCurrentSoil(out Color color)
    {
        color = default;
        return IsPlanted && soilManager != null && influence != null
            && soilManager.TrySample(influence, out color);
    }

    public GameObject CurrentVisual => currentVisual;
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
    private double growthPointRemainder;
    private SoilManager subscribedManager;
    public bool IsPlanted => treePlanter != null ? treePlanter.IsPlanted : plantedWithoutPlanter;

    private void Reset()
    {
        grabCollider = GetComponent<BoxCollider>();
        grabbableTransform = transform;
        treePlanter = GetComponent<TreePlanter>();
        decalProjector = GetComponentInChildren<DecalProjector>(true);
    }

    private void OnEnable()
    {
        if (treeManager != null) treeManager.RegisterTree(this);
        if (treePlanter == null) treePlanter = GetComponent<TreePlanter>();
        StageSettings settings = GetSettings(stage);
        if (settings != null) health = Mathf.Min(health, settings.maximumHealth);
        ApplyStageSettings();
        RefreshVisual();
        subscribedManager = soilManager;
        if (subscribedManager != null) subscribedManager.TickCompleted += OnSoilTick;
    }

    public void SetObjectPool(TreeObjectPool pool, GameObject instance = null)
    {
        objectPool = pool;
        pooledTreeObject = instance != null ? instance : gameObject;
    }

    public void SetTreeManager(TreeManager manager)
    {
        if (treeManager != null) treeManager.UnregisterTree(this);
        treeManager = manager;
        if (isActiveAndEnabled && treeManager != null) treeManager.RegisterTree(this);
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
        if (!IsPlanted || (treeManager != null && !treeManager.IsGameRunning))
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

    public void ResetAsSeed()
    {
        growthPointRemainder = 0;
        growth = 0f;
        plantedWithoutPlanter = false;
        if (treePlanter != null) treePlanter.ResetForSpawn();
        SetStage(GrowthStage.Seed);
        isAlive = true;
        health = seedCollider.maximumHealth;
        ApplyAliveState();
        RefreshRootInfluence();
    }

    public void SetPlanted(bool planted)
    {
        plantedWithoutPlanter = planted;
        if (isActiveAndEnabled) SyncRegistration();
    }

    private void OnSoilTick(float elapsedSeconds)
    {
        if (!isActiveAndEnabled || !IsPlanted || influence == null || (treeManager != null && !treeManager.IsGameRunning)) return;
        LastSoil = influence.SoilBeforeTick;
        StageSettings settings = GetSettings(stage);
        if (settings == null) return;
        MeetsGrowthRequirements = MeetsBounds(LastSoil, settings.minimumSoil, settings.maximumSoil);
        if (!isAlive)
        {
            health -= settings.deadHealthLossPerSecond * elapsedSeconds;
            TryReturnDecomposedTree(settings);
            return;
        }
        if (!MeetsGrowthRequirements)
        {
            health -= settings.healthLossPerSecond * elapsedSeconds;
            if (health < 0f) SetAlive(false);
            return;
        }
        health = Mathf.Min(settings.maximumHealth,
            health + settings.healthRecoveryPerSecond * elapsedSeconds);
        float gained = settings.growthPerSecond * elapsedSeconds;
        growth += gained;
        growthPointRemainder += gained * settings.pointsPerGrowth;
        int earned = (int)Math.Min(int.MaxValue, Math.Floor(growthPointRemainder));
        if (earned > 0 && treeManager != null)
        {
            treeManager.AddPoints(earned);
            growthPointRemainder -= earned;
        }
        // Big produces one seed per threshold; it never advances further.
        if (stage == GrowthStage.Big)
        {
            if (growth >= Mathf.Max(0.01f, settings.growthToNextStage) && TryProduceSeed())
            {
                growth = 0f;
                health -= seedHealthCost;
                if (health < 0f) SetAlive(false);
            }
            return;
        }
        if (growth >= Mathf.Max(0.01f, settings.growthToNextStage))
        {
            if (treeManager != null && treeManager.HasLargerNeighbour(this, settings.rootRadius * 0.5f))
            {
                // Subtract the configured penalty; retry on a later tick at the threshold.
                growth = Mathf.Max(0f, growth - settings.blockedGrowthPenalty);
                health -= settings.blockedHealthPenalty;
                if (health < 0f) SetAlive(false);
                return;
            }
            SetStage((GrowthStage)((int)stage + 1));
        }
    }

    private void TryReturnDecomposedTree(StageSettings settings)
    {
        if (isAlive || health >= -Mathf.Max(0.01f, settings.growthToNextStage)) return;
        // Remove nutrient contribution before returning the complete tree prefab.
        UnregisterRoots();
        GameObject instance = pooledTreeObject != null ? pooledTreeObject : gameObject;
        if (treeManager != null && treeManager.ReturnTree(instance)) return;
        if (objectPool != null && objectPool.IsBorrowed(instance)) objectPool.Return(instance);
        else instance.SetActive(false); // A manually placed scene tree has no borrowed pool entry.
    }

    private bool TryProduceSeed()
    {
        if (objectPool == null) return false;
        Vector3 position = seedSpawnPoint != null ? seedSpawnPoint.position : transform.position;
        if (seedSpawnPoint == null && currentVisual != null)
        {
            foreach (Renderer renderer in currentVisual.GetComponentsInChildren<Renderer>())
                position.y = Mathf.Max(position.y, renderer.bounds.max.y);
        }
        Vector2 spread = UnityEngine.Random.insideUnitCircle * seedSpawnRadius;
        position += new Vector3(spread.x, seedSpawnHeight, spread.y);
        GameObject seed = treeManager != null
            ? treeManager.SpawnTree(objectPool, position, Quaternion.identity)
            : objectPool.Get(position, Quaternion.identity);
        if (seed == null) return false;
        TreeState state = seed.GetComponentInChildren<TreeState>(true);
        if (state == null)
        {
            if (treeManager != null) treeManager.ReturnTree(seed);
            else objectPool.Return(seed);
            Debug.LogError("TreeState: seed pool prefab requires TreeState.", this);
            return false;
        }
        state.ResetAsSeed();
        return true;
    }

    private void ApplyStageAvailability()
    {
        if (!stageBehavioursCached)
        {
            stageBehavioursCached = true;
            if (grabBehaviours == null || grabBehaviours.Length == 0)
            {
                var found = new List<Behaviour>();
                foreach (Behaviour behaviour in GetComponentsInChildren<Behaviour>(true))
                {
                    if (behaviour == null) continue;
                    // Covers the optional Meta grab variants without requiring all of them to be installed.
                    string name = behaviour.GetType().Name;
                    if (name == "Grabbable" || name == "GrabInteractable" ||
                        name == "HandGrabInteractable" || name == "DistanceGrabInteractable" ||
                        name == "DistanceHandGrabInteractable") found.Add(behaviour);
                }
                grabBehaviours = found.ToArray();
            }
            foreach (Behaviour behaviour in grabBehaviours)
                if (behaviour != null && behaviour != this && behaviour != treePlanter)
                    stageBehaviourDefaults[behaviour] = behaviour.enabled;
            foreach (TreeSoilDisplay display in GetComponentsInChildren<TreeSoilDisplay>(true))
                stageBehaviourDefaults[display] = display.enabled;
        }
        foreach (var entry in stageBehaviourDefaults)
            if (entry.Key != null) entry.Key.enabled = stage != GrowthStage.Big && entry.Value;
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
        if (!alive && settings != null) TryReturnDecomposedTree(settings);
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
        ApplyStageAvailability();
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
        if (treeManager != null) treeManager.UnregisterTree(this);
        if (subscribedManager != null) subscribedManager.TickCompleted -= OnSoilTick;
        subscribedManager = null;
        UnregisterRoots();
        registrationAttempted = false;
        ReturnVisual();
    }
}
