using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SoilMaterialViewController : MonoBehaviour
{
    [SerializeField] private Renderer soilRenderer;
    [SerializeField, Min(0)] private int materialSlot;
    private Material original, runtime;
    private readonly HashSet<Object> requests = new HashSet<Object>();
    private static readonly int ViewId = Shader.PropertyToID("_DebugMode");

    private void Awake()
    {
        if (soilRenderer == null) soilRenderer = GetComponent<Renderer>();
        if (soilRenderer == null) { enabled = false; return; }
        Material[] materials = soilRenderer.sharedMaterials;
        if (materialSlot >= materials.Length || materials[materialSlot] == null ||
            !materials[materialSlot].HasProperty(ViewId))
        { Debug.LogError("SoilMaterialViewController: select a soil material with _DebugMode.", this); enabled = false; return; }
        original = materials[materialSlot];
        runtime = new Material(original);
        runtime.name = original.name + " (Hand View)";
        materials[materialSlot] = runtime;
        soilRenderer.sharedMaterials = materials;
        Apply();
    }
    public void RequestRGB(Object requester, bool active)
    {
        if (requester == null) return;
        if (active) requests.Add(requester); else requests.Remove(requester);
        Apply();
    }
    private void Apply()
    {
        requests.RemoveWhere(item => item == null);
        if (runtime != null) runtime.SetFloat(ViewId, isActiveAndEnabled && requests.Count > 0 ? 1f : 0f);
    }
    private void OnEnable() { Apply(); }
    private void OnDisable() { requests.Clear(); Apply(); }
    private void OnDestroy()
    {
        if (soilRenderer != null && runtime != null)
        {
            Material[] materials = soilRenderer.sharedMaterials;
            if (materialSlot < materials.Length && materials[materialSlot] == runtime)
            { materials[materialSlot] = original; soilRenderer.sharedMaterials = materials; }
        }
        if (runtime != null) Destroy(runtime);
    }
}
