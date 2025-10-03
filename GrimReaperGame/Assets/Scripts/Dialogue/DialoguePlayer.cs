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
        public DialogueSequenceSO sequence;
        public bool playOnStart = false;

        [Header("Scene Refs")]
        public Camera playerCamera;
        public Canvas overlayCanvas;

        [Header("UI Prefabs (instanced per player)")]
        public TrackedBubbleUI trackedBubblePrefab;
        public OmniBubbleUI omniPrefab;
        public DecisionPanelUI decisionPrefab;
        public GameObject UIRoot;

        // Runtime instances owned by THIS player
        private TrackedBubbleUI _bubble;
        private OmniBubbleUI _omni;
        private DecisionPanelUI _decision;

        [Header("Gameplay Freeze on Decision")]
        [Tooltip("Player movement script to disable while a decision is up.")]
        public MonoBehaviour movementToDisable; // e.g., FirstPersonRigidbodyController_WithFootsteps
        public bool unlockCursorDuringDecision = true;

        [Header("Input (New Input System)")]
        [Tooltip("Advance/complete line. Also opens decision when pending.")]
        public UnityEngine.InputSystem.InputActionReference advanceAction;
        [Tooltip("Optional: perform to call Trigger() and start this player's sequence.")]
        public UnityEngine.InputSystem.InputActionReference triggerAction;

        [Header("Events")]
        public UnityEvent OnSequenceStart;
        public UnityEvent OnSequenceEnd;
        [Tooltip("(sequenceId, takeLife) emitted when the player makes a decision.")]
        public StringBoolEvent OnDecision;

        // ----- State -----
        private DialogueSequenceSO _active;
        private int _index = -1;
        private bool _isTyping;
        private bool _awaitingDecision;   // panel visible
        private bool _pendingDecision;    // waiting for user to press Interact to open panel

        // Expose for other systems (e.g., inspectable)
        public bool IsDecisionActive => _awaitingDecision;
        public bool IsDecisionPending => _pendingDecision;

        private readonly Dictionary<string, SpeakerSource> _speakers = new();
        private SpeakerSource _currentSpeakerSource;

        // Typewriter state
        private TMP_Text _currentTMP;
        private string _fullText;
        private float _cps;
        private FMODUnity.EventReference _blipEventRef; // kept for future per-char blips if needed

        // ==== Global decision state (class-wide) ====
        private static int _activeDecisionCount = 0;
        public static bool AnyDecisionActive => _activeDecisionCount > 0;
        public static event System.Action<DialoguePlayer, bool> DecisionActiveChanged;

        void Awake()
        {
            if (!playerCamera) playerCamera = Camera.main;
            if (!overlayCanvas) { Debug.LogError("DialoguePlayer: overlayCanvas not assigned."); enabled = false; return; }

            CacheSpeakers();

            if (trackedBubblePrefab)
            {
                _bubble = Instantiate(trackedBubblePrefab, overlayCanvas.transform);
                _bubble.Initialize(overlayCanvas, playerCamera);
                _bubble.SetVisible(false);
                _bubble.transform.SetAsFirstSibling(); // behind other UI
            }

            if (omniPrefab)
            {
                _omni = Instantiate(omniPrefab, overlayCanvas.transform);
                _omni.transform.parent = UIRoot.transform;
                _omni.SetVisible(false);
                _omni.transform.SetAsLastSibling();
            }

            if (decisionPrefab)
            {
                _decision = Instantiate(decisionPrefab, overlayCanvas.transform);
                _decision.transform.parent = UIRoot.transform;
                _decision.Hide();
                _decision.transform.SetAsLastSibling();
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

            // OPEN decision if one is pending
            if (_pendingDecision && _decision != null)
            {
                _pendingDecision = false;
                _awaitingDecision = true;

                if (_bubble) _bubble.transform.SetAsFirstSibling(); // keep behind panel
                _decision.OnChoice.RemoveListener(OnDecisionInternal);
                _decision.OnChoice.AddListener(OnDecisionInternal);
                _decision.Show("Make your choice.");

                // bump global counter only when panel is actually shown
                _activeDecisionCount++;
                DecisionActiveChanged?.Invoke(this, true);

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

        // Public API
        public void Trigger() => Play(sequence);

        public void Play(DialogueSequenceSO seq)
        {
            if (seq == null) return;
            _active = seq;
            _index = -1;
            _isTyping = false;
            _awaitingDecision = false;
            _pendingDecision = false;
            OnSequenceStart?.Invoke();
            NextLine();
        }

        public void Stop()
        {
            StopAllCoroutines();

            // If this player was holding a decision panel, clear the global count.
            if (_awaitingDecision)
            {
                _awaitingDecision = false;
                if (_activeDecisionCount > 0) _activeDecisionCount--;
                DecisionActiveChanged?.Invoke(this, false);
            }

            _active = null;
            _isTyping = false;
            _pendingDecision = false;
            HideAllUI();
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
            _currentTMP = null;
            _currentSpeakerSource = null;

            // Hide previous UI for THIS player only
            if (_bubble) _bubble.SetVisible(false);
            if (_omni) _omni.SetVisible(false);

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
            _blipEventRef = line.blipEvent;
            StartCoroutine(TypewriterRoutine(line));

            // Voice one-shot via AudioManager
            if (AudioManager.instance != null && !line.voiceEvent.IsNull)
            {
                Vector3 pos = (_currentSpeakerSource != null) ? _currentSpeakerSource.AnchorPosition
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
                // DO NOT show the panel yet; wait for the next Interact press
                _pendingDecision = true;
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

            if (_awaitingDecision)
            {
                _awaitingDecision = false;
                if (_activeDecisionCount > 0) _activeDecisionCount--;
                DecisionActiveChanged?.Invoke(this, false);
            }

            OnDecision?.Invoke(_active != null ? _active.sequenceId : string.Empty, takeLife);
            NextLine();
        }

        private void SkipTypewriter()
        {
            _isTyping = false;
            if (_currentTMP != null)
                _currentTMP.maxVisibleCharacters = int.MaxValue;
        }

        private void CacheSpeakers()
        {
            _speakers.Clear();
            var found = FindObjectsByType<SpeakerSource>(FindObjectsSortMode.None);
            foreach (var s in found)
                if (!string.IsNullOrEmpty(s.speakerKey) && !_speakers.ContainsKey(s.speakerKey))
                    _speakers.Add(s.speakerKey, s);
        }

        public void RebuildSpeakerMap() => CacheSpeakers();
    }
}
