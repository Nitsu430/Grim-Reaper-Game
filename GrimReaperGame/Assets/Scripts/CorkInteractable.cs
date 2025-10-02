using UnityEngine;
using System.Collections;

public class CorkboardInteractable : InteractableBase
{
    [Header("Camera Target")]
    public Transform cameraPosition;         // child transform where the camera should move
    public float enterDuration = 0.35f;
    public float exitDuration = 0.30f;
    public AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

    Vector3 savedLocalPos;
    Quaternion savedLocalRot;
    Transform camParent;

    public override void BeginInteract(PlayerInteraction player)
    {
        Debug.Log("Entered");
        if (inUse || player == null || player.playerCamera == null || cameraPosition == null) return;
        inUse = true;

        // Freeze player and unlock cursor
        player.FreezePlayer(true, unlockCursor: true);

        // Store current camera local transform (assuming it's a child of the player)
        camParent = player.playerCamera.transform.parent;
        savedLocalPos = player.playerCamera.transform.localPosition;
        savedLocalRot = player.playerCamera.transform.localRotation;

        player.StartCoroutine(MoveCamera(player.playerCamera.transform, cameraPosition.position, cameraPosition.rotation, enterDuration,
            () => { /* now 'inside' the corkboard */ }));
    }

    public override void EndInteract(PlayerInteraction player)
    {
        if (!inUse || player == null || player.playerCamera == null) return;

        // Move camera back, then unfreeze
        var cam = player.playerCamera.transform;
        Vector3 worldBackPos = camParent.TransformPoint(savedLocalPos);
        Quaternion worldBackRot = camParent.rotation * savedLocalRot;

        player.StartCoroutine(MoveCamera(cam, worldBackPos, worldBackRot, exitDuration, () =>
        {
            // restore local space (avoid drift)
            cam.SetPositionAndRotation(worldBackPos, worldBackRot);
            cam.SetParent(camParent, worldPositionStays: true);

            player.FreezePlayer(false, unlockCursor: true);
            inUse = false;
            player.ClearActive(this);
        }));
    }

    IEnumerator MoveCamera(Transform cam, Vector3 targetPos, Quaternion targetRot, float dur, System.Action onDone)
    {
        // Detach so our movement isn't affected by player updates
        cam.SetParent(null, worldPositionStays: true);

        Vector3 startPos = cam.position;
        Quaternion startRot = cam.rotation;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float a = ease.Evaluate(Mathf.Clamp01(t / Mathf.Max(0.0001f, dur)));
            cam.SetPositionAndRotation(Vector3.LerpUnclamped(startPos, targetPos, a),
                                       Quaternion.SlerpUnclamped(startRot, targetRot, a));
            yield return null;
        }
        cam.SetPositionAndRotation(targetPos, targetRot);
        onDone?.Invoke();
    }
}
