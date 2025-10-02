using UnityEngine;

public interface IInteractable
{
    // Called when the player presses Interact while inside the trigger
    void BeginInteract(PlayerInteraction player);

    // Called when the player presses Cancel (Escape)
    void EndInteract(PlayerInteraction player);

    // Optional: used to block input when an interaction is already active
    bool IsInUse { get; }
}
