using UnityEngine;
using TMPro;

public class PlayerInteraction : MonoBehaviour
{
    [Header("Scene Refs")]
    public Camera playerCamera;                                  // your FPS camera
    public MonoBehaviour movementToDisable;                      // FirstPersonRigidbodyController_WithFootsteps
    public TMP_Text promptLabel;                                 // optional UI text for prompts

    [Header("Input (New Input System)")]
    public UnityEngine.InputSystem.InputActionReference interactAction; // E
    public UnityEngine.InputSystem.InputActionReference cancelAction;   // Escape

    public IInteractable Candidate { get; private set; }
    public IInteractable Active { get; private set; }

    void Awake()
    {
        if (!playerCamera) playerCamera = Camera.main;
        UpdatePrompt(null);
    }

    void OnEnable()
    {
        if (interactAction && interactAction.action != null)
        { interactAction.action.Enable(); interactAction.action.performed += OnInteract; }
        if (cancelAction && cancelAction.action != null)
        { cancelAction.action.Enable(); cancelAction.action.performed += OnCancel; }
    }

    void OnDisable()
    {
        if (interactAction && interactAction.action != null)
        { interactAction.action.performed -= OnInteract; interactAction.action.Disable(); }
        if (cancelAction && cancelAction.action != null)
        { cancelAction.action.performed -= OnCancel; cancelAction.action.Disable(); }
    }

    public void RegisterCandidate(IInteractable i)
    {
        if (Active != null) return;
        Candidate = i;
        UpdatePrompt((i as InteractableBase)?.promptText);
    }

    public void ClearCandidate(IInteractable i)
    {
        if (Candidate == i) { Candidate = null; UpdatePrompt(null); }
    }

    void OnInteract(UnityEngine.InputSystem.InputAction.CallbackContext _)
    {
        Debug.Log("Interacted");
        if (Active != null || Candidate == null || Candidate.IsInUse) return;
        Active = Candidate;
        UpdatePrompt(null);
        Active.BeginInteract(this);
    }

    void OnCancel(UnityEngine.InputSystem.InputAction.CallbackContext _)
    {
        Active?.EndInteract(this);
    }

    public void FreezePlayer(bool freeze, bool unlockCursor = true)
    {
        if (movementToDisable) movementToDisable.enabled = !freeze;
        if (unlockCursor)
        {
            var fp = movementToDisable as FirstPersonRigidbodyController_WithFootsteps;
            if (fp) fp.SetCursorLocked(!freeze);
            else { Cursor.lockState = freeze ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = freeze; }
        }
    }

    public void ClearActive(IInteractable i)
    {
        if (Active == i) Active = null;
        if (Candidate == null) UpdatePrompt(null);
    }

    void UpdatePrompt(string text)
    {
        if (promptLabel) { promptLabel.gameObject.SetActive(!string.IsNullOrEmpty(text)); promptLabel.text = text ?? ""; }
    }
}
