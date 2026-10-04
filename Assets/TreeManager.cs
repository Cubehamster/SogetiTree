using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class TreeManager : MonoBehaviour
{
    [Serializable]
    public sealed class TreeTypePool
    {
        public string treeType;
        public TreeObjectPool pool;
    }

    [Header("Game")]
    [SerializeField, Min(0)] private int startingPoints = 200;
    [SerializeField, Min(1f)] private float gameDurationSeconds = 600f;
    [SerializeField] private bool startAutomatically;
    [SerializeField] private UnityEvent onGameEnded = new UnityEvent();
    public int Score { get; private set; }
    public float RemainingSeconds { get; private set; }
    public bool IsGameRunning { get; private set; }
    public event Action<int> ScoreChanged;
    public string TimerText
    {
        get { int seconds = Mathf.CeilToInt(RemainingSeconds); return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00"); }
    }
    private void Awake() { Score = startingPoints; RemainingSeconds = gameDurationSeconds; }
    private void Start() { if (startAutomatically) StartGame(); }
    public void StartGame()
    {
        if (IsGameRunning) return;
        RemainingSeconds = gameDurationSeconds;
        IsGameRunning = true;
    }
    private void Update()
    {
        if (!IsGameRunning) return;
        RemainingSeconds = Mathf.Max(0f, RemainingSeconds - Time.deltaTime);
        if (RemainingSeconds <= 0f) { IsGameRunning = false; onGameEnded.Invoke(); }
    }
    public void AddPoints(int amount)
    {
        if (!IsGameRunning || amount <= 0) return;
        Score = (int)Math.Min(int.MaxValue, (long)Score + amount);
        ScoreChanged?.Invoke(Score);
    }
    public bool TrySpendPoints(int amount)
    {
        if (!IsGameRunning || amount < 0 || Score < amount) return false;
        Score -= amount;
        ScoreChanged?.Invoke(Score);
        return true;
    }

    [SerializeField] private TreeTypePool[] treePools = new TreeTypePool[0];
    [Tooltip("Optional parent for complete spawned trees. Avoid placing beneath scaled transforms.")]
    [SerializeField] private Transform spawnedTreeParent;

    private readonly HashSet<TreeState> activeTrees = new HashSet<TreeState>();
    private readonly Dictionary<GameObject, TreeObjectPool> spawnedPools =
        new Dictionary<GameObject, TreeObjectPool>();

    public int ActiveTreeCount => activeTrees.Count;
    public IEnumerable<TreeState> ActiveTrees => activeTrees;

    // Existing scene trees register through their TreeState Tree Manager field.
    public void RegisterTree(TreeState tree)
    {
        if (tree != null && tree.isActiveAndEnabled) activeTrees.Add(tree);
    }

    public void UnregisterTree(TreeState tree)
    {
        if (tree != null) activeTrees.Remove(tree);
    }

    // Checks current world locations only at attempted growth, with no physics query.
    // Only planted neighbours count. Dead trees block at half the configured radius.
    public bool HasLargerNeighbour(TreeState tree, float radius)
    {
        if (tree == null || radius <= 0f) return false;
        Vector3 center = tree.Location;
        foreach (TreeState other in activeTrees)
        {
            if (other == null || other == tree || !other.isActiveAndEnabled || !other.IsPlanted) continue;
            if ((int)other.Stage <= (int)tree.Stage) continue;
            float blockingRadius = other.IsAlive ? radius : radius * 0.5f;
            if ((other.Location - center).sqrMagnitude <= blockingRadius * blockingRadius)
                return true;
        }
        return false;
    }

    public GameObject SpawnTree(string treeType, Vector3 position, Quaternion rotation)
    {
        foreach (TreeTypePool entry in treePools)
            if (entry != null && entry.treeType == treeType)
                return SpawnTree(entry.pool, position, rotation);
        Debug.LogWarning("TreeManager: no tree pool configured for " + treeType, this);
        return null;
    }

    public GameObject SpawnTree(TreeObjectPool pool, Vector3 position, Quaternion rotation)
    {
        if (pool == null) return null;
        // Manager injected before activation, so TreeState registers in OnEnable.
        GameObject instance = pool.Get(position, rotation, spawnedTreeParent, this);
        if (instance != null) spawnedPools[instance] = pool;
        return instance;
    }

    public bool ReturnTree(GameObject tree)
    {
        if (tree == null || !spawnedPools.TryGetValue(tree, out TreeObjectPool pool)) return false;
        spawnedPools.Remove(tree);
        if (pool != null) pool.Return(tree);
        else Destroy(tree);
        return true;
    }

    // Writes locations into caller-owned storage. Use when another system needs a snapshot.
    public void GetTreeLocations(List<Vector3> results)
    {
        if (results == null) throw new ArgumentNullException(nameof(results));
        results.Clear();
        foreach (TreeState tree in activeTrees)
            if (tree != null && tree.isActiveAndEnabled) results.Add(tree.Location);
    }
}
