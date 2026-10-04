using Oculus.Interaction;
using Oculus.Interaction.Input;
using Oculus.Interaction.Surfaces;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class SoilPatchPlacement : MonoBehaviour
{
    [Header("Scene references")]
    [Tooltip("Existing soil root. MUST start inactive in the scene, including its SoilManager.")]
    [SerializeField] private GameObject soilRoot;
    [Tooltip("The tracked CenterEyeAnchor / main camera.")]
    [SerializeField] private Transform head;
    [Tooltip("The hand RayInteractor used to reposition the preview.")]
    [SerializeField] private RayInteractor placementRay;
    [Tooltip("Meta Hand/SyntheticHand implementing IHand, for the SAME hand as Placement Ray.")]
    [SerializeField] private MonoBehaviour placementHand;
    private IHand hand;
    private bool wasPinching;
    private Vector3 dragOffset;
    [Tooltip("Transform at physical floor height. Uses its WORLD Y, not its orientation.")]
    [SerializeField] private Transform floorReference;
    [Tooltip("Used if Floor Reference is empty. Match the rig's floor height.")]
    [SerializeField] private float floorHeight;
    [Tooltip("Optional transparent preview material. Otherwise copies soil materials.")]
    [SerializeField] private Material previewMaterial;
    [Tooltip("Optional inactive gameplay root for pools/spawners. Activated AFTER the soil.")]
    [SerializeField] private GameObject gameplayRoot;
    [Tooltip("Placement button panel. Hidden after confirmation.")]
    [SerializeField] private GameObject placementControls;

    [Header("Placement")]
    [SerializeField, Min(0f)] private float initialDistance = 1.5f;
    [Tooltip("Vertical distance from the soil root pivot to the floor. Zero for a floor-level pivot.")]
    [SerializeField] private float rootHeightAboveFloor;
    [SerializeField, Min(0.1f)] private float maximumRayDistance = 6f;
    [SerializeField, Min(0.1f)] private float maximumDistanceFromHead = 4f;
    [SerializeField, Min(0.1f)] private float rotateStep = 15f;
    [SerializeField] private UnityEvent onConfirmed = new UnityEvent();

    public bool IsConfirmed { get; private set; }
    public bool IsMoving { get; private set; }
    public bool HasValidPlacement { get; private set; }
    public Vector3 PlacementPosition => previewRoot != null ? previewRoot.transform.position : Vector3.zero;
    private GameObject previewRoot;
    private float yaw;
    private Quaternion originalRootRotation;
    private float originalHeading;

    private float FloorY => floorReference != null ? floorReference.position.y : floorHeight;

    private void Start()
    {
        hand = placementHand as IHand;
        if (hand == null || placementRay == null)
        { Fail("Assign Placement Hand (Meta IHand) and its Placement Ray."); return; }
        if (head == null && Camera.main != null) head = Camera.main.transform;
        if (soilRoot == null || head == null)
        { Fail("Assign Soil Root and Head."); return; }
        if (soilRoot.activeSelf)
        { Fail("Soil Root must start inactive in the scene. Do not initialize SoilManager before placement."); return; }
        foreach (SoilManager manager in soilRoot.GetComponentsInChildren<SoilManager>(true))
            if (manager.IsInitialized)
            { Fail("SoilManager already initialized. Restart with soil and gameplay inactive."); return; }
        if (gameplayRoot != null && gameplayRoot.activeSelf)
        { Fail("Gameplay Root must start inactive so pools/spawners wait for soil placement."); return; }
        if (gameplayRoot != null && (transform.IsChildOf(gameplayRoot.transform) ||
            soilRoot.transform.IsChildOf(gameplayRoot.transform)))
        { Fail("Placement controller and soil must be outside Gameplay Root."); return; }
        if (transform.IsChildOf(soilRoot.transform))
        { Fail("Placement controller must be outside Soil Root."); return; }

        originalRootRotation = soilRoot.transform.rotation;
        Vector3 originalForward = Vector3.ProjectOnPlane(soilRoot.transform.forward, Vector3.up);
        originalHeading = originalForward.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(originalForward.normalized, Vector3.up).eulerAngles.y : 0f;
        previewRoot = new GameObject("Soil Placement Preview");
        previewRoot.transform.SetParent(soilRoot.transform.parent, false);
        previewRoot.transform.localScale = soilRoot.transform.localScale;
        CopyVisuals(soilRoot.transform, previewRoot.transform);
        ResetPlacement();
    }

    private void Fail(string message)
    {
        Debug.LogError("SoilPatchPlacement: " + message, this);
        enabled = false;
    }

    // Copies renderable meshes only: no SoilManager, colliders, or gameplay scripts.
    private void CopyVisuals(Transform source, Transform destination)
    {
        MeshFilter filter = source.GetComponent<MeshFilter>();
        MeshRenderer renderer = source.GetComponent<MeshRenderer>();
        if (filter != null && renderer != null && renderer.enabled)
        {
            destination.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            MeshRenderer copy = destination.gameObject.AddComponent<MeshRenderer>();
            Material[] materials = renderer.sharedMaterials;
            if (previewMaterial != null)
                for (int i = 0; i < materials.Length; i++) materials[i] = previewMaterial;
            copy.sharedMaterials = materials;
            copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            copy.receiveShadows = false;
            // A box interaction volume works even with flipped mesh normals.
            if (filter.sharedMesh != null)
            {
                BoxCollider collider = destination.gameObject.AddComponent<BoxCollider>();
                collider.center = filter.sharedMesh.bounds.center;
                Vector3 size = filter.sharedMesh.bounds.size;
                collider.size = new Vector3(Mathf.Max(0.01f,size.x),
                    Mathf.Max(0.02f,size.y),Mathf.Max(0.01f,size.z));
                ColliderSurface surface = destination.gameObject.AddComponent<ColliderSurface>();
                surface.InjectAllColliderSurface(collider);
                RayInteractable interactable = destination.gameObject.AddComponent<RayInteractable>();
                interactable.InjectAllRayInteractable(surface);
            }
        }
        for (int i = 0; i < source.childCount; i++)
        {
            Transform child = source.GetChild(i);
            if (!child.gameObject.activeSelf) continue;
            Transform copy = new GameObject(child.name).transform;
            copy.SetParent(destination, false);
            copy.localPosition = child.localPosition;
            copy.localRotation = child.localRotation;
            copy.localScale = child.localScale;
            CopyVisuals(child, copy);
        }
    }

    private void Update()
    {
        if (IsConfirmed || previewRoot == null) return;
        bool tracked = hand != null && hand.IsConnected && hand.IsTrackedDataValid;
        bool pinching = tracked && hand.GetIndexFingerIsPinching();
        if (!tracked || placementRay == null || !placementRay.isActiveAndEnabled)
        {
            IsMoving = false;
            // Require a fresh pinch after tracking/ray availability returns.
            wasPinching = true;
            return;
        }
        if (!pinching) IsMoving = false;
        if (pinching && !wasPinching)
        {
            var candidate = placementRay.CandidateProperties as RayInteractor.RayCandidateProperties;
            RayInteractable target = candidate != null ? candidate.ClosestInteractable : null;
            if (target != null && target.transform.IsChildOf(previewRoot.transform) &&
                TryFloorPoint(out Vector3 point))
            {
                IsMoving = true;
                // Keep the grabbed position under the ray without jumping the pivot.
                dragOffset = previewRoot.transform.position - point;
                dragOffset.y = 0f;
            }
        }
        wasPinching = pinching;
        if (!IsMoving || !TryFloorPoint(out Vector3 floorPoint)) return;
        Vector3 position = floorPoint + dragOffset;
        position.y = FloorY + rootHeightAboveFloor;
        if (!WithinReach(position)) return;
        previewRoot.transform.position = position;
        HasValidPlacement = true;
    }

    private bool TryFloorPoint(out Vector3 point)
    {
        point = default;
        Plane floor = new Plane(Vector3.up, new Vector3(0f, FloorY, 0f));
        Ray ray = placementRay.Ray;
        if (!floor.Raycast(ray, out float distance) || distance > maximumRayDistance) return false;
        point = ray.GetPoint(distance);
        return true;
    }

    private bool WithinReach(Vector3 point)
    {
        Vector3 delta = point - head.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= maximumDistanceFromHead * maximumDistanceFromHead;
    }

    public void RotateLeft() { Rotate(-rotateStep); }
    public void RotateRight() { Rotate(rotateStep); }
    private void Rotate(float degrees)
    {
        if (IsConfirmed || previewRoot == null) return;
        yaw = Mathf.Repeat(yaw + degrees, 360f);
        previewRoot.transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * originalRootRotation;
    }

    public void ResetPlacement()
    {
        if (IsConfirmed || previewRoot == null || head == null) return;
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();
        yaw = Quaternion.LookRotation(forward, Vector3.up).eulerAngles.y - originalHeading;
        Vector3 position = head.position + forward * Mathf.Min(initialDistance, maximumDistanceFromHead);
        position.y = FloorY + rootHeightAboveFloor;
        previewRoot.transform.SetPositionAndRotation(position,
            Quaternion.AngleAxis(yaw, Vector3.up) * originalRootRotation);
        HasValidPlacement = true;
        IsMoving = false;
    }

    public void ConfirmPlacement()
    {
        if (IsConfirmed || !HasValidPlacement || previewRoot == null) return;
        if (!WithinReach(previewRoot.transform.position)) return;
        foreach (SoilManager manager in soilRoot.GetComponentsInChildren<SoilManager>(true))
            if (manager.IsInitialized)
            { Fail("Soil initialized during preview. Restart before confirming."); return; }
        IsMoving = false;
        soilRoot.transform.SetPositionAndRotation(
            previewRoot.transform.position, previewRoot.transform.rotation);
        // Awake/initialization now sees the final world position.
        soilRoot.SetActive(true);
        IsConfirmed = true;
        Destroy(previewRoot);
        if (placementControls != null) placementControls.SetActive(false);
        if (gameplayRoot != null) gameplayRoot.SetActive(true);
        onConfirmed.Invoke();
    }

    private void OnDisable() { IsMoving = false; wasPinching = true; }
    private void OnDestroy() { if (previewRoot != null) Destroy(previewRoot); }
}
