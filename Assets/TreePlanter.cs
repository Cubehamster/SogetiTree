using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public sealed class TreePlanter : MonoBehaviour
{
    [SerializeField] private Grabbable grabbable;
    [Tooltip("The tree root moved by Grabbable. Defaults to Grabbable.Transform.")]
    [SerializeField] private Transform treeTransform;
    [Tooltip("Optional child at the trunk base. Otherwise uses the tree pivot.")]
    [SerializeField] private Transform rootPoint;
    [Tooltip("Use the same Rigidbody referenced by Grabbable.")]
    [SerializeField] private Rigidbody treeRigidbody;
    [SerializeField, Min(0.01f)] private float rayDistance = 1.5f;
    [SerializeField, Min(0f)] private float rayStartOffset = 0.1f;
    [SerializeField, Min(0f)] private float snapDuration = 0.3f;
    [SerializeField] private UnityEvent onPlanted = new UnityEvent();

    [Header("Held tree soil preview")]
    [Tooltip("Optional. Otherwise resolved from the hit collider or its parents.")]
    [SerializeField] private SoilManager soilManager;
    [SerializeField] private DecalProjector previewDecal;
    [Tooltip("Shader Graph color property's Reference name, not its display name.")]
    [SerializeField] private string decalColorProperty = "_BaseColor";
    [SerializeField, Min(0.01f)] private float sampleRadius = 0.5f;
    [SerializeField, Min(1f)] private float previewTicksPerSecond = 10f;
    [SerializeField, Min(0.01f)] private float falloffPower = 2f;

    public bool CanPlant { get; private set; }
    public bool HasSoilSample { get; private set; }
    public Color AverageColor { get; private set; }
    public Vector3 PreviewHitPoint { get; private set; }

    private Material originalDecalMaterial, decalMaterial;
    private int colorPropertyId;
    private float nextPreviewTime;

    public bool IsPlanted { get; private set; }
    public bool IsSnapping => snapping;

    private const int TerrainMask = 1 << 3;
    private bool releasePending, snapping, ownsPhysics;
    private bool previousKinematic, previousGravity;
    private float elapsed;
    private Vector3 startPosition, targetPosition;

    public void SetSoilManager(SoilManager manager)
    {
        soilManager = manager;
    }

    private void Reset()
    {
        grabbable = GetComponent<Grabbable>();
        treeRigidbody = GetComponent<Rigidbody>();
    }

    private void Awake()
    {
        if (grabbable == null) grabbable = GetComponent<Grabbable>();
        if (grabbable == null)
        {
            Debug.LogError("TreePlanting: assign a Meta Grabbable.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (grabbable != null)
            grabbable.WhenPointerEventRaised += HandlePointerEvent;
    }

    private void Start()
    {
        // Meta initializes its target transform during Start.
        ResolveTree();
        if (previewDecal == null)
            previewDecal = treeTransform.GetComponentInChildren<DecalProjector>(true);
        if (previewDecal != null && previewDecal.material != null)
        {
            originalDecalMaterial = previewDecal.material;
            decalMaterial = new Material(originalDecalMaterial);
            decalMaterial.name = originalDecalMaterial.name + " (Tree Preview)";
            colorPropertyId = Shader.PropertyToID(decalColorProperty);
            if (!decalMaterial.HasProperty(colorPropertyId))
                Debug.LogWarning("TreePlanter: decal color Reference not found: " + decalColorProperty, this);
            previewDecal.material = decalMaterial;
        }
        HidePreview();
    }

    private void ResolveTree()
    {
        if (treeTransform == null)
            treeTransform = grabbable.Transform != null
                ? grabbable.Transform : grabbable.transform;
        if (treeRigidbody == null)
            treeRigidbody = treeTransform.GetComponent<Rigidbody>();
    }

    private void HandlePointerEvent(PointerEvent evt)
    {
        if (evt.Type == PointerEventType.Select)
        {
            releasePending = false;
            nextPreviewTime = 0f;
            snapping = false;
            IsPlanted = false;
            // Leave the Rigidbody kinematic while Meta holds it. Restore its
            // original physics after the final release, before checking soil.
        }
        else if (evt.Type == PointerEventType.Unselect)
        {
            releasePending = true;
        }
        else if (evt.Type == PointerEventType.Cancel)
        {
            releasePending = false;
        }
    }

    private void LateUpdate()
    {
        if (grabbable == null) return;
        bool held = grabbable.GrabPoints.Count > 0;
        if (held && !snapping && Time.time >= nextPreviewTime)
        {
            nextPreviewTime = Time.time + 1f / Mathf.Max(1f, previewTicksPerSecond);
            UpdatePreview();
        }
        else if (!held) HidePreview();
        // Defer until Meta has completed release/throw handling.
        if (releasePending)
        {
            releasePending = false;
            if (grabbable.GrabPoints.Count == 0 && !IsPlanted && !snapping)
            {
                RestorePhysics();
                TryPlant();
            }
        }

        if (!snapping) return;
        elapsed += Time.deltaTime;
        float t = snapDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / snapDuration);
        treeTransform.position = Vector3.Lerp(startPosition, targetPosition, t);
        if (t < 1f) return;

        treeTransform.position = targetPosition;
        snapping = false;
        IsPlanted = true;
        onPlanted.Invoke();
    }

    private void TryPlant()
    {
        ResolveTree();
        Vector3 rootPosition = rootPoint != null ? rootPoint.position : treeTransform.position;
        if (!TryGetPlantHit(out RaycastHit hit)) return;
        HidePreview();

        startPosition = treeTransform.position;
        // Preserve rotation and place the root marker exactly on the hit point.
        targetPosition = startPosition + hit.point - rootPosition;
        if (treeRigidbody != null)
        {
            previousKinematic = treeRigidbody.isKinematic;
            previousGravity = treeRigidbody.useGravity;
            ownsPhysics = true;
            if (!treeRigidbody.isKinematic)
            {
                treeRigidbody.linearVelocity = Vector3.zero;
                treeRigidbody.angularVelocity = Vector3.zero;
            }
            treeRigidbody.useGravity = false;
            treeRigidbody.isKinematic = true;
        }
        elapsed = 0f;
        snapping = true;
    }

    private bool TryGetPlantHit(out RaycastHit hit)
    {
        ResolveTree();
        Vector3 root = rootPoint != null ? rootPoint.position : treeTransform.position;
        bool found = Physics.Raycast(root + Vector3.up * rayStartOffset,
            Vector3.down, out hit, rayDistance + rayStartOffset,
            TerrainMask, QueryTriggerInteraction.Ignore);
        return found && Vector3.Dot(treeTransform.up, Vector3.up) > 0.7f;
    }

    private void UpdatePreview()
    {
        CanPlant = TryGetPlantHit(out RaycastHit hit);
        HasSoilSample = false;
        if (!CanPlant) { HidePreview(); return; }
        PreviewHitPoint = hit.point;
        HasSoilSample = TryAverageSoil(hit, out Color average);
        if (!HasSoilSample) { SetDecalVisible(false); return; }
        AverageColor = average;
        if (decalMaterial != null && decalMaterial.HasProperty(colorPropertyId))
        {
            // Soil alpha is not data; preserve the material's original opacity.
            average.a = originalDecalMaterial.GetColor(colorPropertyId).a;
            decalMaterial.SetColor(colorPropertyId, average);
        }
        SetDecalVisible(decalMaterial != null && decalMaterial.HasProperty(colorPropertyId));
    }

    private bool TryAverageSoil(RaycastHit hit, out Color average)
    {
        average = default;
        // Resolve the actual hit surface, so crossing between soil meshes
        // doesn't accidentally sample the wrong manager.
        SoilManager manager = hit.collider.GetComponentInParent<SoilManager>();
        if (manager == null && soilManager != null &&
            hit.collider.transform.IsChildOf(soilManager.transform))
            manager = soilManager;
        if (manager == null) return false;

        // Read authoritative cached RGB values. No mesh color reads or
        // per-tree vertex caches/material copies are performed here.
        return manager.TrySampleRadius(hit.point, sampleRadius,
            out average, falloffPower);
    }

    private void SetDecalVisible(bool visible)
    {
        if (previewDecal != null) previewDecal.enabled = visible;
    }

    private void HidePreview()
    {
        CanPlant = false;
        HasSoilSample = false;
        SetDecalVisible(false);
    }

    private void OnDestroy()
    {
        if (previewDecal != null && previewDecal.material == decalMaterial)
            previewDecal.material = originalDecalMaterial;
        if (decalMaterial != null) Destroy(decalMaterial);
    }

    private void OnDrawGizmosSelected()
    {
        Transform target = treeTransform != null ? treeTransform : transform;
        if (treeTransform == null && grabbable != null)
            target = grabbable.Transform != null ? grabbable.Transform : grabbable.transform;
        Vector3 root = rootPoint != null ? rootPoint.position : target.position;
        Vector3 origin = root + Vector3.up * rayStartOffset;
        Vector3 end = origin + Vector3.down * (rayDistance + rayStartOffset);
        Color old = Gizmos.color;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(root, origin);
        Gizmos.DrawWireSphere(origin, 0.025f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, end);
        Gizmos.DrawWireSphere(end, 0.025f);
        if (Application.isPlaying && CanPlant)
        {
            Gizmos.color = HasSoilSample ? AverageColor : Color.yellow;
            Gizmos.DrawWireSphere(PreviewHitPoint, sampleRadius);
        }
        Gizmos.color = old;
    }

    private void RestorePhysics()
    {
        if (!ownsPhysics || treeRigidbody == null) return;
        treeRigidbody.useGravity = previousGravity;
        treeRigidbody.isKinematic = previousKinematic;
        ownsPhysics = false;
    }

    private void OnDisable()
    {
        HidePreview();
        if (grabbable != null)
            grabbable.WhenPointerEventRaised -= HandlePointerEvent;
        releasePending = false;
        snapping = false;
        IsPlanted = false;
        // Avoid overriding Meta's physics lock if disabled during a grab.
        if (grabbable == null || grabbable.GrabPoints.Count == 0) RestorePhysics();
    }
}
