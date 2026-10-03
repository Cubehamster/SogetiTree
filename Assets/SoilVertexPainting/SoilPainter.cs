using UnityEngine;
namespace TreePlanting {
public sealed class SoilPainter : MonoBehaviour {
    public enum CastMode { Raycast, Spherecast }
    [Tooltip("Forward (+Z) points along the painting ray. Defaults to this transform.")]
    public Transform brushSource;
    public CastMode castMode;
    public LayerMask layers = ~0;
    [Min(.01f)] public float maxDistance = 5;
    [Min(.001f)] public float sphereCastRadius = .05f;
    [Min(.001f)] public float brushRadius = .3f;
    public SoilChannel channel = SoilChannel.Water;
    public SoilPaintMode mode = SoilPaintMode.Add;
    [Tooltip("Add: signed change per second. Set: target value in [0,1].")]
    [Range(-1,1)] public float value = .4f;
    [Min(0)] public float strength = 1;
    [Min(.01f)] public float falloffPower = 1;
    [Tooltip("Enable for a quick continuous-paint test; normally controlled by VR events.")]
    public bool painting;
    public bool HasHit { get; private set; }
    public RaycastHit LastHit { get; private set; }
    public void BeginPainting() => painting = true;
    public void EndPainting() => painting = false;
    public void SetPainting(bool active) => painting = active;
    void Update() { if (painting) PaintStep(Time.deltaTime); }
    public int PaintStep(float deltaSeconds) {
        Transform source = brushSource != null ? brushSource : transform;
        Ray ray = new Ray(source.position, source.forward);
        RaycastHit hit;
        HasHit = castMode == CastMode.Raycast
            ? Physics.Raycast(ray, out hit, maxDistance, layers, QueryTriggerInteraction.Ignore)
            : Physics.SphereCast(ray, sphereCastRadius, out hit, maxDistance, layers, QueryTriggerInteraction.Ignore);
        if (!HasHit) return 0;
        LastHit = hit;
        // Collider and SoilSurface deliberately live on the same object.
        SoilSurface soil = hit.collider.GetComponent<SoilSurface>();
        return soil == null ? 0 : soil.Paint(hit.point, brushRadius, channel, value,
            strength * Mathf.Max(0, deltaSeconds), mode, falloffPower);
    }
    void OnDrawGizmosSelected() {
        Transform source = brushSource != null ? brushSource : transform;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(source.position, source.position + source.forward * maxDistance);
        if (Application.isPlaying && HasHit) Gizmos.DrawWireSphere(LastHit.point, brushRadius);
    }
}
}
