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
        public bool playOnStart = false;

        [Header("Scene Refs")]
        public Camera playerCamera;             // Usually Main Camera
        public Canvas overlayCanvas;            // Shared screen-space canvas

        [Header("UI Prefabs/Refs (instanced per player)")]
        public TrackedBubbleUI trackedBubblePrefab;
        public OmniBubbleUI omniPrefab;
        public DecisionPanelUI decisionPrefab;

        // Runtime instances owned by THIS player
        private TrackedBubbleUI _bubble;
        private OmniBubbleUI _omni;
        private DecisionPanelUI _decision;

        [Header("Gameplay Freeze on Decision")]
        [Tooltip("Player movement script to disable while a decision is up.")]
        public MonoBehaviour movementToDisable; // e.g., FirstPersonRigidbodyController_WithFootsteps
        public bool unlockCursorDuringDecision = true;

        [Header("Input (New Input System)")]
        [Tooltip("Advance/complete line. Also used to OPEN the decision when pending.")]
        public UnityEngine.InputSystem.InputActionReference advanceAction;
        [Tooltip("Optional: perform to call Trigger() and start this player's sequence.")]
        public UnityEngine.InputSystem.InputActionReference triggerAction;

        [Header("Events")]
        public UnityEvent OnSequenceStart;
        public UnityEvent OnSequenceEnd;
        [Tooltip("(sequenceId, takeLife) emitted when the player makes a decision.")]
        public StringBoolEvent OnDecision;

        // State
        private DialogueSequenceSO _active;
        private int _index;
        private bool _isTyping;
        private bool _awaitingDecision;

        // “Press Interact again to show decision” support
        private bool _pendingDecision;
        private DialogueLine _pendingDecisionLine;

        private readonly Dictionary<string, SpeakerSource> _speakers = new();
        private SpeakerSource _currentSpeakerSource;

        // Typewriter state
        private TMP_Text _currentTMP;
        private string _fullText;
        private float _cps;

        void Awake()
        {
            if (!playerCamera) playerCamera = Camera.main;
            CacheSpeakers();

            if (overlayCanvas == null)
            {
                Debug.LogError("DialoguePlayer: overlayCanvas not assigned.");
                enabled = false; return;
            }

            // Create our own UI instances so multiple players don’t collide
            if (trackedBubblePrefab)
            {
                _bubble = Instantiate(trackedBubblePrefab, overlayCanvas.transform);
                _bubble.Initialize(overlayCanvas, playerCamera);
                _bubble.SetVisible(false);
                _bubble.transform.SetAsFirstSibling(); // keep behind other panels
            }

            if (omniPrefab)
            {
                _omni = Instantiate(omniPrefab, overlayCanvas.transform);
                _omni.SetVisible(false);
            }

            if (decisionPrefab)
            {
                _decision = Instantiate(decisionPrefab, overlayCanvas.transform);
                _decision.Hide();
            }
        }

        void Start()
        {
            if (playOnStart && sequence) Trigger();
        }

        void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            if (advanceAction && advanceAction.action != null)
            { advanceAction.action.Enable(); advanceAction.action.performed += OnAdvancePerformed; }
            if (triggerAction && triggerAction.action != null)
            { triggerAction.action.Enable(); triggerAction.action.performed += OnTriggerPerformed; }
#endif
        }

        void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            if (advanceAction && advanceAction.action != null)
            { advanceAction.action.performed -= OnAdvancePerformed; advanceAction.action.Disable(); }
            if (triggerAction && triggerAction.action != null)
            { triggerAction.action.performed -= OnTriggerPerformed; triggerAction.action.Disable(); }
#endif
        }

        private void OnAdvancePerformed(UnityEngine.InputSystem.InputAction.CallbackContext ctx)
        {
            if (_active == null) return;

            // If a decision is pending, this press opens the panel
            if (_pendingDecision && _decision != null)
            {
                _pendingDecision = false;
                _awaitingDecision = true;

                if (_bubble) _bubble.transform.SetAsFirstSibling(); // keep behind panel
                _decision.OnChoice.RemoveListener(OnDecisionInternal);
                _decision.OnChoice.AddListener(OnDecisionInternal);
                _decision.Show("Make your choice.");

                // Freeze gameplay input + cursor
                if (movementToDisable) movementToDisable.enabled = false;
                if (unlockCursorDuringDecision)
                {
                    var fp = movementToDisable as FirstPersonRigidbodyController_WithFootsteps;
                    if (fp) fp.SetCursorLocked(false);
                    else { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
                }
                return;
            }

            // If decision panel is up, ignore advance
            if (_awaitingDecision) return;

            if (_isTyping) SkipTypewriter();
            else NextLine();
        }

        private void OnTriggerPerformed(UnityEngine.InputSystem.InputAction.CallbackContext ctx)
        {
            if (_active == null && sequence != null) Trigger();
        }

        // Public API: called by other scripts (e.g., trigger volumes)
        public void Trigger() => Play(sequence);

        public void Play(DialogueSequenceSO seq)
        {
            if (seq == null) return;
            _active = seq;
            _index = -1;
            _pendingDecision = false;
            _awaitingDecision = false;
            OnSequenceStart?.Invoke();
            NextLine();
        }

        public void Stop()
        {
            StopAllCoroutines();
            _active = null;
            _isTyping = false;
            _awaitingDecision = false;
            _pendingDecision = false;
            HideAllUI();                 // only hides THIS player's UI
            OnSequenceEnd?.Invoke();
        }

        private void HideAllUI()
        {
            if (_bubble) _bubble.SetVisible(false);
            if (_omni) _omni.SetVisible(false);
            if (_decision) _decision.Hide();
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
            StopAllCoroutines();
            _isTyping = false;
            _awaitingDecision = false;
            _pendingDecision = false;
            _pendingDecisionLine = null;
            _currentTMP = null;
            _currentSpeakerSource = null;
            HideAllUI();

            SpeakerSource speaker = null;
            bool hasSpeaker = !string.IsNullOrWhiteSpace(line.speakerKey) &&
                              _speakers.TryGetValue(line.speakerKey, out speaker);

            if (hasSpeaker && _bubble)
            {
                _currentSpeakerSource = speaker;
                _bubble.SetVisible(true);
                _bubble.SetTarget(speaker.transform, speaker.worldOffset);
                _bubble.SetContent(line.speakerName, "");
                _currentTMP = _bubble.bodyText;
            }
            else if (_omni)
            {
                _omni.SetVisible(true);
                _omni.SetContent(string.IsNullOrEmpty(line.speakerName) ? "" : line.speakerName, "");
                _currentTMP = _omni.bodyText;
            }

            if (_currentTMP == null)
            {
                Debug.LogWarning("DialoguePlayer: No TMP target to render text to. Check UI assignments.");
                return;
            }

            // Typewriter start
            _fullText = line.text;
            _cps = Mathf.Max(1f, line.charsPerSecond);
            StartCoroutine(TypewriterRoutine(line));

            // Voice one-shot via your AudioManager
            if (AudioManager.instance != null && !line.voiceEvent.IsNull)
            {
                Vector3 pos = (_currentSpeakerSource != null)
                              ? _currentSpeakerSource.AnchorPosition
                              : (playerCamera ? playerCamera.transform.position : transform.position);
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
            if (line.doesTriggerDecision && _decision != null)
            {
                // Arm the decision; user must press Interact again to open it
                _pendingDecision = true;
                _pendingDecisionLine = line;
                yield break;
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
                if (fp) fp.SetCursorLocked(true);
                else { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            }
            if (movementToDisable) movementToDisable.enabled = true;

            _awaitingDecision = false;
            OnDecision?.Invoke(_active != null ? _active.sequenceId : string.Empty, takeLife);
            NextLine();
        }

        private void SkipTypewriter()
        {
            _isTyping = false;
            if (_currentTMP != null) _currentTMP.maxVisibleCharacters = int.MaxValue;
        }

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

        // Call if you spawn speakers at runtime before playing
        public void RebuildSpeakerMap() => CacheSpeakers();
    }
}
