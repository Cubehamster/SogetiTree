using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TreeMeshPool : MonoBehaviour
{
    [SerializeField] private GameObject prefab;
    [SerializeField, Min(0)] private int initialSize = 20;
    [SerializeField] private SoilManager soilManager;

    public SoilManager SoilManager => soilManager;

    private readonly Queue<GameObject> available = new Queue<GameObject>();
    private readonly HashSet<GameObject> borrowed = new HashSet<GameObject>();
    private readonly Queue<GameObject> pendingReturns = new Queue<GameObject>();
    private Transform storage;
    private bool initialized;

    private void Awake() { Initialize(); }

    private void Initialize()
    {
        if (initialized) return;
        if (prefab == null)
        {
            Debug.LogError("TreeMeshPool: assign a prefab.", this);
            return;
        }
        GameObject container = new GameObject("Pooled Tree Meshes");
        storage = container.transform;
        storage.SetParent(transform, false);
        container.SetActive(false);
        initialized = true;
        for (int i = 0; i < initialSize; i++) available.Enqueue(CreateInstance());
    }

    private GameObject CreateInstance()
    {
        // Instantiate under an inactive parent so injection happens before OnEnable.
        GameObject instance = Instantiate(prefab, storage, false);
        instance.SetActive(false);
        AssignSoilManager(instance);
        return instance;
    }

    private void AssignSoilManager(GameObject instance)
    {
        // Includes inactive child components; does not reach into the new parent.
        foreach (TreeState state in instance.GetComponentsInChildren<TreeState>(true))
            state.SetSoilManager(soilManager);
        foreach (TreePlanter planter in instance.GetComponentsInChildren<TreePlanter>(true))
            planter.SetSoilManager(soilManager);
        foreach (TreePlanting.SoilPainter painter in
                 instance.GetComponentsInChildren<TreePlanting.SoilPainter>(true))
            painter.targetSoil = soilManager;
    }

    public GameObject Get(Vector3 position, Quaternion rotation, Transform parent = null)
    {
        Initialize();
        if (!initialized) return null;
        GameObject instance = null;
        while (available.Count > 0 && instance == null) instance = available.Dequeue();
        if (instance == null) instance = CreateInstance();
        borrowed.Add(instance);
        instance.transform.SetParent(parent, false);
        instance.transform.localScale = prefab.transform.localScale;
        instance.transform.SetPositionAndRotation(position, rotation);
        AssignSoilManager(instance);
        instance.SetActive(true);
        return instance;
    }

    public void Return(GameObject instance)
    {
        if (instance == null || !borrowed.Remove(instance)) return;
        // Return may be called by TreeState.OnDisable while its parent is
        // transitioning activation. Reparent only after that callback completes.
        instance.SetActive(false);
        pendingReturns.Enqueue(instance);
    }

    private void LateUpdate()
    {
        while (pendingReturns.Count > 0)
        {
            GameObject instance = pendingReturns.Dequeue();
            if (instance == null) continue;
            instance.transform.SetParent(storage, false);
            available.Enqueue(instance);
        }
    }

    // Optional runtime reassignment, including already borrowed objects.
    public void SetSoilManager(SoilManager manager)
    {
        soilManager = manager;
        foreach (GameObject instance in available)
            if (instance != null) AssignSoilManager(instance);
        foreach (GameObject instance in borrowed)
            if (instance != null) AssignSoilManager(instance);
        foreach (GameObject instance in pendingReturns)
            if (instance != null) AssignSoilManager(instance);
    }

    private void OnDestroy()
    {
        foreach (GameObject instance in borrowed)
            if (instance != null) Destroy(instance);
        borrowed.Clear();
        foreach (GameObject instance in pendingReturns)
            if (instance != null) Destroy(instance);
        pendingReturns.Clear();
    }
}
