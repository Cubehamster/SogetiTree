using UnityEngine;

namespace TreePlanting
{
    [DisallowMultipleComponent]
    public sealed class SoilPainter : MonoBehaviour
    {
        public enum CastMode { Raycast, Spherecast }
        public enum Channel { Roughness, Nutrients, Water }
        public enum PaintMode { Add, Set }

        [Header("Soil selection — optional filters")]
        [Tooltip("Drag an existing SoilManager from the scene. Empty allows any hit manager.")]
        public SoilManager targetSoil;
        [Tooltip("Optional existing painted Mesh asset. Only managers using this baseline can be painted. Does not replace or modify the asset.")]
        public Mesh soilAsset;

        [Header("Brush")]
        [Tooltip("Forward (+Z) points along the painting ray. Defaults to this transform.")]
        public Transform brushSource;
        public CastMode castMode;
        public LayerMask layers = 1 << 3;
        [Min(0.01f)] public float maxDistance = 5f;
        [Min(0.001f)] public float sphereCastRadius = 0.05f;
        [Min(0.001f)] public float brushRadius = 0.3f;
        public Channel channel = Channel.Water;
        public PaintMode mode = PaintMode.Add;
        [Tooltip("Add: signed change per second per vertex at brush center. Set: target value.")]
        [Range(-1f, 1f)] public float value = 0.4f;
        [Min(0f)] public float strength = 1f;
        [Min(0.01f)] public float falloffPower = 1f;
        [Min(1f)] public float paintChecksPerSecond = 10f;
        public bool painting;

        public bool HasHit { get; private set; }
        public bool HasValidSoil { get; private set; }
        public RaycastHit LastHit { get; private set; }
        public SoilManager LastSoil { get; private set; }
        private float accumulatedTime;

        public void BeginPainting() => SetPainting(true);
        public void EndPainting() => SetPainting(false);
        public void SetPainting(bool active)
        {
            if (painting != active) accumulatedTime = 0f;
            painting = active;
            if (!active) ClearHit();
        }

        private void Update()
        {
            if (!painting) { accumulatedTime = 0f; ClearHit(); return; }
            accumulatedTime += Time.deltaTime;
            if (accumulatedTime < 1f / Mathf.Max(1f, paintChecksPerSecond)) return;
            float elapsed = accumulatedTime;
            accumulatedTime = 0f;
            PaintStep(elapsed);
        }

        // Returns number of vertices included in the queued brush, not uploaded vertices.
        // Call manually instead of enabling continuous painting if scheduling externally.
        public int PaintStep(float deltaSeconds)
        {
            ClearHit();
            if (deltaSeconds <= 0f || strength <= 0f) return 0;
            Transform source = brushSource != null ? brushSource : transform;
            Ray ray = new Ray(source.position, source.forward);
            RaycastHit hit;
            HasHit = castMode == CastMode.Raycast
                ? Physics.Raycast(ray, out hit, maxDistance, layers, QueryTriggerInteraction.Ignore)
                : Physics.SphereCast(ray, sphereCastRadius, out hit, maxDistance, layers, QueryTriggerInteraction.Ignore);
            if (!HasHit) return 0;
            LastHit = hit;
            SoilManager manager = hit.collider.GetComponentInParent<SoilManager>();
            if (manager == null || !manager.isActiveAndEnabled) return 0;
            if (targetSoil != null && targetSoil != manager) return 0;
            if (!manager.Initialize()) return 0;
            if (soilAsset != null && manager.BaselineMesh != soilAsset) return 0;
            HasValidSoil = true;
            LastSoil = manager;
            float amount = strength * deltaSeconds;
            if (mode == PaintMode.Set)
                return manager.QueueSetBrush(hit.point, brushRadius, (int)channel,
                    value, amount, falloffPower);
            Vector3 delta = Vector3.zero;
            delta[(int)channel] = value * amount;
            return manager.QueueBrush(hit.point, brushRadius, delta, falloffPower);
        }

        private void ClearHit()
        {
            HasHit = false;
            HasValidSoil = false;
            LastSoil = null;
        }

        private void OnDisable() { accumulatedTime = 0f; ClearHit(); }

        private void OnDrawGizmosSelected()
        {
            Transform source = brushSource != null ? brushSource : transform;
            Color old = Gizmos.color;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(source.position, source.position + source.forward * maxDistance);
            if (Application.isPlaying && HasHit)
            {
                Gizmos.color = HasValidSoil ? Color.green : Color.red;
                Gizmos.DrawWireSphere(LastHit.point, brushRadius);
            }
            Gizmos.color = old;
        }
    }
}
