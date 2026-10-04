using System;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class WristSeedShop : MonoBehaviour
{
    [Serializable]
    public sealed class Offer
    {
        public string label = "Seed";
        [Tooltip("Seed offers: assign the tree pool here. Item offers: leave empty and use Item Pool.")]
        public TreeObjectPool pool;
        public ItemPool itemPool;
        [Min(0)] public int price = 50;
        public Transform anchor;
        public Text priceText;
        [NonSerialized] internal GameObject stock;
        [NonSerialized] internal Grabbable grab;
        [NonSerialized] internal SoilAreaItem itemEffect;
        [NonSerialized] internal Rigidbody body;
        [NonSerialized] internal bool gravity;
        [NonSerialized] internal RigidbodyConstraints constraints;
        [NonSerialized] internal float refillAt;
        [NonSerialized] internal Transform worldParent;
    }

    [SerializeField] private TreeManager treeManager;
    [Tooltip("Assign the LEFT Hand or SyntheticHand component implementing Meta IHand.")]
    [SerializeField] private MonoBehaviour leftHandSource;
    private IHand leftHand;
    [SerializeField] private Transform head;
    [SerializeField] private Vector3 wristOffset;
    [SerializeField] private Vector3 wristEulerOffset;
    [Tooltip("Local direction pointing out of the watch face. Tune to your wrist anchor.")]
    [SerializeField] private Vector3 faceNormal = Vector3.up;
    [SerializeField, Range(1f, 90f)] private float lookAngle = 35f;
    [SerializeField, Range(1f, 90f)] private float faceAngle = 75f;
    [SerializeField, Min(0f)] private float hideDelay = 0.4f;
    [SerializeField, Min(0f)] private float refillDelay = 0.5f;
    [SerializeField, Min(0f)] private float scoreAnimationDuration = 0.4f;
    [SerializeField] private Offer[] offers = { new Offer(), new Offer(), new Offer() };
    [Header("Additional item offers")]
    [SerializeField] private Offer[] itemOffers = {
        new Offer { label = "Raincloud" }, new Offer { label = "Wind" }, new Offer { label = "Sunlight" }
    };
    private Offer[] shopOffers = new Offer[0];
    [Tooltip("Enable to generate a simple non-interactive world-space watch UI and seed anchors.")]
    [SerializeField] private bool buildDefaultUI = true;
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private Text scoreText;
    [SerializeField] private Text timerText;

    private float lastLook = float.NegativeInfinity;
    private float displayedScore, animationStart, animationTarget, animationElapsed;
    [Header("Runtime status (inspect during Play)")]
    [SerializeField] private string shopStatus = "Waiting for initialization";
    private bool visible;
    private GameObject generatedUI;

    private void Start()
    {
        leftHand = leftHandSource as IHand;
        if (treeManager == null || leftHand == null || head == null)
        {
            Debug.LogError("WristSeedShop: assign Tree Manager, LEFT Hand/SyntheticHand source and headset Head.", this);
            enabled = false;
            return;
        }
        shopOffers = new Offer[(offers != null ? offers.Length : 0) + (itemOffers != null ? itemOffers.Length : 0)];
        if (offers != null) Array.Copy(offers, shopOffers, offers.Length);
        if (itemOffers != null) Array.Copy(itemOffers, 0, shopOffers, offers != null ? offers.Length : 0, itemOffers.Length);
        if (buildDefaultUI) BuildUI();
        displayedScore = animationStart = animationTarget = treeManager.Score;
        treeManager.ScoreChanged += AnimateScore;
        SetVisible(false);
        foreach (Offer offer in shopOffers)
        {
            if (offer == null) continue;
            if ((offer.pool == null && offer.itemPool == null) || offer.anchor == null)
                Debug.LogError("WristSeedShop: offer " + offer.label + " needs a TreeObjectPool OR ItemPool and slot anchor. Enable Build Default UI to create anchors.", this);
        }
    }

    private void AnimateScore(int value)
    {
        animationStart = displayedScore;
        animationTarget = value;
        animationElapsed = 0f;
    }

    private void LateUpdate()
    {
        if (treeManager == null || leftHand == null || head == null) return;
        bool wristTracked = leftHand.GetJointPose(HandJointId.HandWristRoot, out Pose wristPose);
        if (wristTracked)
            transform.SetPositionAndRotation(wristPose.position + wristPose.rotation * wristOffset,
                wristPose.rotation * Quaternion.Euler(wristEulerOffset));

        // Purchase first: a held seed must not be hidden with the watch or returned as stock.
        foreach (Offer offer in shopOffers)
        {
            if (offer == null || offer.stock == null || offer.grab == null || offer.grab.GrabPoints.Count == 0) continue;
            if (!treeManager.TrySpendPoints(offer.price))
            {
                ReturnStock(offer);
                offer.refillAt = Time.unscaledTime + refillDelay;
                continue;
            }
            RestoreBody(offer);
            // Purchased trees leave the wrist hierarchy, preserving their world pose.
            offer.stock.transform.SetParent(offer.worldParent, true);
            if (offer.itemEffect != null) offer.itemEffect.BeginPurchased();
            offer.itemEffect = null;
            offer.stock = null;
            offer.grab = null;
            offer.body = null;
            offer.refillAt = Time.unscaledTime + refillDelay;
        }

        Vector3 toWatch = transform.position - head.position;
        Vector3 normal = transform.TransformDirection(faceNormal.normalized);
        if (wristTracked && toWatch.sqrMagnitude > 0.0001f &&
            Vector3.Dot(head.forward, toWatch.normalized) >= Mathf.Cos(lookAngle * Mathf.Deg2Rad) &&
            Vector3.Dot(normal, -toWatch.normalized) >= Mathf.Cos(faceAngle * Mathf.Deg2Rad))
            lastLook = Time.unscaledTime;
        if (!wristTracked) lastLook = float.NegativeInfinity;
        SetVisible(treeManager.IsGameRunning && wristTracked && Time.unscaledTime - lastLook <= hideDelay);

        foreach (Offer offer in shopOffers)
        {
            if (offer == null) continue;
            bool affordable = treeManager.IsGameRunning && treeManager.Score >= offer.price;
            // Unaffordable seeds are absent, so they cannot be picked up for free.
            if (!affordable && offer.stock != null) ReturnStock(offer);
            if (affordable && offer.stock == null && Time.unscaledTime >= offer.refillAt)
                SpawnStock(offer);
            if (offer.stock != null)
            {
                offer.stock.SetActive(visible);
                if (visible && offer.anchor != null)
                {
                    offer.stock.transform.SetPositionAndRotation(offer.anchor.position, offer.anchor.rotation);
                    if (offer.body != null && !offer.body.isKinematic)
                    {
                        offer.body.linearVelocity = Vector3.zero;
                        offer.body.angularVelocity = Vector3.zero;
                    }
                }
            }
            if (offer.priceText != null)
            {
                offer.priceText.text = offer.label + "\n" + offer.price + " pts";
                offer.priceText.color = affordable ? Color.white : Color.gray;
            }
        }

        shopStatus = treeManager.IsGameRunning
            ? "Game running; stock pools independently of watch visibility. Score: " + treeManager.Score
            : "Shop closed. Call TreeManager.StartGame() to stock seeds.";
        animationElapsed += Time.unscaledDeltaTime;
        float t = scoreAnimationDuration <= 0f ? 1f : Mathf.Clamp01(animationElapsed / scoreAnimationDuration);
        // Normalized logistic sigmoid: exact endpoints, smooth S-shaped transition.
        const float edge = 0.01798621f; // sigmoid(-4)
        float ease = t >= 1f ? 1f : (1f / (1f + Mathf.Exp(-8f * (t - 0.5f))) - edge) / (1f - 2f * edge);
        displayedScore = Mathf.Lerp(animationStart, animationTarget, ease);
        if (scoreText != null) scoreText.text = Mathf.RoundToInt(displayedScore) + " pts";
        if (timerText != null) timerText.text = treeManager.TimerText;
    }

    private void SpawnStock(Offer offer)
    {
        if ((offer.pool == null && offer.itemPool == null) || offer.anchor == null) return;
        GameObject item = offer.itemPool != null
            ? offer.itemPool.Get(offer.anchor.position, offer.anchor.rotation, treeManager)
            : treeManager.SpawnTree(offer.pool, offer.anchor.position, offer.anchor.rotation);
        if (item == null)
        {
            Debug.LogError("WristSeedShop: pool could not supply " + offer.label + ". Check its prefab and Console errors.", this);
            offer.refillAt = Time.unscaledTime + Mathf.Max(1f, refillDelay);
            return;
        }
        TreeState state = item.GetComponentInChildren<TreeState>(true);
        SoilAreaItem effect = item.GetComponentInChildren<SoilAreaItem>(true);
        Grabbable grab = item.GetComponentInChildren<Grabbable>(true);
        bool valid = grab != null && (offer.itemPool != null ? effect != null : state != null);
        if (!valid)
        {
            Debug.LogError("WristSeedShop: pool prefab requires Grabbable plus TreeState (seed) or SoilAreaItem (item).", item);
            if (offer.itemPool != null) offer.itemPool.Return(item);
            else treeManager.ReturnTree(item);
            offer.refillAt = float.PositiveInfinity;
            return;
        }
        if (offer.itemPool == null) state.ResetAsSeed();
        offer.itemEffect = offer.itemPool != null ? effect : null;
        offer.stock = item;
        offer.worldParent = item.transform.parent;
        item.transform.SetParent(offer.anchor, true);
        item.transform.localPosition = Vector3.zero;
        item.transform.localRotation = Quaternion.identity;
        offer.grab = grab;
        // Keep the prefab's kinematic setting: Meta must capture the normal release state.
        offer.body = item.GetComponentInChildren<Rigidbody>(true);
        if (offer.body != null)
        {
            offer.gravity = offer.body.useGravity;
            offer.constraints = offer.body.constraints;
            offer.body.useGravity = false;
            offer.body.constraints = RigidbodyConstraints.FreezeAll;
        }
    }

    private static void RestoreBody(Offer offer)
    {
        if (offer.body == null) return;
        offer.body.useGravity = offer.gravity;
        offer.body.constraints = offer.constraints;
    }

    private void ReturnStock(Offer offer)
    {
        RestoreBody(offer);
        if (offer.stock != null)
        {
            if (offer.itemPool != null) offer.itemPool.Return(offer.stock);
            else if (treeManager != null) treeManager.ReturnTree(offer.stock);
        }
        offer.itemEffect = null;
        offer.stock = null;
        offer.grab = null;
        offer.body = null;
    }

    private void SetVisible(bool value)
    {
        visible = value;
        if (visualRoot != null && visualRoot != gameObject) visualRoot.SetActive(value);
    }

    private void OnDisable()
    {
        SetVisible(false);
        foreach (Offer offer in shopOffers) if (offer != null) ReturnStock(offer);
    }

    private void OnDestroy()
    {
        if (treeManager != null) treeManager.ScoreChanged -= AnimateScore;
        if (generatedUI != null) Destroy(generatedUI);
    }

    private void BuildUI()
    {
        generatedUI = new GameObject("Watch UI", typeof(RectTransform), typeof(Canvas));
        generatedUI.transform.SetParent(transform, false);
        generatedUI.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        generatedUI.transform.localScale = Vector3.one * 0.001f;
        generatedUI.GetComponent<RectTransform>().sizeDelta = new Vector2(280f, 250f);
        generatedUI.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        visualRoot = generatedUI;
        scoreText = MakeText("Score", new Vector2(-65f, 95f), new Vector2(130f, 40f), 25);
        timerText = MakeText("Time", new Vector2(75f, 95f), new Vector2(110f, 40f), 25);
        for (int i = 0; i < shopOffers.Length; i++)
        {
            Offer offer = shopOffers[i];
            if (offer == null) continue;
            float x = ((i % 3) - 1) * 85f;
            float z = 35f - (i / 3) * 110f;
            if (offer.anchor == null)
            {
                GameObject anchor = new GameObject((offer.itemPool != null || i >= (offers != null ? offers.Length : 0) ? "Item Slot " : "Seed Slot ") + (i + 1));
                anchor.transform.SetParent(transform, false);
                anchor.transform.localPosition = new Vector3(x * 0.001f, 0.035f, z * 0.001f);
                offer.anchor = anchor.transform;
            }
            offer.priceText = MakeText("Price " + (i + 1), new Vector2(x, z - 40f), new Vector2(85f, 45f), 15);
        }
    }

    private Text MakeText(string label, Vector2 position, Vector2 size, int fontSize)
    {
        GameObject child = new GameObject(label, typeof(RectTransform), typeof(Text));
        child.transform.SetParent(generatedUI.transform, false);
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Text text = child.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }
}
