using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Spawns a 4-disc Tower of Hanoi on a table to the player's right.
/// Red discs, blue rods. Discs snap to pegs; only a smaller disc may stack on a larger one.
/// </summary>
public class TowerOfHanoiSetup : MonoBehaviour
{
    [Header("Placement (world)")]
    [Tooltip("Center of the Hanoi table — default is to the right of the main dumbbell table.")]
    public Vector3 tableCenter = new Vector3(1.05f, 0.575f, 0.55f);

    public Vector3 tableScale = new Vector3(0.85f, 1.15f, 0.5f);

    [Header("Colors")]
    public Color rodColor = new Color(0.15f, 0.35f, 0.95f);
    public Color discColor = new Color(0.9f, 0.12f, 0.12f);
    public Color tableColor = new Color(0.45f, 0.32f, 0.2f);

    [Header("Layout")]
    public int discCount = 4;
    public float pegSpacing = 0.22f;
    public float pegHeight = 0.28f;
    public float pegRadius = 0.012f;
    public float discHeight = 0.03f;
    public float discRadiusStep = 0.025f;
    public float discInnerClearance = 0.008f;

    readonly List<Stack<HanoiDisc>> _pegs = new List<Stack<HanoiDisc>>();
    readonly List<Transform> _pegRoots = new List<Transform>();
    readonly List<HanoiDisc> _discs = new List<HanoiDisc>();
    Material _rodMat;
    Material _discMat;
    Material _tableMat;

    // Disabled — replaced by archery targets (ArcheryTargetSetup).
    // [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<TowerOfHanoiSetup>() != null)
            return;
        var go = new GameObject("TowerOfHanoi");
        go.AddComponent<TowerOfHanoiSetup>();
    }

    void Start()
    {
        Build();
    }

    void Build()
    {
        _rodMat = MakeLit(rodColor);
        _discMat = MakeLit(discColor);
        _tableMat = MakeLit(tableColor);

        float tableTopY = tableCenter.y + tableScale.y * 0.5f;

        var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.name = "HanoiTable";
        table.transform.SetParent(transform, false);
        table.transform.position = tableCenter;
        table.transform.localScale = tableScale;
        ApplyMat(table, _tableMat);
        var tableRb = table.AddComponent<Rigidbody>();
        tableRb.isKinematic = true;
        tableRb.useGravity = false;

        _pegs.Clear();
        _pegRoots.Clear();

        for (int i = 0; i < 3; i++)
        {
            float x = tableCenter.x + (i - 1) * pegSpacing;
            var pegRoot = new GameObject($"Peg_{i}").transform;
            pegRoot.SetParent(transform, false);
            pegRoot.position = new Vector3(x, tableTopY, tableCenter.z);
            _pegRoots.Add(pegRoot);
            _pegs.Add(new Stack<HanoiDisc>());

            var rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rod.name = "Rod";
            rod.transform.SetParent(pegRoot, false);
            rod.transform.localPosition = new Vector3(0f, pegHeight * 0.5f, 0f);
            rod.transform.localScale = new Vector3(pegRadius * 2f, pegHeight * 0.5f, pegRadius * 2f);
            ApplyMat(rod, _rodMat);
            var rodRb = rod.AddComponent<Rigidbody>();
            rodRb.isKinematic = true;
            rodRb.useGravity = false;
            // Soften collisions with discs — discs use trigger snap, rod stays solid.
            var rodCol = rod.GetComponent<Collider>();
            if (rodCol != null)
                rodCol.isTrigger = false;
        }

        // Largest disc = sizeIndex discCount-1 at bottom of peg 0.
        for (int size = discCount - 1; size >= 0; size--)
            SpawnDisc(size, pegIndex: 0);
    }

    void SpawnDisc(int sizeIndex, int pegIndex)
    {
        float radius = pegRadius + discInnerClearance + (sizeIndex + 1) * discRadiusStep;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = $"Disc_{sizeIndex}";
        go.transform.SetParent(transform, false);
        go.transform.localScale = new Vector3(radius * 2f, discHeight * 0.5f, radius * 2f);
        ApplyMat(go, _discMat);

        // Punch a visual "ring" feel: slightly darker inner is enough; keep solid for grab.
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.3f + sizeIndex * 0.15f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        var grab = go.AddComponent<XRGrabInteractable>();
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        grab.throwOnDetach = false;

        var disc = go.AddComponent<HanoiDisc>();
        disc.Init(this, sizeIndex);
        _discs.Add(disc);
        PlaceOnPeg(disc, pegIndex, animate: false);

        grab.selectEntered.AddListener(_ => disc.OnPickedUp());
        grab.selectExited.AddListener(_ => disc.OnReleased());
    }

    /// <summary>Return every disc to the starting stack on peg 0 (largest at bottom).</summary>
    public void ResetPuzzle()
    {
        foreach (var disc in _discs)
        {
            if (disc == null)
                continue;
            var grab = disc.GetComponent<XRGrabInteractable>();
            if (grab != null && grab.isSelected && grab.interactionManager != null)
            {
                var selecting = new List<IXRSelectInteractor>(grab.interactorsSelecting);
                foreach (var interactor in selecting)
                {
                    if (interactor != null)
                        grab.interactionManager.SelectExit(interactor, grab);
                }
            }
        }

        for (int i = 0; i < _pegs.Count; i++)
            _pegs[i].Clear();

        foreach (var disc in _discs)
        {
            if (disc == null)
                continue;
            disc.PegIndex = -1;
        }

        // Largest first (same as initial spawn order).
        var ordered = new List<HanoiDisc>(_discs);
        ordered.Sort((a, b) => b.SizeIndex.CompareTo(a.SizeIndex));
        foreach (var disc in ordered)
            PlaceOnPeg(disc, pegIndex: 0, animate: false);

        Debug.Log("[Hanoi] Reset to starting stack.");
    }

    public bool CanPlace(HanoiDisc disc, int pegIndex)
    {
        if (pegIndex < 0 || pegIndex >= _pegs.Count)
            return false;
        var stack = _pegs[pegIndex];
        if (stack.Count == 0)
            return true;
        return stack.Peek().SizeIndex > disc.SizeIndex;
    }

    public int FindNearestLegalPeg(HanoiDisc disc, Vector3 worldPos)
    {
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < _pegRoots.Count; i++)
        {
            if (!CanPlace(disc, i))
                continue;
            float d = HorizontalDistSq(worldPos, _pegRoots[i].position);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        // Only snap if reasonably close to a peg.
        const float maxSnapDist = 0.28f;
        if (best >= 0 && bestDist <= maxSnapDist * maxSnapDist)
            return best;
        return -1;
    }

    public void DetachFromPeg(HanoiDisc disc)
    {
        if (disc.PegIndex < 0)
            return;
        var stack = _pegs[disc.PegIndex];
        if (stack.Count > 0 && stack.Peek() == disc)
            stack.Pop();
        disc.PegIndex = -1;
    }

    public bool IsTopDisc(HanoiDisc disc)
    {
        if (disc.PegIndex < 0 || disc.PegIndex >= _pegs.Count)
            return false;
        var stack = _pegs[disc.PegIndex];
        return stack.Count > 0 && stack.Peek() == disc;
    }

    public void PlaceOnPeg(HanoiDisc disc, int pegIndex, bool animate)
    {
        if (disc.PegIndex >= 0)
            DetachFromPeg(disc);

        var stack = _pegs[pegIndex];
        int level = stack.Count;
        stack.Push(disc);
        disc.PegIndex = pegIndex;

        Vector3 pegPos = _pegRoots[pegIndex].position;
        float y = pegPos.y + discHeight * 0.5f + level * discHeight;
        Vector3 target = new Vector3(pegPos.x, y, pegPos.z);

        var rb = disc.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        disc.transform.SetPositionAndRotation(target, Quaternion.identity);
        // Stay kinematic on the peg until grabbed so stacks don't jitter.
        if (rb != null)
            rb.isKinematic = true;
    }

    public void ReturnToPeg(HanoiDisc disc)
    {
        if (disc.HomePegIndex >= 0)
            PlaceOnPeg(disc, disc.HomePegIndex, animate: false);
    }

    static float HorizontalDistSq(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
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
        if (r != null)
            r.sharedMaterial = mat;
    }
}

/// <summary>One Tower of Hanoi disc (ring). SizeIndex 0 = smallest.</summary>
public class HanoiDisc : MonoBehaviour
{
    public int SizeIndex { get; private set; }
    public int PegIndex { get; set; } = -1;
    public int HomePegIndex { get; private set; } = -1;

    TowerOfHanoiSetup _board;
    bool _held;

    public void Init(TowerOfHanoiSetup board, int sizeIndex)
    {
        _board = board;
        SizeIndex = sizeIndex;
    }

    public void OnPickedUp()
    {
        HomePegIndex = PegIndex;

        // Only the top disc of a peg may be lifted.
        if (_board != null && !_board.IsTopDisc(this))
        {
            _held = false;
            StartCoroutine(ForceDropIllegal());
            return;
        }

        _held = true;
        _board?.DetachFromPeg(this);
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
            rb.isKinematic = false;
    }

    IEnumerator ForceDropIllegal()
    {
        yield return null;
        var grab = GetComponent<XRGrabInteractable>();
        if (grab != null && grab.isSelected && grab.interactionManager != null)
        {
            var interactor = grab.firstInteractorSelecting;
            if (interactor != null)
                grab.interactionManager.SelectExit((IXRSelectInteractor)interactor, grab);
        }
        _board?.ReturnToPeg(this);
    }

    public void OnReleased()
    {
        if (!_held && PegIndex >= 0)
            return;
        _held = false;

        if (_board == null)
            return;

        int peg = _board.FindNearestLegalPeg(this, transform.position);
        if (peg >= 0)
            _board.PlaceOnPeg(this, peg, animate: true);
        else
            _board.ReturnToPeg(this);
    }
}
