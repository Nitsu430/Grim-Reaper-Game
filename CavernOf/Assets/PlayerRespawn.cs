using UnityEngine;

public class PlayerRespawn : MonoBehaviour
{
    public Transform respawnPoint;   // Assign in inspector
    public float respawnDelay = 0.5f; // Small delay for effect

    private bool isRespawning = false;

    public void Respawn()
    {
        if (!isRespawning)
        {
            isRespawning = true;
            Invoke(nameof(DoRespawn), respawnDelay);
        }
    }

    private void DoRespawn()
    {
        transform.position = respawnPoint.position;
        isRespawning = false;
    }
}
