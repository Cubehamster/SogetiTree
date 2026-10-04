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
    [SerializeField] private Transform displayAnchor;
    [SerializeField] private Vector3 displayWorldOffset = new Vector3(0f, 0.18f, 0f);
    [SerializeField] private SoilMaterialViewController materialView;
    [SerializeField, Min(0.01f)] private float sampleRadius = 0.5f;
    [SerializeField, Min(1f)] private float sampleTicksPerSecond = 10f;
    [SerializeField, Min(0.01f)] private float falloffPower = 2f;
    public enum HandAction { LeftHandDebugView, RightHandSoilDisplay }
    [Header("Hand action")]
    [SerializeField] private HandAction action;
    [SerializeField, Range(-1f, 1f)] private float fingerStraightness = 0.65f;
    [SerializeField, Range(-1f, 1f)] private float curledFingerMaximumDot = 0.35f;
    [SerializeField, Min(0f)] private float pointingActivationDelay = 0.1f;
    public bool IsPointing { get; private set; }
    private IHand hand;
    private float pointingTime, nextSample;
    private Color lastSample;
    private bool hasSample;
    private SoilManager lastManager;

    private void Awake()
    {
        hand = handSource as IHand;
        if (hand == null)
        { Debug.LogError("HandSoilInspector: assign a component implementing Meta IHand.", this); enabled = false; }
    }
    private void LateUpdate()
    {
        if (hand == null || !hand.IsConnected || !hand.IsTrackedDataValid)
        { Clear(); return; }
        bool pointingPose = IsIndexOnlyPointing();
        pointingTime = pointingPose ? pointingTime + Time.deltaTime : 0f;
        IsPointing = pointingPose && pointingTime >= pointingActivationDelay;
        if (materialView != null)
            materialView.RequestRGB(this, action == HandAction.LeftHandDebugView && IsPointing);
        if (action == HandAction.RightHandSoilDisplay && IsPointing)
            UpdatePointer();
        else HidePointerDisplay();
    }

    private void UpdatePointer()
    {
        if (display == null) return;
        var candidate = rayInteractor != null && rayInteractor.isActiveAndEnabled
            ? rayInteractor.CandidateProperties as RayInteractor.RayCandidateProperties : null;
        RayInteractable target = candidate != null ? candidate.ClosestInteractable : null;
        SoilManager manager = target != null ? target.GetComponentInParent<SoilManager>() : null;
        if (manager == null || !manager.isActiveAndEnabled)
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

    private bool Curled(HandJointId root, HandJointId middle, HandJointId tip)
    {
        if (!hand.GetJointPose(root, out Pose a) || !hand.GetJointPose(middle, out Pose b) ||
            !hand.GetJointPose(tip, out Pose c)) return false;
        Vector3 first = b.position-a.position, last = c.position-b.position;
        return first.sqrMagnitude > 1e-8f && last.sqrMagnitude > 1e-8f &&
            Vector3.Dot(first.normalized, last.normalized) <= curledFingerMaximumDot;
    }

    private bool IsIndexOnlyPointing()
    {
        return Straight(HandJointId.HandIndex1, HandJointId.HandIndex2, HandJointId.HandIndexTip)
            && Curled(HandJointId.HandMiddle1, HandJointId.HandMiddle2, HandJointId.HandMiddleTip)
            && Curled(HandJointId.HandRing1, HandJointId.HandRing2, HandJointId.HandRingTip)
            && Curled(HandJointId.HandPinky1, HandJointId.HandPinky2, HandJointId.HandPinkyTip);
    }
    private void HidePointerDisplay()
    {
        hasSample = false; lastManager = null;
        if (display != null) display.HideExternalSoil();
    }
    private void Clear()
    {
        IsPointing = false; pointingTime = 0f; nextSample = 0f;
        HidePointerDisplay();
        if (materialView != null) materialView.RequestRGB(this, false);
    }
    private void OnDisable() { Clear(); }
}
