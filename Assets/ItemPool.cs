using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ItemPool : MonoBehaviour
{
    [SerializeField] private GameObject prefab;
    [SerializeField, Min(0)] private int initialSize = 20;
    [SerializeField] private SoilManager soilManager;
    [SerializeField] private TreeManager treeManager;
    [SerializeField] private Transform spawnedItemParent;
    private readonly Queue<GameObject> available = new Queue<GameObject>();
    private readonly Queue<GameObject> pendingReturns = new Queue<GameObject>();
    private readonly HashSet<GameObject> borrowed = new HashSet<GameObject>();
    private Transform storage;
    private bool initialized;

    private void Awake() { Initialize(); }
    private bool Initialize()
    {
        if (initialized) return true;
        if (prefab == null || soilManager == null)
        { Debug.LogError("ItemPool: assign item Prefab and SoilManager.", this); return false; }
        if (prefab.GetComponentInChildren<SoilAreaItem>(true) == null)
        { Debug.LogError("ItemPool: item prefab needs SoilAreaItem.", this); return false; }
        GameObject container = new GameObject("Pooled Items");
        container.transform.SetParent(transform, false);
        container.SetActive(false);
        storage = container.transform;
        initialized = true;
        for (int i = 0; i < initialSize; i++) available.Enqueue(Create());
        return true;
    }

    private GameObject Create()
    {
        GameObject instance = Instantiate(prefab, storage, false);
        instance.SetActive(false);
        return instance;
    }

    public GameObject Get(Vector3 position, Quaternion rotation, TreeManager managerOverride = null)
    {
        if (!Initialize()) return null;
        GameObject instance = null;
        while (available.Count > 0 && instance == null) instance = available.Dequeue();
        if (instance == null) instance = Create();
        borrowed.Add(instance);
        instance.transform.SetParent(spawnedItemParent, false);
        instance.transform.localScale = prefab.transform.localScale;
        instance.transform.SetPositionAndRotation(position, rotation);
        foreach (SoilAreaItem effect in instance.GetComponentsInChildren<SoilAreaItem>(true))
            effect.SetPoolContext(this, instance, soilManager, managerOverride != null ? managerOverride : treeManager);
        instance.SetActive(true);
        return instance;
    }

    public void Return(GameObject instance)
    {
        if (instance == null || !borrowed.Remove(instance)) return;
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

    private void OnDestroy()
    {
        foreach (GameObject instance in borrowed) if (instance != null) Destroy(instance);
        foreach (GameObject instance in pendingReturns) if (instance != null) Destroy(instance);
        borrowed.Clear();
        pendingReturns.Clear();
    }
}
