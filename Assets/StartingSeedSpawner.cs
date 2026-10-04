using UnityEngine;

public sealed class StartingSeedSpawner : MonoBehaviour
{
    [SerializeField] private TreeObjectPool treeObjectPool;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private bool spawnOnStart = true;

    private void Start()
    {
        if (spawnOnStart)
            SpawnSeed();
    }

    public void SpawnSeed()
    {
        if (treeObjectPool == null)
            return;

        Transform point = spawnPoint != null ? spawnPoint : transform;

        GameObject tree = treeObjectPool.Get(
            point.position,
            point.rotation);

        if (tree == null)
            return;

        TreeState state = tree.GetComponentInChildren<TreeState>(true);

        if (state != null)
        {
            state.SetStage(TreeState.GrowthStage.Seed);
            state.MakeAlive();
        }
    }
}