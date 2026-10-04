using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class HandSoilInspector : MonoBehaviour
{
    [Tooltip("Meta Hand or SyntheticHand component implementing IHand.")]
    [SerializeField] private MonoBehaviour handSource;
    [SerializeField] private RayInteractor rayInteractor;
    [SerializeField] private TreeSoilDisplay display;
    [SerializeField] private Transform head;
    [SerializeField] private Transform displayAnchor;
    [SerializeField] private Vector3 displayWorldOffset = new Vector3(0f, 0.18f, 0f);
    [SerializeField] private SoilMaterialViewController materialView;
    [SerializeField, Min(0.01f)] private float sampleRadius = 0.5f;
    [SerializeField, Min(1f)] private float sampleTicksPerSecond = 10f;
    [SerializeField, Min(0.01f)] private float falloffPower = 2f;
    [SerializeField] private bool requireIndexExtended = true;
    [Header("Palm inspection")]
    [Tooltip("Toggle if the cyan palm normal points out of the back of this hand.")]
    [SerializeField] private bool invertPalmNormal;
    [SerializeField, Range(-1f,1f)] private float fingerStraightness = 0.65f;
    [SerializeField, Range(0f,90f)] private float headLookAngle = 20f;
    [SerializeField, Range(0f,90f)] private float palmFacingAngle = 55f;
    [SerializeField, Min(0.01f)] private float maximumPalmDistance = 0.8f;
    [SerializeField, Min(0f)] private float activationDelay = 0.2f;
    [SerializeField, Min(0f)] private float releaseDelay = 0.12f;
    public bool InspectingPalm { get; private set; }
    private IHand hand;
    private float validTime, invalidTime, nextSample;
    private Color lastSample;
    private bool hasSample;
    private SoilManager lastManager;
    private Vector3 palmPosition, palmNormal;

    private void Awake()
    {
        hand = handSource as IHand;
        if (head == null && Camera.main != null) head = Camera.main.transform;
        if (hand == null)
        { Debug.LogError("HandSoilInspector: assign a component implementing Meta IHand.", this); enabled = false; }
    }
    private void LateUpdate()
    {
        if (hand == null || !hand.IsConnected || !hand.IsTrackedDataValid)
        { Clear(); return; }
        bool palmVisible = OpenPalmFacesHead();
        if (palmVisible)
        {
            invalidTime = 0f; validTime += Time.deltaTime;
            if (validTime >= activationDelay) InspectingPalm = true;
        }
        else
        {
            validTime = 0f; invalidTime += Time.deltaTime;
            if (invalidTime >= releaseDelay) InspectingPalm = false;
        }
        if (materialView != null) materialView.RequestRGB(this, InspectingPalm);
        UpdatePointer();
    }

    private void UpdatePointer()
    {
        if (display == null) return;
        var candidate = rayInteractor != null && rayInteractor.isActiveAndEnabled
            ? rayInteractor.CandidateProperties as RayInteractor.RayCandidateProperties : null;
        RayInteractable target = candidate != null ? candidate.ClosestInteractable : null;
        SoilManager manager = target != null ? target.GetComponentInParent<SoilManager>() : null;
        if (manager == null || !manager.isActiveAndEnabled ||
            (requireIndexExtended && !Straight(HandJointId.HandIndex1, HandJointId.HandIndex2, HandJointId.HandIndexTip)))
        { hasSample = false; lastManager = null; display.HideExternalSoil(); return; }
        if (Time.time >= nextSample || manager != lastManager)
        {
            nextSample = Time.time + 1f / Mathf.Max(1f, sampleTicksPerSecond);
            lastManager = manager;
            hasSample = manager.TrySampleRadius(rayInteractor.End, sampleRadius, out lastSample, falloffPower);
        }
        if (!hasSample) { display.HideExternalSoil(); return; }
        Vector3 anchorPosition;
        if (displayAnchor != null) anchorPosition = displayAnchor.position;
        else if (hand.GetJointPose(HandJointId.HandWristRoot, out Pose wrist)) anchorPosition = wrist.position;
        else { display.HideExternalSoil(); return; }
        display.ShowExternalSoil(lastSample, anchorPosition + displayWorldOffset);
    }

    private bool Straight(HandJointId root, HandJointId middle, HandJointId tip)
    {
        if (!hand.GetJointPose(root, out Pose a) || !hand.GetJointPose(middle, out Pose b) ||
            !hand.GetJointPose(tip, out Pose c)) return false;
        Vector3 first = b.position-a.position, last = c.position-b.position;
        return first.sqrMagnitude > 1e-8f && last.sqrMagnitude > 1e-8f &&
            Vector3.Dot(first.normalized, last.normalized) >= fingerStraightness;
    }

    private bool OpenPalmFacesHead()
    {
        if (head == null || !hand.GetJointPose(HandJointId.HandWristRoot, out Pose wrist) ||
            !hand.GetJointPose(HandJointId.HandIndex1, out Pose index) ||
            !hand.GetJointPose(HandJointId.HandPinky1, out Pose pinky)) return false;
        palmPosition = (wrist.position + index.position + pinky.position) / 3f;
        palmNormal = Vector3.Cross(index.position-wrist.position, pinky.position-wrist.position).normalized;
        if (invertPalmNormal) palmNormal = -palmNormal;
        Vector3 toPalm = palmPosition-head.position;
        if (toPalm.sqrMagnitude > maximumPalmDistance*maximumPalmDistance || toPalm.sqrMagnitude < 0.001f) return false;
        if (Vector3.Dot(head.forward, toPalm.normalized) < Mathf.Cos(headLookAngle*Mathf.Deg2Rad) ||
            Vector3.Dot(palmNormal, -toPalm.normalized) < Mathf.Cos(palmFacingAngle*Mathf.Deg2Rad)) return false;
        return Straight(HandJointId.HandIndex1, HandJointId.HandIndex2, HandJointId.HandIndexTip)
            && Straight(HandJointId.HandMiddle1, HandJointId.HandMiddle2, HandJointId.HandMiddleTip)
            && Straight(HandJointId.HandRing1, HandJointId.HandRing2, HandJointId.HandRingTip)
            && Straight(HandJointId.HandPinky1, HandJointId.HandPinky2, HandJointId.HandPinkyTip)
            && Straight(HandJointId.HandThumb1, HandJointId.HandThumb2, HandJointId.HandThumbTip);
    }
    private void Clear()
    {
        InspectingPalm = false; validTime = invalidTime = 0f; hasSample = false; lastManager = null;
        if (display != null) display.HideExternalSoil();
        if (materialView != null) materialView.RequestRGB(this, false);
    }
    private void OnDisable() { Clear(); }
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = Color.cyan; Gizmos.DrawRay(palmPosition, palmNormal * 0.12f);
    }
}
