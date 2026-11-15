using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Runtime bow &amp; arrow:
/// - Left interact (grip/trigger) anywhere summons the bow into the left hand.
/// - Releasing left interact hides and stows the bow again.
/// - Draw with the right arm: grip near the string to nock; pot flexion sets draw + motor.
/// - Motor resistance uses elbow potentiometer only (no mass / IMU torque).
/// </summary>
public class BowAndArrowSetup : MonoBehaviour
{
    [Header("Placement")]
    [Tooltip("Hidden stow pose for the bow when unequipped (no table).")]
    public Vector3 stowPosition = new Vector3(-0.95f, 1.2f, 0.55f);

    [Header("Left-hand summon")]
    [Tooltip("Grip offset in player-facing space: X=right, Y=up, Z=forward (away from face).")]
    public Vector3 leftHandLocalPosition = new Vector3(0f, -0.02f, 0.03f);

    [Header("Draw / motor (potentiometer only)")]
    [Tooltip("Elbow flexion (deg from straight) that maps to full draw / max PWM.")]
    public float flexionAtFullDrawDeg = 80f;

    [Tooltip("How close the right hand must be to the nock (m) to start a draw (fallback if XR select misses).")]
    public float stringGrabRadius = 0.18f;

    [Tooltip("Grip/trigger value (0–1) required to hold the bow (left) or string (right).")]
    [Range(0.1f, 1f)]
    public float gripThreshold = 0.55f;

    [Tooltip("Minimum flexion (deg) before the arrow is considered drawn enough to fire.")]
    public float minFireFlexionDeg = 8f;

    [Header("Arrow flight")]
    public float maxArrowSpeed = 28f;
    public float minArrowSpeed = 6f;
    public float arrowLifetimeSeconds = 8f;
    public float arrowMassKg = 0.05f;
    [Tooltip("Extra meters along aim so the arrow clears the bow on release.")]
    public float launchClearanceMeters = 0.35f;

    [Header("Colors")]
    public Color bowColor = new Color(0.52f, 0.32f, 0.14f);
    public Color bowAccentColor = new Color(0.28f, 0.16f, 0.08f);
    public Color gripWrapColor = new Color(0.12f, 0.1f, 0.08f);
    public Color stringColor = new Color(0.92f, 0.9f, 0.85f);
    public Color arrowColor = new Color(0.72f, 0.5f, 0.22f);
    public Color tipColor = new Color(0.62f, 0.64f, 0.68f);
    public Color fletchingColor = new Color(0.75f, 0.18f, 0.12f);

    ElbowTorqueOutput _torque;
    Transform _leftController;
    Transform _rightController;
    Transform _bowRoot;
    Transform _gripPoint;
    Transform _nockRest;
    Transform _upperTip;
    Transform _lowerTip;
    LineRenderer _stringLine;
    XRSimpleInteractable _stringInteractable;
    Transform _stringHandle;
    Transform _leftHandProxy;
    GameObject _arrow;
    Rigidbody _arrowRb;
    bool _drawing;
    bool _bowHeldByLeft;
    bool _stringHeldByRight;
    float _draw01;
    float _visualDraw01;
    Material _bowMat;
    Material _bowAccentMat;
    Material _gripMat;
    Material _stringMat;
    Material _arrowMat;
    Material _tipMat;
    Material _fletchingMat;
    Vector3 _bowHomePos;
    Quaternion _bowHomeRot;
    bool _bowHomeCaptured;
    bool _bowEquipped;
    Collider _stringHandleCollider;
    Transform _playerCamera;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<BowAndArrowSetup>() != null)
            return;
        var go = new GameObject("BowAndArrow");
        go.AddComponent<BowAndArrowSetup>();
    }

    void Start()
    {
        _torque = ElbowTorqueOutput.Instance;
        BuildBow();
        ResolveControllers();
    }

    void OnDestroy()
    {
        if (_drawing)
            EndDraw(fire: false);
    }

    void Update()
    {
        if (_leftController == null || _rightController == null)
            ResolveControllers();

        UpdateLeftHandProxy();
        UpdateLeftBowSummon();
        UpdateDrawState();
        UpdateStringVisual();
        UpdateNockedArrow();
        UpdateMotorFromPot();
    }

    void LateUpdate()
    {
        // Apply after XR pose updates so orientation isn't overwritten mid-frame.
        if (_bowEquipped)
            ApplyBowHandPose();
    }

    void BuildBow()
    {
        _bowMat = MakeLit(bowColor, metallic: 0.05f, smoothness: 0.35f);
        _bowAccentMat = MakeLit(bowAccentColor, metallic: 0.08f, smoothness: 0.4f);
        _gripMat = MakeLit(gripWrapColor, metallic: 0f, smoothness: 0.15f);
        _stringMat = MakeLit(stringColor, metallic: 0f, smoothness: 0.45f);
        _arrowMat = MakeLit(arrowColor, metallic: 0.05f, smoothness: 0.4f);
        _tipMat = MakeLit(tipColor, metallic: 0.75f, smoothness: 0.7f);
        _fletchingMat = MakeLit(fletchingColor, metallic: 0f, smoothness: 0.25f);

        // No brown bow table — bow is summoned into the hand only.
        _bowRoot = new GameObject("Bow").transform;
        _bowRoot.SetParent(transform, false);
        _bowRoot.position = stowPosition;
        _bowRoot.rotation = Quaternion.Euler(0f, 90f, 0f);

        BuildRealisticBowVisual(_bowRoot);

        _upperTip = new GameObject("UpperTip").transform;
        _upperTip.SetParent(_bowRoot, false);
        _upperTip.localPosition = new Vector3(0f, 0.46f, 0.055f);

        _lowerTip = new GameObject("LowerTip").transform;
        _lowerTip.SetParent(_bowRoot, false);
        _lowerTip.localPosition = new Vector3(0f, -0.46f, 0.055f);

        _nockRest = new GameObject("NockRest").transform;
        _nockRest.SetParent(_bowRoot, false);
        _nockRest.localPosition = new Vector3(0.012f, 0.02f, 0.07f);

        _gripPoint = new GameObject("GripAttach").transform;
        _gripPoint.SetParent(_bowRoot, false);
        _gripPoint.localPosition = new Vector3(0f, -0.02f, 0f);

        var rb = _bowRoot.gameObject.AddComponent<Rigidbody>();
        rb.mass = 0.8f;
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        // No XRGrabInteractable — summon is global via left interact button.

        // Right-hand string grab handle at the nock (only while bow is equipped).
        var handleGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        handleGo.name = "StringHandle";
        _stringHandle = handleGo.transform;
        _stringHandle.SetParent(_nockRest, false);
        _stringHandle.localPosition = Vector3.zero;
        _stringHandle.localScale = Vector3.one * 0.035f;
        ApplyMat(handleGo, _stringMat);
        _stringHandleCollider = handleGo.GetComponent<Collider>();
        if (_stringHandleCollider != null)
            _stringHandleCollider.isTrigger = false;

        var handleRb = handleGo.AddComponent<Rigidbody>();
        handleRb.isKinematic = true;
        handleRb.useGravity = false;

        _stringInteractable = handleGo.AddComponent<XRSimpleInteractable>();
        _stringInteractable.selectMode = InteractableSelectMode.Single;
        _stringInteractable.selectEntered.AddListener(OnStringSelectEntered);
        _stringInteractable.selectExited.AddListener(OnStringSelectExited);

        var stringGo = new GameObject("BowString");
        stringGo.transform.SetParent(_bowRoot, false);
        _stringLine = stringGo.AddComponent<LineRenderer>();
        _stringLine.positionCount = 3;
        _stringLine.widthMultiplier = 0.0045f;
        _stringLine.material = _stringMat != null
            ? _stringMat
            : new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit"));
        if (_stringLine.material != null && _stringLine.material.HasProperty("_BaseColor"))
            _stringLine.material.SetColor("_BaseColor", stringColor);
        _stringLine.startColor = stringColor;
        _stringLine.endColor = stringColor;
        _stringLine.useWorldSpace = true;
        _stringLine.numCapVertices = 4;

        _bowHomePos = _bowRoot.position;
        _bowHomeRot = _bowRoot.rotation;
        _bowHomeCaptured = true;
        // Start stowed: invisible until left interact anywhere.
        SetBowEquipped(false);
        IgnoreBowVsTables();
        StartCoroutine(IgnoreTablesNextFrames());
        Debug.Log("[Bow] LEFT grip/trigger anywhere to spawn bow in hand; release to hide. RIGHT grabs string to draw.");
    }

    /// <summary>Show or hide bow visuals / string interactable.</summary>
    void SetBowEquipped(bool equipped)
    {
        _bowEquipped = equipped;
        _bowHeldByLeft = equipped;

        if (_bowRoot != null)
        {
            foreach (var r in _bowRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r != null)
                    r.enabled = equipped;
            }
        }

        if (_stringLine != null)
        {
            _stringLine.enabled = equipped;
            if (!equipped)
            {
                _stringLine.SetPosition(0, Vector3.zero);
                _stringLine.SetPosition(1, Vector3.zero);
                _stringLine.SetPosition(2, Vector3.zero);
            }
        }

        if (_stringHandleCollider != null)
            _stringHandleCollider.enabled = equipped;

        if (_stringInteractable != null)
            _stringInteractable.enabled = equipped;

        if (equipped)
            UpdateStringVisual();
    }

    void EquipBowToLeftHand()
    {
        if (_bowRoot == null || GetLeftHandAnchor() == null)
            return;

        FreezeBowPhysics();
        // Stay under the setup root; world pose is driven every frame so the bow
        // stays upright and faces the player's forward (not controller roll).
        _bowRoot.SetParent(transform, true);
        SetBowEquipped(true);
        ApplyBowHandPose();
        IgnoreBowVsTables();
        Debug.Log("[Bow] Equipped in left hand.");
    }

    /// <summary>
    /// Place the bow in the left hand, upright, with the string toward the player
    /// so from their view it faces forward (shoots along look direction).
    /// </summary>
    void ApplyBowHandPose()
    {
        Transform hand = GetLeftHandAnchor();
        if (_bowRoot == null || hand == null)
            return;

        Vector3 forward = GetPlayerFlatForward();
        // Bow local +Y = limbs (world up). Local +Z = string/nock side → toward player
        // so aim (through the bow away from the string) matches player forward.
        Quaternion worldRot = Quaternion.LookRotation(-forward, Vector3.up);

        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude < 1e-6f)
            right = Vector3.right;
        else
            right.Normalize();

        Vector3 worldPos = hand.position
            + right * leftHandLocalPosition.x
            + Vector3.up * leftHandLocalPosition.y
            + forward * leftHandLocalPosition.z;

        _bowRoot.SetPositionAndRotation(worldPos, worldRot);

        var rb = _bowRoot.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = worldPos;
            rb.rotation = worldRot;
        }
    }

    Vector3 GetPlayerFlatForward()
    {
        Vector3 f = Vector3.zero;

        // Prefer live head pose (most reliable on Quest).
        if (TryGetDevicePose(XRNode.Head, out _, out Quaternion headRot)
            || TryGetDevicePose(XRNode.CenterEye, out _, out headRot))
        {
            f = headRot * Vector3.forward;
        }

        if (f.sqrMagnitude < 1e-6f)
        {
            Transform cam = GetPlayerCamera();
            if (cam != null)
                f = cam.forward;
        }

        f.y = 0f;
        if (f.sqrMagnitude < 1e-6f)
        {
            Transform hand = GetLeftHandAnchor();
            if (hand != null)
            {
                f = hand.forward;
                f.y = 0f;
            }
        }

        if (f.sqrMagnitude < 1e-6f)
            return Vector3.forward;
        return f.normalized;
    }

    Transform GetPlayerCamera()
    {
        if (_playerCamera != null)
            return _playerCamera;

        var cam = Camera.main;
        if (cam != null)
        {
            _playerCamera = cam.transform;
            return _playerCamera;
        }

        foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (c == null)
                continue;
            if (c.CompareTag("MainCamera") || c.stereoTargetEye != StereoTargetEyeMask.None)
            {
                _playerCamera = c.transform;
                return _playerCamera;
            }
        }

        return null;
    }

    void StowBow()
    {
        if (_drawing)
            EndDraw(fire: false);
        else
            DespawnArrow();

        _stringHeldByRight = false;
        ForceReleaseInteractable(_stringInteractable);

        FreezeBowPhysics();

        if (_stringHandle != null && _nockRest != null)
        {
            _stringHandle.SetParent(_nockRest, false);
            _stringHandle.localPosition = Vector3.zero;
        }

        if (_bowRoot != null && _bowHomeCaptured)
        {
            _bowRoot.SetParent(transform, true);
            _bowRoot.SetPositionAndRotation(_bowHomePos, _bowHomeRot);
        }

        SetBowEquipped(false);
        IgnoreBowVsTables();
    }

    /// <summary>Left grip or trigger held = bow in hand; release = hide. Works anywhere.</summary>
    void UpdateLeftBowSummon()
    {
        bool wantBow = ReadLeftInteractHeld();
        if (wantBow && !_bowEquipped)
            EquipBowToLeftHand();
        else if (!wantBow && _bowEquipped)
        {
            StowBow();
            Debug.Log("[Bow] Released — hidden.");
        }
        else if (_bowEquipped)
        {
            ApplyBowHandPose();
        }
    }

    void UpdateLeftHandProxy()
    {
        if (_leftController != null || _leftHandProxy == null)
            return;
        if (TryGetDevicePose(XRNode.LeftHand, out Vector3 pos, out Quaternion rot))
            _leftHandProxy.SetPositionAndRotation(pos, rot);
    }

    Transform GetLeftHandAnchor()
    {
        if (_leftController != null)
            return _leftController;

        if (_leftHandProxy == null)
        {
            var go = new GameObject("LeftHandBowAnchor");
            _leftHandProxy = go.transform;
            _leftHandProxy.SetParent(transform, false);
        }

        if (TryGetDevicePose(XRNode.LeftHand, out Vector3 pos, out Quaternion rot))
            _leftHandProxy.SetPositionAndRotation(pos, rot);

        return _leftHandProxy;
    }

    bool ReadLeftInteractHeld()
    {
        // Grip (typical XR "select") or trigger ("interact") — either summons/holds the bow.
        if (ReadGrip(XRNode.LeftHand) >= gripThreshold)
            return true;

        var device = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (!device.isValid)
            return false;

        if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigBtn) && trigBtn)
            return true;
        if (device.TryGetFeatureValue(CommonUsages.trigger, out float trig) && trig >= gripThreshold)
            return true;
        if (device.TryGetFeatureValue(CommonUsages.gripButton, out bool gripBtn) && gripBtn)
            return true;

        return false;
    }

    /// <summary>
    /// Recurve-style bow from primitives + a procedural limb tube:
    /// thick wood riser, leather wrap, arrow shelf, curved limbs, tip nocks.
    /// </summary>
    void BuildRealisticBowVisual(Transform root)
    {
        // Main riser body (slightly flared at limb pockets).
        CreateMeshPart("RiserCore", root, BuildRiserMesh(), _bowMat);

        // Leather grip wrap around the handle.
        var grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        grip.name = "GripWrap";
        grip.transform.SetParent(root, false);
        grip.transform.localPosition = new Vector3(0f, -0.02f, 0.005f);
        grip.transform.localScale = new Vector3(0.038f, 0.055f, 0.048f);
        ApplyMat(grip, _gripMat);
        Object.Destroy(grip.GetComponent<Collider>());

        // Arrow shelf / rest on the sight window side.
        var shelf = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shelf.name = "ArrowShelf";
        shelf.transform.SetParent(root, false);
        shelf.transform.localPosition = new Vector3(0.022f, 0.035f, 0.01f);
        shelf.transform.localScale = new Vector3(0.028f, 0.008f, 0.045f);
        ApplyMat(shelf, _bowAccentMat);
        Object.Destroy(shelf.GetComponent<Collider>());

        // Sight window cutout suggestion (dark inset plate).
        var window = GameObject.CreatePrimitive(PrimitiveType.Cube);
        window.name = "SightWindow";
        window.transform.SetParent(root, false);
        window.transform.localPosition = new Vector3(0.018f, 0.07f, 0.0f);
        window.transform.localScale = new Vector3(0.006f, 0.07f, 0.035f);
        ApplyMat(window, _bowAccentMat);
        Object.Destroy(window.GetComponent<Collider>());

        // Stabilizer stub under the grip (modern recurve look).
        var stab = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stab.name = "StabilizerStub";
        stab.transform.SetParent(root, false);
        stab.transform.localPosition = new Vector3(0f, -0.09f, -0.04f);
        stab.transform.localRotation = Quaternion.Euler(75f, 0f, 0f);
        stab.transform.localScale = new Vector3(0.012f, 0.035f, 0.012f);
        ApplyMat(stab, _tipMat);
        Object.Destroy(stab.GetComponent<Collider>());

        // Upper / lower recurve limbs (procedural tapered tubes).
        CreateMeshPart("UpperLimb", root, BuildLimbMesh(upper: true), _bowMat);
        CreateMeshPart("LowerLimb", root, BuildLimbMesh(upper: false), _bowMat);

        // Horn / reinforced tip nocks.
        CreateTipNock("UpperNock", root, new Vector3(0f, 0.46f, 0.055f), upper: true);
        CreateTipNock("LowerNock", root, new Vector3(0f, -0.46f, 0.055f), upper: false);

        // Limb pocket caps where limbs meet the riser.
        CreateLimbPocket("UpperPocket", root, new Vector3(0f, 0.14f, 0.01f), 12f);
        CreateLimbPocket("LowerPocket", root, new Vector3(0f, -0.16f, 0.01f), -12f);
    }

    void CreateTipNock(string name, Transform parent, Vector3 localPos, bool upper)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(upper ? -35f : 35f, 0f, 0f);
        go.transform.localScale = new Vector3(0.014f, 0.018f, 0.014f);
        ApplyMat(go, _bowAccentMat);
        Object.Destroy(go.GetComponent<Collider>());

        // String groove ring.
        var groove = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        groove.name = name + "Groove";
        groove.transform.SetParent(go.transform, false);
        groove.transform.localPosition = new Vector3(0f, upper ? 0.35f : -0.35f, 0.15f);
        groove.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        groove.transform.localScale = new Vector3(0.7f, 0.08f, 0.7f);
        ApplyMat(groove, _stringMat);
        Object.Destroy(groove.GetComponent<Collider>());
    }

    void CreateLimbPocket(string name, Transform parent, Vector3 localPos, float pitchDeg)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(pitchDeg, 0f, 0f);
        go.transform.localScale = new Vector3(0.036f, 0.04f, 0.05f);
        ApplyMat(go, _bowAccentMat);
        Object.Destroy(go.GetComponent<Collider>());
    }

    static void CreateMeshPart(string name, Transform parent, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
    }

    /// <summary>Solid wood riser with a slight side profile and flared limb sockets.</summary>
    static Mesh BuildRiserMesh()
    {
        // Cross-section centers along Y (bottom → top). Z pushes belly toward string (+Z).
        var centers = new[]
        {
            new Vector3(0f, -0.15f, 0.008f),
            new Vector3(0f, -0.10f, 0.006f),
            new Vector3(0f, -0.05f, 0.004f),
            new Vector3(0f, -0.02f, 0.004f),
            new Vector3(0f, 0.02f, 0.006f),
            new Vector3(0f, 0.06f, 0.008f),
            new Vector3(0f, 0.10f, 0.010f),
            new Vector3(0f, 0.14f, 0.012f),
        };
        var radii = new[] { 0.018f, 0.017f, 0.016f, 0.0155f, 0.015f, 0.016f, 0.017f, 0.0185f };
        // Flatten slightly in X so the grip feels board-like, not a round stick.
        return BuildTaperedTube(centers, radii, radialSegments: 10, xScale: 0.75f, zScale: 1.15f);
    }

    /// <summary>Recurve limb: thick at pocket, tapers to tip, curves toward the string then kicks forward.</summary>
    static Mesh BuildLimbMesh(bool upper)
    {
        const int samples = 14;
        var centers = new Vector3[samples];
        var radii = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)(samples - 1);
            // Base near riser → tip.
            float y = Mathf.Lerp(0.14f, 0.46f, t);
            // Classic recurve silhouette in YZ: dips toward string mid-limb, tip kicks out.
            float belly = 0.012f + 0.055f * Mathf.Sin(t * Mathf.PI) - 0.012f * t;
            float tipKick = 0.02f * t * t;
            float z = belly + tipKick;
            // Slight side lean so limbs aren't perfectly flat in one plane.
            float x = 0.004f * Mathf.Sin(t * Mathf.PI);

            centers[i] = new Vector3(x, upper ? y : -y, z);
            // Thick root → thin tip.
            radii[i] = Mathf.Lerp(0.014f, 0.006f, t * t);
        }

        return BuildTaperedTube(centers, radii, radialSegments: 8, xScale: 0.7f, zScale: 1.25f);
    }

    /// <summary>Tube mesh along a polyline with per-sample radius and elliptical cross-section.</summary>
    static Mesh BuildTaperedTube(Vector3[] centers, float[] radii, int radialSegments, float xScale, float zScale)
    {
        int n = centers.Length;
        var verts = new List<Vector3>(n * radialSegments + 2);
        var norms = new List<Vector3>(n * radialSegments + 2);
        var tris = new List<int>((n - 1) * radialSegments * 6 + radialSegments * 6);

        // Cap centers.
        int startCap = 0;
        verts.Add(centers[0]);
        norms.Add((centers[0] - centers[1]).normalized);
        int endCap = 1;
        verts.Add(centers[n - 1]);
        norms.Add((centers[n - 1] - centers[n - 2]).normalized);

        for (int i = 0; i < n; i++)
        {
            Vector3 tangent;
            if (i == 0)
                tangent = (centers[1] - centers[0]).normalized;
            else if (i == n - 1)
                tangent = (centers[n - 1] - centers[n - 2]).normalized;
            else
                tangent = (centers[i + 1] - centers[i - 1]).normalized;

            Vector3 normal = Vector3.Cross(tangent, Vector3.right);
            if (normal.sqrMagnitude < 1e-6f)
                normal = Vector3.Cross(tangent, Vector3.forward);
            normal.Normalize();
            Vector3 binormal = Vector3.Cross(tangent, normal).normalized;

            for (int r = 0; r < radialSegments; r++)
            {
                float ang = (r / (float)radialSegments) * Mathf.PI * 2f;
                Vector3 offset = (Mathf.Cos(ang) * binormal * xScale + Mathf.Sin(ang) * normal * zScale) * radii[i];
                verts.Add(centers[i] + offset);
                norms.Add(offset.normalized);
            }
        }

        int Ring(int i) => 2 + i * radialSegments;

        for (int i = 0; i < n - 1; i++)
        {
            int a = Ring(i);
            int b = Ring(i + 1);
            for (int r = 0; r < radialSegments; r++)
            {
                int r2 = (r + 1) % radialSegments;
                // CCW when viewed from outside (Unity front face).
                tris.Add(a + r);
                tris.Add(a + r2);
                tris.Add(b + r);
                tris.Add(a + r2);
                tris.Add(b + r2);
                tris.Add(b + r);
            }
        }

        // Start / end caps.
        int ring0 = Ring(0);
        for (int r = 0; r < radialSegments; r++)
        {
            int r2 = (r + 1) % radialSegments;
            tris.Add(startCap);
            tris.Add(ring0 + r);
            tris.Add(ring0 + r2);
        }

        int ringN = Ring(n - 1);
        for (int r = 0; r < radialSegments; r++)
        {
            int r2 = (r + 1) % radialSegments;
            tris.Add(endCap);
            tris.Add(ringN + r2);
            tris.Add(ringN + r);
        }

        var mesh = new Mesh { name = "BowTube" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        // Soften any ring-frame normal seams.
        mesh.RecalculateNormals();
        return mesh;
    }

    void OnStringSelectEntered(SelectEnterEventArgs args)
    {
        if (!IsRightInteractor(args.interactorObject))
        {
            StartCoroutine(ForceExitNextFrame(_stringInteractable, args.interactorObject));
            return;
        }

        _stringHeldByRight = true;
        if (_bowHeldByLeft && !_drawing)
            BeginDraw();
    }

    void OnStringSelectExited(SelectExitEventArgs args)
    {
        _stringHeldByRight = false;
        if (_drawing)
            EndDraw(fire: HasEnoughDrawToFire());
    }

    static bool IsRightInteractor(IXRInteractor interactor)
    {
        return InteractorNameContains(interactor, "Right");
    }

    static bool InteractorNameContains(IXRInteractor interactor, string side)
    {
        if (interactor == null)
            return false;
        var t = interactor.transform;
        string n = t.name;
        if (t.parent != null)
            n += " " + t.parent.name;
        if (t.parent != null && t.parent.parent != null)
            n += " " + t.parent.parent.name;
        return n.IndexOf(side, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    System.Collections.IEnumerator ForceExitNextFrame(XRBaseInteractable interactable, IXRSelectInteractor interactor)
    {
        yield return null;
        if (interactable != null && interactor != null && interactable.interactionManager != null
            && interactable.IsSelected(interactor))
        {
            interactable.interactionManager.SelectExit(interactor, interactable);
        }
    }

    System.Collections.IEnumerator IgnoreTablesNextFrames()
    {
        yield return null;
        IgnoreBowVsTables();
        yield return null;
        IgnoreBowVsTables();
    }

    /// <summary>Disable physical contacts between the bow/string and any table colliders.</summary>
    void IgnoreBowVsTables()
    {
        if (_bowRoot == null)
            return;

        var bowCols = _bowRoot.GetComponentsInChildren<Collider>(true);
        foreach (var col in FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (col == null || !IsTableCollider(col))
                continue;
            foreach (var bowCol in bowCols)
            {
                if (bowCol != null)
                    Physics.IgnoreCollision(bowCol, col, true);
            }
        }
    }

    static bool IsTableCollider(Collider col)
    {
        Transform t = col.transform;
        for (int i = 0; i < 6 && t != null; i++)
        {
            string n = t.name;
            if (n.IndexOf("Table", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Plane", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Floor", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Ground", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            t = t.parent;
        }
        return false;
    }

    void FreezeBowPhysics()
    {
        if (_bowRoot == null)
            return;
        var rb = _bowRoot.GetComponent<Rigidbody>();
        if (rb == null)
            return;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    /// <summary>Register the bow transform with the table reset button home list.</summary>
    public void RegisterHomeWithReset(TableResetButton reset)
    {
        if (reset == null || _bowRoot == null)
            return;
        reset.Remember(_bowRoot);
    }

    /// <summary>Cancel draw, despawn arrows, and stow the bow (hidden) on its table.</summary>
    public void ResetToStart()
    {
        ForceReleaseInteractable(_stringInteractable);

        // Destroy any fired arrows still flying.
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t != null && t.name == "Arrow")
                Destroy(t.gameObject);
        }
        _arrow = null;
        _arrowRb = null;

        StowBow();
        Debug.Log("[Bow] Reset — stowed and hidden.");
    }

    static void ForceReleaseInteractable(XRBaseInteractable interactable)
    {
        if (interactable == null || !interactable.isSelected || interactable.interactionManager == null)
            return;
        var selecting = new List<IXRSelectInteractor>(interactable.interactorsSelecting);
        foreach (var interactor in selecting)
        {
            if (interactor != null)
                interactable.interactionManager.SelectExit(interactor, interactable);
        }
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
            ? new[] { "Left Controller", "LeftHand Controller", "LeftHand", "Controller Left", "Left_Controller" }
            : new[] { "Right Controller", "RightHand Controller", "RightHand", "Controller Right", "Right_Controller" };

        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var needle in needles)
        {
            foreach (var t in all)
            {
                if (t.name.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return t;
            }
        }

        // Fallback: XR node pose as a tracked proxy under this system.
        return null;
    }

    void UpdateDrawState()
    {
        if (_bowRoot == null || !_bowEquipped || !_bowHeldByLeft)
        {
            if (_drawing)
                EndDraw(fire: false);
            return;
        }

        // Prefer XR string select; also allow grip+proximity fallback for the right hand.
        if (_stringHeldByRight)
        {
            if (!_drawing)
                BeginDraw();
            return;
        }

        bool rightGrip = ReadGrip(XRNode.RightHand) >= gripThreshold;
        Vector3 rightPos = GetRightHandPosition();
        Vector3 nock = _stringHandle != null ? _stringHandle.position : _nockRest.position;
        float dist = Vector3.Distance(rightPos, nock);
        bool nearString = dist <= stringGrabRadius;

        if (!_drawing && rightGrip && nearString)
        {
            BeginDraw();
        }
        else if (_drawing && !rightGrip && !_stringHeldByRight)
        {
            EndDraw(fire: HasEnoughDrawToFire());
        }
    }

    bool HasEnoughDrawToFire()
    {
        float potFlex = _draw01 * flexionAtFullDrawDeg;
        float visualFlex = _visualDraw01 * flexionAtFullDrawDeg;
        return Mathf.Max(potFlex, visualFlex) >= minFireFlexionDeg || _visualDraw01 >= 0.15f;
    }

    void BeginDraw()
    {
        _drawing = true;
        _draw01 = 0f;
        _visualDraw01 = 0f;
        EnsureArrowNocked();
        Debug.Log("[Bow] Drawing — arrow nocked. Bend elbow for resistance (pot only).");
    }

    void EndDraw(bool fire)
    {
        _drawing = false;
        _torque ??= ElbowTorqueOutput.Instance;
        _torque?.ClearMotorOverride();

        if (fire && _arrow != null)
            FireArrow();
        else
            DespawnArrow();

        _draw01 = 0f;
        _visualDraw01 = 0f;
    }

    void EnsureArrowNocked()
    {
        if (_arrow != null)
            return;

        _arrow = new GameObject("Arrow");
        _arrow.transform.SetParent(null, true);

        // Wood shaft (thin cylinder along +Z).
        var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        shaft.name = "Shaft";
        shaft.transform.SetParent(_arrow.transform, false);
        shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        // Unity cylinder height=2 → scale.y 0.34 → 0.68 m shaft.
        shaft.transform.localScale = new Vector3(0.012f, 0.34f, 0.012f);
        shaft.transform.localPosition = new Vector3(0f, 0f, 0.36f);
        ApplyMat(shaft, _arrowMat);
        Object.Destroy(shaft.GetComponent<Collider>());

        // Field point / pile (cone-ish via stretched capsule + sphere).
        var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        tip.name = "Tip";
        tip.transform.SetParent(_arrow.transform, false);
        tip.transform.localScale = new Vector3(0.018f, 0.018f, 0.032f);
        tip.transform.localPosition = new Vector3(0f, 0f, 0.72f);
        ApplyMat(tip, _tipMat);
        Object.Destroy(tip.GetComponent<Collider>());

        var tipCollar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tipCollar.name = "TipCollar";
        tipCollar.transform.SetParent(_arrow.transform, false);
        tipCollar.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        tipCollar.transform.localScale = new Vector3(0.014f, 0.012f, 0.014f);
        tipCollar.transform.localPosition = new Vector3(0f, 0f, 0.695f);
        ApplyMat(tipCollar, _tipMat);
        Object.Destroy(tipCollar.GetComponent<Collider>());

        // Nock at the rear (string groove).
        var nock = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nock.name = "Nock";
        nock.transform.SetParent(_arrow.transform, false);
        nock.transform.localScale = new Vector3(0.014f, 0.014f, 0.02f);
        nock.transform.localPosition = new Vector3(0f, 0f, 0.01f);
        ApplyMat(nock, _arrowMat);
        Object.Destroy(nock.GetComponent<Collider>());

        var nockGroove = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        nockGroove.name = "NockGroove";
        nockGroove.transform.SetParent(_arrow.transform, false);
        nockGroove.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        nockGroove.transform.localScale = new Vector3(0.008f, 0.01f, 0.008f);
        nockGroove.transform.localPosition = new Vector3(0f, 0f, 0.002f);
        ApplyMat(nockGroove, _stringMat);
        Object.Destroy(nockGroove.GetComponent<Collider>());

        // Three vanes (cock feather + two hens), slightly staggered like real fletching.
        for (int i = 0; i < 3; i++)
        {
            float yaw = i * 120f + (i == 0 ? 0f : 5f);
            var vane = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vane.name = i == 0 ? "CockFeather" : $"HenFeather_{i}";
            vane.transform.SetParent(_arrow.transform, false);
            vane.transform.localRotation = Quaternion.Euler(8f, yaw, 0f);
            vane.transform.localScale = new Vector3(0.0025f, 0.045f, 0.07f);
            vane.transform.localPosition = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0.018f, 0.08f);
            ApplyMat(vane, i == 0 ? _fletchingMat : MakeLit(new Color(0.85f, 0.85f, 0.82f), metallic: 0f, smoothness: 0.2f));
            Object.Destroy(vane.GetComponent<Collider>());
        }

        var col = _arrow.AddComponent<CapsuleCollider>();
        col.direction = 2; // Z
        col.radius = 0.015f;
        col.height = 0.74f;
        col.center = new Vector3(0f, 0f, 0.36f);

        _arrowRb = _arrow.AddComponent<Rigidbody>();
        _arrowRb.mass = arrowMassKg;
        _arrowRb.isKinematic = true;
        _arrowRb.useGravity = false;
        _arrowRb.interpolation = RigidbodyInterpolation.Interpolate;
        _arrowRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var trail = _arrow.AddComponent<TrailRenderer>();
        trail.time = 0.35f;
        trail.minVertexDistance = 0.02f;
        trail.widthMultiplier = 0.018f;
        trail.emitting = false;
        var trailMat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit"));
        trailMat.color = new Color(1f, 0.85f, 0.35f, 0.7f);
        trail.material = trailMat;
        trail.startColor = new Color(1f, 0.9f, 0.4f, 0.75f);
        trail.endColor = new Color(1f, 0.9f, 0.4f, 0f);
    }

    void UpdateNockedArrow()
    {
        if (_arrow == null || !_drawing)
            return;

        Vector3 aimFwd = GetAimForward();
        if (aimFwd.sqrMagnitude < 1e-6f)
            aimFwd = Vector3.forward;
        aimFwd.Normalize();

        // Nock at the pulled string; shaft/tip point along aim.
        Vector3 nock = GetStringPullPoint();
        _arrow.transform.SetParent(null, true);
        _arrow.transform.SetPositionAndRotation(nock, Quaternion.LookRotation(aimFwd, Vector3.up));

        float pull = Vector3.Distance(nock, _nockRest.position);
        _visualDraw01 = Mathf.Clamp01(pull / 0.45f);
    }

    void UpdateStringVisual()
    {
        if (_stringLine == null || _upperTip == null || _lowerTip == null || _nockRest == null)
            return;

        if (!_bowEquipped)
        {
            _stringLine.enabled = false;
            return;
        }

        _stringLine.enabled = true;
        Vector3 mid = _drawing ? GetStringPullPoint() : _nockRest.position;
        _stringLine.SetPosition(0, _upperTip.position);
        _stringLine.SetPosition(1, mid);
        _stringLine.SetPosition(2, _lowerTip.position);

        // Keep the right-hand grab handle on the pulled string so selection stays valid.
        if (_stringHandle != null)
        {
            if (_drawing)
            {
                _stringHandle.SetParent(null, true);
                _stringHandle.position = mid;
            }
            else if (_stringHandle.parent != _nockRest)
            {
                _stringHandle.SetParent(_nockRest, false);
                _stringHandle.localPosition = Vector3.zero;
            }
        }
    }

    Vector3 GetStringPullPoint()
    {
        // Keep the string on the line from the bow rest back toward the right hand.
        Vector3 right = GetRightHandPosition();
        Vector3 rest = _nockRest.position;
        Vector3 toHand = right - rest;
        float maxPull = 0.45f;
        if (toHand.sqrMagnitude < 1e-6f)
            return rest;

        // Only allow pull "behind" the rest (away from where the arrow will fly).
        // Front = rest; rear = right hand → flight is rest - rear direction... 
        // String should sit at the right hand, clamped in distance from rest.
        if (toHand.magnitude > maxPull)
            return rest + toHand.normalized * maxPull;
        return right;
    }

    void UpdateMotorFromPot()
    {
        _torque ??= ElbowTorqueOutput.Instance;
        if (_torque == null)
            return;

        if (!_drawing)
        {
            if (_torque.HasMotorOverride)
                _torque.ClearMotorOverride();
            return;
        }

        float flexion = _torque.ElbowFlexionDeg;
        if (float.IsNaN(flexion))
        {
            _draw01 = 0f;
            _torque.SetMotorOverride(0f, "Bow draw");
            return;
        }

        float span = Mathf.Max(1f, flexionAtFullDrawDeg);
        _draw01 = Mathf.Clamp01(flexion / span);
        float maxPwm = Mathf.Clamp(_torque.maxMotorOutput, 0f, 255f);
        float pwm = _draw01 * maxPwm;
        _torque.SetMotorOverride(pwm, "Bow draw");
    }

    void FireArrow()
    {
        if (_arrow == null)
            return;

        // Power from pot and/or how far the string was pulled — never launch at 0.
        float power01 = Mathf.Max(_draw01, _visualDraw01, 0.35f);
        power01 = Mathf.Clamp01(power01);
        float speed = Mathf.Lerp(minArrowSpeed, maxArrowSpeed, power01);

        Vector3 aim = GetAimForward();
        if (aim.sqrMagnitude < 1e-6f)
            aim = _bowRoot != null ? _bowRoot.forward : Vector3.forward;
        aim.Normalize();

        // Spawn clear of the bow so the first frames are visibly in flight.
        Vector3 nock = _nockRest != null ? _nockRest.position : _arrow.transform.position;
        Vector3 spawn = nock + aim * launchClearanceMeters;
        _arrow.transform.SetParent(null, true);
        _arrow.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(aim, Vector3.up));

        IgnoreBowCollision(_arrow, true);
        IgnoreArrowVsTables(_arrow);

        var trail = _arrow.GetComponent<TrailRenderer>();
        if (trail != null)
        {
            trail.Clear();
            trail.emitting = true;
        }

        _arrowRb.isKinematic = false;
        _arrowRb.useGravity = true;
        _arrowRb.linearVelocity = aim * speed;
        _arrowRb.angularVelocity = Vector3.zero;
        // Keep tip pointed along velocity.
        _arrowRb.freezeRotation = true;

        // Detach ownership so Update can't touch this instance; it flies on its own.
        var flying = _arrow;
        _arrow = null;
        _arrowRb = null;
        _visualDraw01 = 0f;

        Object.Destroy(flying, arrowLifetimeSeconds);
        Debug.Log($"[Bow] Arrow flying — speed={speed:F1} m/s power={power01:P0} dir={aim}");
    }

    void IgnoreArrowVsTables(GameObject arrow)
    {
        if (arrow == null)
            return;
        var arrowCols = arrow.GetComponentsInChildren<Collider>(true);
        foreach (var col in FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (col == null || !IsTableCollider(col))
                continue;
            // Still collide with floor eventually for landing — only skip *Table* names, not Plane.
            Transform t = col.transform;
            bool isTable = false;
            for (int i = 0; i < 4 && t != null; i++)
            {
                if (t.name.IndexOf("Table", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isTable = true;
                    break;
                }
                t = t.parent;
            }
            if (!isTable)
                continue;
            foreach (var a in arrowCols)
            {
                if (a != null)
                    Physics.IgnoreCollision(a, col, true);
            }
        }
    }

    void DespawnArrow()
    {
        if (_arrow == null)
            return;
        Object.Destroy(_arrow);
        _arrow = null;
        _arrowRb = null;
    }

    void IgnoreBowCollision(GameObject arrow, bool ignore)
    {
        if (_bowRoot == null || arrow == null)
            return;
        var arrowCols = arrow.GetComponentsInChildren<Collider>();
        var bowCols = _bowRoot.GetComponentsInChildren<Collider>();
        foreach (var a in arrowCols)
        {
            foreach (var b in bowCols)
            {
                if (a != null && b != null)
                    Physics.IgnoreCollision(a, b, ignore);
            }
        }
    }

    /// <summary>
    /// Shot direction: from the right (string) hand through the left (bow) hand.
    /// That is always "forward" along the draw — controller.forward / bow.forward
    /// often point sideways when gripping.
    /// </summary>
    Vector3 GetAimForward()
    {
        Vector3 rear = GetRightHandPosition();
        Vector3 front = GetLeftHandPosition();

        // Prefer bow grip / nock as the front point when available.
        if (_nockRest != null)
            front = _nockRest.position;
        else if (_bowRoot != null)
            front = _bowRoot.position;

        Vector3 through = front - rear;
        if (through.sqrMagnitude > 0.0025f) // > 5 cm
            return through.normalized;

        // Hands nearly overlapping — fall back to bow local +Z.
        if (_bowRoot != null && _bowRoot.forward.sqrMagnitude > 1e-6f)
            return _bowRoot.forward.normalized;

        return Vector3.forward;
    }

    Vector3 GetLeftHandPosition()
    {
        if (_leftController != null)
            return _leftController.position;

        if (TryGetDevicePose(XRNode.LeftHand, out Vector3 pos, out _))
            return pos;

        return _bowRoot != null ? _bowRoot.position : transform.position;
    }

    Vector3 GetRightHandPosition()
    {
        if (_rightController != null)
            return _rightController.position;

        if (TryGetDevicePose(XRNode.RightHand, out Vector3 pos, out _))
            return pos;

        return _nockRest != null ? _nockRest.position : transform.position;
    }

    static float ReadGrip(XRNode node)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
            return 0f;

        if (device.TryGetFeatureValue(CommonUsages.grip, out float grip))
            return grip;

        if (device.TryGetFeatureValue(CommonUsages.gripButton, out bool pressed))
            return pressed ? 1f : 0f;

        return 0f;
    }

    static bool TryGetDevicePose(XRNode node, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
            return false;
        bool okPos = device.TryGetFeatureValue(CommonUsages.devicePosition, out pos);
        bool okRot = device.TryGetFeatureValue(CommonUsages.deviceRotation, out rot);
        return okPos && okRot;
    }

    static Material MakeLit(Color color, float metallic = 0f, float smoothness = 0.4f)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("HDRP/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", color);
        if (mat.HasProperty("_Metallic"))
            mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness"))
            mat.SetFloat("_Glossiness", smoothness);
        return mat;
    }

    static void ApplyMat(GameObject go, Material mat)
    {
        var r = go.GetComponent<MeshRenderer>();
        if (r != null && mat != null)
            r.sharedMaterial = mat;
    }
}
