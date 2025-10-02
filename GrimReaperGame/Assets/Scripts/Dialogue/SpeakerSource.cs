using UnityEngine;


namespace DialogueSystem
{
    public class SpeakerSource : MonoBehaviour
    {
        [Tooltip("Unique key that dialogue lines will reference. E.g., 'GRIM_REAPER', 'TENANT_02'.")]
        public string speakerKey;


        [Tooltip("Offset relative to this transform where the bubble feels anchored (e.g., above head). Used to project to screen.")]
        public Vector3 worldOffset = new Vector3(0, 2.0f, 0);


        public Vector3 AnchorPosition => transform.position + worldOffset;
    }
}