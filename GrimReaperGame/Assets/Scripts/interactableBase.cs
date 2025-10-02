using UnityEngine;

[RequireComponent(typeof(Collider))]
public abstract class InteractableBase : MonoBehaviour, IInteractable
{
    protected bool inUse;
    public bool IsInUse => inUse;

    [Header("Prompt")]
    [TextArea] public string promptText = "Press [E] to interact";

    protected virtual void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    // Handshake with the player's interaction component
    void OnTriggerEnter(Collider other)
    {
        var pi = other.GetComponentInParent<PlayerInteraction>();
        if (pi) pi.RegisterCandidate(this);
    }

    void OnTriggerExit(Collider other)
    {
        var pi = other.GetComponentInParent<PlayerInteraction>();
        if (pi && (Object)pi.Candidate == this) pi.ClearCandidate(this);
    }

    // Base implementations (override in concrete interactables)
    public abstract void BeginInteract(PlayerInteraction player);
    public abstract void EndInteract(PlayerInteraction player);
}
