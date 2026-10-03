using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

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

    public bool IsPlanted { get; private set; }
    public bool IsSnapping => snapping;

    private const int TerrainMask = 1 << 3;
    private bool releasePending, snapping, ownsPhysics;
    private bool previousKinematic, previousGravity;
    private float elapsed;
    private Vector3 startPosition, targetPosition;

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

        Vector3 rootPosition = rootPoint != null
            ? rootPoint.position
            : treeTransform.position;

        Vector3 origin = rootPosition + Vector3.up * rayStartOffset;
        float distance = rayDistance + rayStartOffset;

        Debug.DrawRay(origin, Vector3.down * distance, Color.cyan, 3f);

        if (!Physics.Raycast(
                origin,
                Vector3.down,
                out RaycastHit hit,
                distance,
                TerrainMask,
                QueryTriggerInteraction.Ignore))
        {
            Debug.Log(
                $"[TreePlanter] No terrain hit. Origin: {origin}, " +
                $"distance: {distance}. Ground must have a non-trigger " +
                "collider on physics layer 3.",
                this);
            return;
        }

        float upright = Vector3.Dot(treeTransform.up, Vector3.up);

        Debug.Log(
            $"[TreePlanter] Hit {hit.collider.name} at {hit.point}. " +
            $"Upright: {upright:F3} (must be > 0.7).",
            this);

        if (upright <= 0.7f)
            return;

        startPosition = treeTransform.position;
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

        Debug.Log($"[TreePlanter] Starting snap to {targetPosition}.", this);
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
        if (grabbable != null)
            grabbable.WhenPointerEventRaised -= HandlePointerEvent;
        releasePending = false;
        snapping = false;
        IsPlanted = false;
        // Avoid overriding Meta's physics lock if disabled during a grab.
        if (grabbable == null || grabbable.GrabPoints.Count == 0) RestorePhysics();
    }

    private void OnDrawGizmosSelected()
    {
        Transform target = treeTransform;

        if (target == null && grabbable != null)
            target = grabbable.Transform != null
                ? grabbable.Transform
                : grabbable.transform;

        if (target == null)
            target = transform;

        Vector3 rootPosition = rootPoint != null
            ? rootPoint.position
            : target.position;

        Vector3 origin = rootPosition + Vector3.up * rayStartOffset;
        Vector3 end = origin + Vector3.down * (rayDistance + rayStartOffset);

        Color previousColor = Gizmos.color;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(rootPosition, origin);
        Gizmos.DrawWireSphere(origin, 0.025f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, end);
        Gizmos.DrawWireSphere(end, 0.025f);

        Gizmos.color = previousColor;
    }
}
