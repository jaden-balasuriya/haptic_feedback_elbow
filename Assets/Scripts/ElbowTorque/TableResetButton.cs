using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Red "Reset" plunger on the main dumbbell table. Press down with a controller/hand
/// to return dumbbells, archery targets, and the bow to their starting poses.
/// </summary>
public class TableResetButton : MonoBehaviour
{
    [Header("Placement (world)")]
    [Tooltip("Center of the button resting on the main table top.")]
    public Vector3 buttonCenter = new Vector3(0.48f, 1.175f, 0.38f);

    [Tooltip("X/Z = diameter, Y = height of the circular plunger.")]
    public Vector3 buttonSize = new Vector3(0.14f, 0.05f, 0.14f);

    [Header("Press")]
    [Tooltip("How far the cap travels down when pressed (m).")]
    public float pressDepth = 0.02f;

    [Tooltip("Fraction of pressDepth that fires a reset.")]
    [Range(0.3f, 1f)]
    public float activateFraction = 0.7f;

    [Tooltip("Cooldown after a reset before another press counts.")]
    public float cooldownSeconds = 0.75f;

    public Color buttonColor = new Color(0.85f, 0.08f, 0.08f);
    public Color labelColor = Color.white;

    Transform _cap;
    Vector3 _capRestLocal;
    Vector3 _capPressedLocal;
    int _overlapHands;
    bool _firedThisPress;
    float _cooldownUntil;
    Material _buttonMat;
    Transform _leftController;
    Transform _rightController;
    float _controllerResolveTimer;

    struct HomePose
    {
        public Transform transform;
        public Vector3 position;
        public Quaternion rotation;
        public Rigidbody rb;
        public bool wasKinematic;
    }

    readonly List<HomePose> _homes = new List<HomePose>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<TableResetButton>() != null)
            return;
        var go = new GameObject("TableResetButton");
        go.AddComponent<TableResetButton>();
    }

    void Start()
    {
        BuildButton();
        // Wait a frame so targets/bow finish spawning, then snapshot homes.
        StartCoroutine(CaptureHomesNextFrame());
    }

    System.Collections.IEnumerator CaptureHomesNextFrame()
    {
        yield return null;
        yield return null;
        CaptureHomes();
    }

    void BuildButton()
    {
        _buttonMat = MakeLit(buttonColor);

        var root = new GameObject("ResetPlunger");
        root.transform.SetParent(transform, false);
        root.transform.position = buttonCenter;

        float diameter = Mathf.Max(buttonSize.x, buttonSize.z);
        float height = buttonSize.y;
        // Unity cylinder primitive: height 2, radius 0.5 → scale (d, h/2, d).
        Vector3 cylScale = new Vector3(diameter, height * 0.5f, diameter);
        Vector3 baseScale = new Vector3(diameter * 1.08f, height * 0.18f, diameter * 1.08f);

        // Fixed circular base (slightly darker red).
        var baseGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseGo.name = "Base";
        baseGo.transform.SetParent(root.transform, false);
        baseGo.transform.localPosition = new Vector3(0f, -height * 0.35f, 0f);
        baseGo.transform.localScale = baseScale;
        ApplyMat(baseGo, MakeLit(new Color(0.55f, 0.05f, 0.05f)));
        var baseRb = baseGo.AddComponent<Rigidbody>();
        baseRb.isKinematic = true;
        baseRb.useGravity = false;

        // Movable red circular cap.
        var capGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        capGo.name = "Cap";
        capGo.transform.SetParent(root.transform, false);
        capGo.transform.localPosition = Vector3.zero;
        capGo.transform.localScale = cylScale;
        ApplyMat(capGo, _buttonMat);
        Object.Destroy(capGo.GetComponent<Collider>());

        var trigger = capGo.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 0.55f;

        var sensor = capGo.AddComponent<ResetButtonSensor>();
        sensor.Owner = this;

        _cap = capGo.transform;
        _capRestLocal = Vector3.zero;
        _capPressedLocal = new Vector3(0f, -pressDepth, 0f);

        // White "Reset" label on top (rotated 180° so it faces the player).
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(_cap, false);
        labelGo.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        labelGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        labelGo.transform.localScale = Vector3.one;

        var text = labelGo.AddComponent<TextMesh>();
        text.text = "Reset";
        text.fontSize = 64;
        text.characterSize = 0.012f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = labelColor;
        text.fontStyle = FontStyle.Bold;
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font != null)
            text.font = font;

        Debug.Log("[Reset] Red Reset button placed on the table.");
    }

    void CaptureHomes()
    {
        _homes.Clear();

        foreach (var box in FindObjectsByType<BoxMass>(FindObjectsSortMode.None))
            Remember(box.transform);

        // Bow root if present.
        var bow = FindFirstObjectByType<BowAndArrowSetup>();
        if (bow != null)
            bow.RegisterHomeWithReset(this);

        Debug.Log($"[Reset] Captured {_homes.Count} home pose(s).");
    }

    public void Remember(Transform t)
    {
        if (t == null)
            return;
        for (int i = 0; i < _homes.Count; i++)
        {
            if (_homes[i].transform == t)
                return;
        }

        var rb = t.GetComponent<Rigidbody>();
        _homes.Add(new HomePose
        {
            transform = t,
            position = t.position,
            rotation = t.rotation,
            rb = rb,
            wasKinematic = rb != null && rb.isKinematic,
        });
    }

    void Update()
    {
        if (_cap == null)
            return;

        // Also treat controller poses near the cap as a press (Quest controllers
        // often have no physics collider on the tip).
        bool proximityPress = ControllerPressingCap();
        bool pressing = _overlapHands > 0 || proximityPress;

        Vector3 target = pressing ? _capPressedLocal : _capRestLocal;
        _cap.localPosition = Vector3.Lerp(_cap.localPosition, target, 1f - Mathf.Exp(-20f * Time.deltaTime));

        float depth = _capRestLocal.y - _cap.localPosition.y;
        bool activated = depth >= pressDepth * activateFraction;

        if (!pressing)
        {
            _firedThisPress = false;
            return;
        }

        if (activated && !_firedThisPress && Time.time >= _cooldownUntil)
        {
            _firedThisPress = true;
            _cooldownUntil = Time.time + cooldownSeconds;
            PerformReset();
        }
    }

    bool ControllerPressingCap()
    {
        Vector3 capPos = _cap.position;
        float radius = Mathf.Max(buttonSize.x, buttonSize.z) * 0.55f;
        float topY = buttonCenter.y + buttonSize.y * 0.5f;
        float bottomY = topY - pressDepth - 0.04f;

        _controllerResolveTimer -= Time.deltaTime;
        if (_controllerResolveTimer <= 0f
            || _leftController == null
            || _rightController == null)
        {
            _controllerResolveTimer = 1f;
            ResolveControllers();
        }

        if (_leftController != null
            && PointPressing(_leftController.position, capPos, radius, topY, bottomY))
            return true;
        if (_rightController != null
            && PointPressing(_rightController.position, capPos, radius, topY, bottomY))
            return true;

        return HandNearCap(XRNode.LeftHand, capPos, radius, topY, bottomY)
            || HandNearCap(XRNode.RightHand, capPos, radius, topY, bottomY);
    }

    void ResolveControllers()
    {
        if (_leftController == null)
            _leftController = FindControllerTransform(left: true);
        if (_rightController == null)
            _rightController = FindControllerTransform(left: false);
    }

    static Transform FindControllerTransform(bool left)
    {
        string[] needles = left
            ? new[] { "Left Controller", "LeftHand Controller", "Controller Left" }
            : new[] { "Right Controller", "RightHand Controller", "Controller Right" };

        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var needle in needles)
        {
            foreach (var t in all)
            {
                if (t.name.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return t;
            }
        }
        return null;
    }

    static bool HandNearCap(XRNode node, Vector3 capPos, float radius, float topY, float bottomY)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid || !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos))
            return false;
        return PointPressing(pos, capPos, radius, topY, bottomY);
    }

    static bool PointPressing(Vector3 pos, Vector3 capPos, float radius, float topY, float bottomY)
    {
        float dx = pos.x - capPos.x;
        float dz = pos.z - capPos.z;
        if (dx * dx + dz * dz > radius * radius)
            return false;
        return pos.y <= topY + 0.03f && pos.y >= bottomY;
    }

    internal void NotifyHandEnter(Collider other)
    {
        if (!IsHandOrController(other))
            return;
        _overlapHands++;
    }

    internal void NotifyHandExit(Collider other)
    {
        if (!IsHandOrController(other))
            return;
        _overlapHands = Mathf.Max(0, _overlapHands - 1);
    }

    static bool IsHandOrController(Collider other)
    {
        if (other == null)
            return false;

        // Controllers / interactors often have colliders or child colliders.
        Transform t = other.transform;
        for (int i = 0; i < 6 && t != null; i++)
        {
            string n = t.name;
            if (n.IndexOf("Controller", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Hand", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Interactor", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Poke", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            t = t.parent;
        }

        // Direct XR grab: something currently held that isn't a table prop — treat any
        // moving kinematic interactor proxy as a hand if it has XRBaseInteractor.
        if (other.GetComponentInParent<XRBaseInteractor>() != null)
            return true;

        return false;
    }

    void PerformReset()
    {
        Debug.Log("[Reset] Button pressed — restoring table objects.");

        ForceReleaseAllGrabs();

        ElbowTorqueOutput.Instance?.ClearHeld();
        ElbowTorqueOutput.Instance?.ClearMotorOverride();

        // Dumbbells / remembered poses.
        foreach (var home in _homes)
        {
            if (home.transform == null)
                continue;
            ForceReleaseGrabOn(home.transform.gameObject);
            if (home.rb != null)
            {
                home.rb.linearVelocity = Vector3.zero;
                home.rb.angularVelocity = Vector3.zero;
                home.rb.isKinematic = true;
            }
            home.transform.SetPositionAndRotation(home.position, home.rotation);
            if (home.rb != null)
                home.rb.isKinematic = home.wasKinematic;
        }

        var targets = FindFirstObjectByType<ArcheryTargetSetup>();
        targets?.ResetTargets();

        var bow = FindFirstObjectByType<BowAndArrowSetup>();
        bow?.ResetToStart();

        // Clean up any loose fired arrows still in the scene.
        foreach (var go in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (go != null && go.name == "Arrow" && go.parent == null)
                Destroy(go.gameObject);
        }
    }

    static void ForceReleaseAllGrabs()
    {
        var grabs = FindObjectsByType<XRGrabInteractable>(FindObjectsSortMode.None);
        foreach (var grab in grabs)
            ForceReleaseGrab(grab);
    }

    static void ForceReleaseGrabOn(GameObject go)
    {
        if (go == null)
            return;
        var grab = go.GetComponent<XRGrabInteractable>();
        if (grab != null)
            ForceReleaseGrab(grab);
    }

    static void ForceReleaseGrab(XRGrabInteractable grab)
    {
        if (grab == null || !grab.isSelected || grab.interactionManager == null)
            return;

        // Copy because SelectExit mutates the selecting list.
        var selecting = new List<IXRSelectInteractor>(grab.interactorsSelecting);
        foreach (var interactor in selecting)
        {
            if (interactor != null)
                grab.interactionManager.SelectExit(interactor, grab);
        }
    }

    static Material MakeLit(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("HDRP/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", color);
        return mat;
    }

    static void ApplyMat(GameObject go, Material mat)
    {
        var r = go.GetComponent<MeshRenderer>();
        if (r != null && mat != null)
            r.sharedMaterial = mat;
    }
}

/// <summary>Trigger relay on the red button cap.</summary>
public class ResetButtonSensor : MonoBehaviour
{
    public TableResetButton Owner;

    void OnTriggerEnter(Collider other) => Owner?.NotifyHandEnter(other);

    void OnTriggerExit(Collider other) => Owner?.NotifyHandExit(other);
}
