using UnityEngine;

public class RaycastFocusInteractor : MonoBehaviour
{
    public PlayerInteraction player;
    public float maxDistance = 3f;
    public LayerMask mask = ~0;
    public float sphereRadius = 0.05f;

    [Header("Stability")]
    [Tooltip("Seconds to keep candidate after losing sight (prevents flicker).")]
    public float loseSightGrace = 0.15f;

    IInteractable lastLookCandidate;
    float lostSightAt = -999f;

    void Reset() { player = GetComponentInParent<PlayerInteraction>(); }

    void OnDisable()
    {
        // If we owned the candidate, clear it
        if (player && player.Candidate == lastLookCandidate)
            player.ClearCandidate(lastLookCandidate);
        lastLookCandidate = null;
        lostSightAt = -999f;
    }

    void Update()
    {
        if (!player) return;

        // Cast forward from camera
        Ray ray = new Ray(transform.position, transform.forward);
        bool hitSomething = Physics.SphereCast(ray, sphereRadius, out RaycastHit hit,
                                               maxDistance, mask, QueryTriggerInteraction.Ignore);

        if (hitSomething)
        {
            var looked = hit.collider.GetComponentInParent<IInteractable>();

            if (looked != null)
            {
                // Register/refresh our looked-at candidate
                if (player.Candidate != looked)
                    player.RegisterCandidate(looked);

                lastLookCandidate = looked;
                lostSightAt = -999f;
                return;
            }
        }

        // No interactable in sight
        // If we previously set one, clear it after a small grace period
        if (lastLookCandidate != null)
        {
            if (lostSightAt < 0f) lostSightAt = Time.unscaledTime;

            if (Time.unscaledTime - lostSightAt >= loseSightGrace)
            {
                if (player.Candidate == lastLookCandidate)
                    player.ClearCandidate(lastLookCandidate);

                lastLookCandidate = null;
                lostSightAt = -999f;
            }
        }
    }
}
