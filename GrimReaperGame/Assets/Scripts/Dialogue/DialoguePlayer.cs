using DialogueSystem;
using FMOD;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;



namespace DialogueSystem
{
    [System.Serializable] public class StringBoolEvent : UnityEvent<string, bool> { }

    public class DialoguePlayer : MonoBehaviour
    {
        [Header("Playback Source")]
        public DialogueSequenceSO sequence;     // Assign per instance
        public bool playOnStart = false;        // Optional auto trigger

        [Header("Scene Refs")]
        public Camera playerCamera;             // Usually main camera (first-person)
        public Canvas overlayCanvas;            // Screen-space canvas hosting UI

        [Header("UI Prefabs/Refs")]
        public TrackedBubbleUI trackedBubblePrefab;
        public OmniBubbleUI omniUI;             // narrator panel (assign existing)
        public DecisionPanelUI decisionUI;      // decision panel (assign existing)

        [Header("Gameplay Freeze on Decision")]
        [Tooltip("Player movement script to disable while a decision is up.")]
        public MonoBehaviour movementToDisable; // e.g., FirstPersonRigidbodyController_WithFootsteps
        public bool unlockCursorDuringDecision = true;      // decision panel (assign existing)

        [Header("Input (New Input System)")]
        [Tooltip("Advance to next or complete typewriter when performed.")]
        public UnityEngine.InputSystem.InputActionReference advanceAction;
        [Tooltip("Optional: perform to call Trigger() and start this player's sequence.")]
        public UnityEngine.InputSystem.InputActionReference triggerAction;

        [Header("Events")]
        public UnityEvent OnSequenceStart;
        public UnityEvent OnSequenceEnd;
        [Tooltip("(sequenceId, takeLife) emitted when the player makes a decision.")]
        public StringBoolEvent OnDecision;

        private DialogueSequenceSO _active;
        private int _index;
        private bool _isTyping;
        private bool _awaitingDecision;

        private Dictionary<string, SpeakerSource> _speakers = new();
        private TrackedBubbleUI _bubble;

        // Typewriter state
        private TMP_Text _currentTMP;
        private string _fullText;
        private float _cps;
        private FMODUnity.EventReference _blipEventRef;

        private SpeakerSource _currentSpeakerSource;



        void Awake()
        {
            if (!playerCamera) playerCamera = Camera.main;
            CacheSpeakers();

            if (trackedBubblePrefab)
            {
                _bubble = Instantiate(trackedBubblePrefab, overlayCanvas.transform);
                _bubble.Initialize(overlayCanvas, playerCamera);
                _bubble.SetVisible(false);
            }

            if (omniUI) omniUI.SetVisible(false);
            if (decisionUI) decisionUI.Hide();
        }

        void Start()
        {
            if (playOnStart && sequence) Trigger();
        }

        void OnEnable()
        {
            if (advanceAction && advanceAction.action != null)
            {
                advanceAction.action.Enable();
                advanceAction.action.performed += OnAdvancePerformed;
            }
            if (triggerAction && triggerAction.action != null)
            {
                triggerAction.action.Enable();
                triggerAction.action.performed += OnTriggerPerformed;
            }
        }

        void OnDisable()
        {
            if (advanceAction && advanceAction.action != null)
            {
                advanceAction.action.performed -= OnAdvancePerformed;
                advanceAction.action.Disable();
            }
            if (triggerAction && triggerAction.action != null)
            {
                triggerAction.action.performed -= OnTriggerPerformed;
                triggerAction.action.Disable();
            }
        }

        private void OnAdvancePerformed(UnityEngine.InputSystem.InputAction.CallbackContext ctx)
        {
            if (_active == null || _awaitingDecision) return;
            if (_isTyping) SkipTypewriter(); else NextLine();
        }

        private void OnTriggerPerformed(UnityEngine.InputSystem.InputAction.CallbackContext ctx)
        {
            if (_active == null && sequence != null) Trigger();
        }

        // Public API: called by other scripts (e.g., trigger volumes)
        public void Trigger()
        {
            Play(sequence);
        }

        public void Play(DialogueSequenceSO seq)
        {
            if (seq == null) return;
            _active = seq;
            _index = -1;
            OnSequenceStart?.Invoke();
            NextLine();
        }

        public void Stop()
        {
            StopAllCoroutines();
            _active = null;
            _isTyping = false;
            _awaitingDecision = false;
            HideAllUI();
            OnSequenceEnd?.Invoke();
        }

        private void HideAllUI()
        {
            if (_bubble) _bubble.SetVisible(false);
            if (omniUI) omniUI.SetVisible(false);
            if (decisionUI) decisionUI.Hide();
        }

        private void NextLine()
        {
            if (_active == null) return;
            _index++;
            if (_index >= _active.lines.Count) { Stop(); return; }

            var line = _active.lines[_index];
            ShowLine(line);
        }

        private void ShowLine(DialogueLine line)
        {
            StopAnyAudio();
            StopAllCoroutines();
            _isTyping = false;
            _awaitingDecision = false;
            HideAllUI();
            _currentTMP = null;
            _currentSpeakerSource = null;

            SpeakerSource speaker = null; // initialize to satisfy definite assignment in older C# compilers
            bool hasSpeaker = !string.IsNullOrWhiteSpace(line.speakerKey) && _speakers.TryGetValue(line.speakerKey, out speaker);

            if (hasSpeaker)
            {
                _currentSpeakerSource = speaker;
                if (_bubble)
                {
                    _bubble.SetVisible(true);
                    _bubble.SetTarget(speaker.transform, speaker.worldOffset);
                    _bubble.SetContent(line.speakerName, "");
                    _currentTMP = _bubble.bodyText;
                }
            }
            else
            {
                // Omniscient narrator / no speaker source
                if (omniUI)
                {
                    omniUI.SetVisible(true);
                    omniUI.SetContent(string.IsNullOrEmpty(line.speakerName) ? "" : line.speakerName, "");
                    _currentTMP = omniUI.bodyText;
                }
            }

            if (_currentTMP == null)
            {
                UnityEngine.Debug.LogWarning("DialoguePlayer: No TMP target to render text to. Check UI assignments.");
                return;
            }

            // Typewriter start
            _fullText = line.text;
            _cps = Mathf.Max(1f, line.charsPerSecond);
            _blipEventRef = line.blipEvent;
            StartCoroutine(TypewriterRoutine(line));

            // Voice one-shot via AudioManager
            if (AudioManager.instance != null && !line.voiceEvent.IsNull)
            {
                Vector3 pos = (_currentSpeakerSource != null) ? _currentSpeakerSource.AnchorPosition : playerCamera.transform.position;
                AudioManager.instance.PlayOneShot(line.voiceEvent, pos);
            }


        }

        private IEnumerator TypewriterRoutine(DialogueLine line)
        {
            _isTyping = line.useTypewriter;

            if (!_isTyping)
            {
                _currentTMP.text = _fullText;
                yield return MaybeAutoAdvanceOrDecision(line);
                yield break;
            }

            _currentTMP.text = _fullText;
            _currentTMP.ForceMeshUpdate();
            int total = _currentTMP.textInfo.characterCount;
            int visible = 0;
            _currentTMP.maxVisibleCharacters = 0;

            float delay = 1f / _cps;

            while (visible < total)
            {
                visible++;
                _currentTMP.maxVisibleCharacters = visible;


                yield return new WaitForSeconds(delay);
            }

            _isTyping = false;
            yield return MaybeAutoAdvanceOrDecision(line);
        }

        private IEnumerator MaybeAutoAdvanceOrDecision(DialogueLine line)
        {
            if (line.doesTriggerDecision && decisionUI != null)
            {
                _awaitingDecision = true;
                _bubble.transform.SetAsFirstSibling();
                decisionUI.OnChoice.RemoveListener(OnDecisionInternal);
                decisionUI.OnChoice.AddListener(OnDecisionInternal);
                decisionUI.Show($"Make your choice.");
                // Freeze gameplay input + cursor
                if (movementToDisable) movementToDisable.enabled = false;
                if (unlockCursorDuringDecision)
                {
                    var fp = movementToDisable as FirstPersonRigidbodyController_WithFootsteps;
                    if (fp) fp.SetCursorLocked(false); else { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
                }
                yield break; // Wait for user choice to continue
            }


            if (_active != null && _active.autoAdvanceDelay > 0f)
            {
                yield return new WaitForSeconds(_active.autoAdvanceDelay);
                NextLine();
            }
        }



        private void OnDecisionInternal(bool takeLife)
        {
            // Unfreeze gameplay
            if (unlockCursorDuringDecision)
            {
                var fp = movementToDisable as FirstPersonRigidbodyController_WithFootsteps;
                if (fp) fp.SetCursorLocked(true); else { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            }
            if (movementToDisable) movementToDisable.enabled = true;

            _awaitingDecision = false;
            OnDecision?.Invoke(_active != null ? _active.sequenceId : string.Empty, takeLife);
            NextLine();
        }
        

        private void SkipTypewriter()
        {
            _isTyping = false;
            if (_currentTMP != null)
                _currentTMP.maxVisibleCharacters = int.MaxValue;
        }

        private void StopAnyAudio() { /* no persistent dialogue audio to stop when using one-shots */ }

        private void CacheSpeakers()
        {
            _speakers.Clear();
            var found = FindObjectsByType<SpeakerSource>(FindObjectsSortMode.None);
            foreach (var s in found)
            {
                if (!string.IsNullOrEmpty(s.speakerKey) && !_speakers.ContainsKey(s.speakerKey))
                    _speakers.Add(s.speakerKey, s);
            }
        }

        // Call this if you spawn speakers at runtime before playing
        public void RebuildSpeakerMap() => CacheSpeakers();
    }
}