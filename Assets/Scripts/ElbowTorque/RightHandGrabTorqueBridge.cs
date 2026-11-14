using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Listens to the right-hand XR grab interactor and drives ElbowTorqueOutput
/// from the held object's BoxMass. Attach to the same GameObject as the
/// right controller's XRDirectInteractor (or XRRayInteractor).
/// </summary>
[RequireComponent(typeof(XRBaseInteractor))]
public class RightHandGrabTorqueBridge : MonoBehaviour
{
    [Tooltip("Optional. If empty, uses ElbowTorqueOutput.Instance.")]
    public ElbowTorqueOutput torqueOutput;

    XRBaseInteractor _interactor;

    void Awake()
    {
        _interactor = GetComponent<XRBaseInteractor>();
    }

    void OnEnable()
    {
        _interactor.selectEntered.AddListener(OnSelectEntered);
        _interactor.selectExited.AddListener(OnSelectExited);
    }

    void OnDisable()
    {
        _interactor.selectEntered.RemoveListener(OnSelectEntered);
        _interactor.selectExited.RemoveListener(OnSelectExited);

        ResolveOutput()?.ClearHeld();
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        var output = ResolveOutput();
        if (output == null)
            return;

        var box = args.interactableObject.transform.GetComponentInParent<BoxMass>();
        if (box == null)
        {
            // Grabbed something without BoxMass — treat as zero torque.
            output.ClearHeld();
            return;
        }

        output.SetHeldMass(box.massKg);
        Debug.Log($"[ElbowTorque] Grabbed '{box.name}' mass={box.massKg:F1} kg");
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        // If something else is still selected (multi-grab), keep its mass; else clear.
        var output = ResolveOutput();
        if (output == null)
            return;

        if (_interactor.hasSelection)
        {
            var selected = _interactor.interactablesSelected;
            for (int i = 0; i < selected.Count; i++)
            {
                var box = selected[i].transform.GetComponentInParent<BoxMass>();
                if (box == null)
                    continue;

                output.SetHeldMass(box.massKg);
                return;
            }
        }

        output.ClearHeld();
        Debug.Log("[ElbowTorque] Released — torque = 0");
    }

    ElbowTorqueOutput ResolveOutput()
    {
        if (torqueOutput != null)
            return torqueOutput;

        return ElbowTorqueOutput.Instance;
    }
}
