using UnityEngine;

// Interactable that moves the object in front of the camera for inspection.
// Rotate by click & drag (Input Actions). Escape to put down.
// Triggers the PLAYER'S DialoguePlayer (the one that already holds the sequence).

public class InspectableInteractable : InteractableBase
{
    [Header("Inspection Placement")]
    [Tooltip("Distance in front of the camera to place the item while inspecting.")]
    public float inspectDistance = 0.8f;
    [Tooltip("Local scale to apply while inspecting (1 = keep original).")]
    public float inspectScale = 1f;
    [Tooltip("Seconds to move from world to inspect pose.")]
    public float moveDuration = 0.25f;
    public AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Rotation (Click & Drag) — New Input System")]
    [Tooltip("Pointer delta while dragging (e.g., Pointer/delta).")]
    public UnityEngine.InputSystem.InputActionReference dragDeltaAction; // Vector2
    [Tooltip("Pointer/button hold to rotate (e.g., Pointer/press or a custom 'Rotate' action).")]
    public UnityEngine.InputSystem.InputActionReference dragHoldAction;  // Button
    public float rotationSpeed = 120f; // degrees/sec at delta=1

    [Header("Dialogue")]
    public DialogueSystem.DialoguePlayer dialoguePlayer;

    // internal state
    Transform originalParent;
    bool dialogueStarted = false;
    Vector3 originalPos; Quaternion originalRot; Vector3 originalScale;
    Transform rig; // moves/rotates separately from item
    Camera cam;

    bool rotating;

    void OnEnable()
    {
#if ENABLE_INPUT_SYSTEM
        if (dragDeltaAction && dragDeltaAction.action != null) dragDeltaAction.action.Enable();
        if (dragHoldAction && dragHoldAction.action != null) dragHoldAction.action.Enable();
#endif
    }

    void OnDisable()
    {
#if ENABLE_INPUT_SYSTEM
        if (dragDeltaAction && dragDeltaAction.action != null) dragDeltaAction.action.Disable();
        if (dragHoldAction && dragHoldAction.action != null) dragHoldAction.action.Disable();
#endif
    }

    public override void BeginInteract(PlayerInteraction player)
    {
        if (inUse) return;
        inUse = true;

        if (!player || !player.playerCamera) { inUse = false; return; }
        cam = player.playerCamera;

        // Freeze player and UNLOCK cursor for click & drag
        player.FreezePlayer(true, true);

        // Save original pose
        originalParent = transform.parent;
        originalPos = transform.position;
        originalRot = transform.rotation;
        originalScale = transform.localScale;

        // Create/position rig
        if (rig == null)
        {
            var go = new GameObject(name + "_InspectRig");
            rig = go.transform;
        }
        rig.SetPositionAndRotation(cam.transform.position + cam.transform.forward * inspectDistance,
                                   cam.transform.rotation);

        // Disable physics while inspecting
        var rb = GetComponent<Rigidbody>();
        if (rb) { rb.isKinematic = true; rb.detectCollisions = false; }
        foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;

        // Parent under rig and tween into place
        transform.SetParent(rig, worldPositionStays: true);
        Vector3 targetLocalPos = Vector3.zero;
        Quaternion targetLocalRot = Quaternion.identity;
        Vector3 targetLocalScale = Vector3.one * inspectScale;

        StartCoroutine(TweenToLocal(targetLocalPos, targetLocalRot, targetLocalScale, moveDuration, () =>
        {
            rotating = true;

            // 🔹 Trigger the player's DialoguePlayer (it already has the Sequence)
            if (dialoguePlayer) { dialoguePlayer.Trigger(); dialogueStarted = true; }
        }));
    }

    public override void EndInteract(PlayerInteraction player)
    {
        if (!inUse) return;

        if (dialogueStarted && dialoguePlayer)
        {
            dialoguePlayer.Stop();
            dialogueStarted = false;
        }

        rotating = false;
        StartCoroutine(ReturnToWorld(player));
    }

    System.Collections.IEnumerator TweenToLocal(Vector3 tgtPos, Quaternion tgtRot, Vector3 tgtScale, float dur, System.Action onDone)
    {
        Vector3 sPos = transform.localPosition; Quaternion sRot = transform.localRotation; Vector3 sScale = transform.localScale;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float a = ease.Evaluate(Mathf.Clamp01(t / Mathf.Max(0.0001f, dur)));
            transform.localPosition = Vector3.LerpUnclamped(sPos, tgtPos, a);
            transform.localRotation = Quaternion.SlerpUnclamped(sRot, tgtRot, a);
            transform.localScale = Vector3.LerpUnclamped(sScale, tgtScale, a);
            yield return null;
        }
        transform.localPosition = tgtPos; transform.localRotation = tgtRot; transform.localScale = tgtScale;
        onDone?.Invoke();
    }

    System.Collections.IEnumerator ReturnToWorld(PlayerInteraction player)
    {
        // Tween back to saved world pose
        Vector3 sPos = transform.position; Quaternion sRot = transform.rotation; Vector3 sScale = transform.localScale;
        float dur = moveDuration;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float a = ease.Evaluate(Mathf.Clamp01(t / Mathf.Max(0.0001f, dur)));
            transform.position = Vector3.LerpUnclamped(sPos, originalPos, a);
            transform.rotation = Quaternion.SlerpUnclamped(sRot, originalRot, a);
            transform.localScale = Vector3.LerpUnclamped(sScale, originalScale, a);
            yield return null;
        }

        // Restore hierarchy & physics
        transform.SetParent(originalParent, worldPositionStays: true);
        var rb = GetComponent<Rigidbody>();
        if (rb) { rb.isKinematic = false; rb.detectCollisions = true; }
        foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = true;

        // Unfreeze player (re-lock cursor) and clear
        if (player) { player.FreezePlayer(false, true); player.ClearActive(this); }
        inUse = false;
    }

    void Update()
    {
        if (!inUse || !rotating || rig == null) return;

#if ENABLE_INPUT_SYSTEM
        bool holding = (dragHoldAction && dragHoldAction.action != null) && dragHoldAction.action.IsPressed();
        if (!holding) return;

        Vector2 delta = Vector2.zero;
        if (dragDeltaAction && dragDeltaAction.action != null)
            delta = dragDeltaAction.action.ReadValue<Vector2>();
#else
        bool holding = Input.GetMouseButton(0);
        if (!holding) return;
        Vector2 delta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#endif

        if (delta.sqrMagnitude > 0f)
        {
            float dt = Time.unscaledDeltaTime;
            // yaw around world up; pitch around camera right
            rig.rotation = Quaternion.AngleAxis(delta.x * rotationSpeed * dt, Vector3.up) *
                           Quaternion.AngleAxis(-delta.y * rotationSpeed * dt, cam.transform.right) * rig.rotation;
        }
    }
}
