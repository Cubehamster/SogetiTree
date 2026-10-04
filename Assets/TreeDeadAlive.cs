using UnityEngine;

[DisallowMultipleComponent]
public sealed class TreeDeadAlive : MonoBehaviour
{
    [SerializeField] private TreeState treeState;
    [SerializeField] private MeshRenderer leavesRenderer;

    private void OnEnable()
    {
        Bind(GetComponentInParent<TreeState>());
    }

    private void OnDisable()
    {
        treeState = null;
    }

    public void Bind(TreeState owner)
    {
        treeState = owner;

        if (treeState != null)
            ApplyVisuals(treeState.IsAlive);
    }

    public void SetAlive(bool alive)
    {
        if (treeState != null)
            treeState.SetAlive(alive);

        ApplyVisuals(alive);
    }

    public void MakeAlive()
    {
        SetAlive(true);
    }

    public void MakeDead()
    {
        SetAlive(false);
    }

    public void ApplyVisuals(bool alive)
    {
        if (leavesRenderer != null)
            leavesRenderer.enabled = alive;
    }
}