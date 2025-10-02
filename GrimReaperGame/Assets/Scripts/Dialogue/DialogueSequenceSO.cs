using FMODUnity;
using System.Collections.Generic;
using UnityEngine;


namespace DialogueSystem
{
    [System.Serializable]
    public class DialogueLine
    {
        [Tooltip("Speaker key that maps to a SpeakerSource in the scene. Leave empty for omniscient / non-diegetic narrator lines.")]
        public string speakerKey;

        [Tooltip("Displayed name in the bubble or UI.")]
        public string speakerName;

        [TextArea(2, 6)] public string text;

        [Header("Typewriter")]
        public bool useTypewriter = true;
        [Range(1, 120)] public float charsPerSecond = 45f;

        [Header("Decision Gate")]
        [Tooltip("If true, showing this line will pause and prompt the player to choose: Take Life / Leave Alone.")]
        public bool doesTriggerDecision = false;

        [Header("Audio (optional)")]
        [Tooltip("One-shot voice line to emit at the speaker (or camera if narrator). Uses your AudioManager.")]
        public FMODUnity.EventReference voiceEvent;
        [Tooltip("Per-character blip SFX during typewriter (played at camera). Uses your AudioManager.")]
        public FMODUnity.EventReference blipEvent;
    }


    [CreateAssetMenu(menuName = "Dialogue/Sequence", fileName = "NewDialogueSequence")]
    public class DialogueSequenceSO : ScriptableObject
    {
        public List<DialogueLine> lines = new List<DialogueLine>();


        [Header("Flow")]
        [Tooltip("If > 0, auto-advance lines after this delay when not waiting for a decision.")]
        public float autoAdvanceDelay = 0f;


        [Tooltip("Identifier for analytics or callbacks.")]
        public string sequenceId = System.Guid.NewGuid().ToString();
    }
}