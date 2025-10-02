using TMPro;
using UnityEngine;

namespace DialogueSystem
{
    public class TrackedBubbleUI : MonoBehaviour
    {
        [Header("Refs")]
        public RectTransform rect;         // root rect
        public RectTransform arrow;        // rotates to face target (UP = forward)
        public TMP_Text speakerLabel;
        public TMP_Text bodyText;

        [Header("Layout")]
        public float edgePadding = 32f;    // padding from edges when clamped (in screen pixels)
        public bool hideArrowWhenOnScreen = true;
        public bool hideBubbleBodyWhenOffScreen = false; // show just an indicator if desired

        private RectTransform _canvasRect;
        private Canvas _canvas;
        private Camera _cam;
        private Transform _target;
        private Vector3 _offset;
        private bool _active;

        public void Initialize(Canvas canvas, Camera cam)
        {
            _canvasRect = canvas.transform as RectTransform;
            _canvas = canvas;
            _cam = cam;
            SetVisible(false);
        }

        public void SetTarget(Transform worldTarget, Vector3 worldOffset)
        {
            _target = worldTarget;
            _offset = worldOffset;
        }

        public void SetContent(string speaker, string body)
        {
            if (speakerLabel) speakerLabel.text = speaker;
            if (bodyText) bodyText.text = body;
        }

        public void SetVisible(bool v)
        {
            _active = v;
            if (rect) rect.gameObject.SetActive(v);
        }

        void LateUpdate()
        {
            if (!_active || _canvasRect == null || _cam == null || _target == null) return;

            Vector3 worldPos = _target.position + _offset;
            Vector3 view = _cam.WorldToViewportPoint(worldPos); // 0..1 viewport, z is depth

            bool isBehind = view.z < 0f;
            Vector2 center = new Vector2(0.5f, 0.5f);
            Vector2 dir = (Vector2)view - center;
            if (isBehind) dir = -dir; // flip so arrow points correctly when behind

            bool onScreen = !isBehind && view.x >= 0f && view.x <= 1f && view.y >= 0f && view.y <= 1f;

            // Desired viewport position
            Vector2 vpPos;
            if (onScreen)
            {
                vpPos = new Vector2(view.x, view.y);
            }
            else
            {
                // Force to nearest screen edge based on dominant axis.
                float ax = Mathf.Abs(dir.x);
                float ay = Mathf.Abs(dir.y);
                if (ax > ay)
                {
                    // left/right edge
                    float x = dir.x > 0 ? 1f : 0f;
                    float y = Mathf.Clamp01(0.5f + (dir.y / ax) * 0.5f);
                    vpPos = new Vector2(x, y);
                }
                else
                {
                    // top/bottom edge
                    float y = dir.y > 0 ? 1f : 0f;
                    float x = Mathf.Clamp01(0.5f + (dir.x / ay) * 0.5f);
                    vpPos = new Vector2(x, y);
                }
            }

            // Convert to screen space
            Vector2 screenPos = new Vector2(vpPos.x * Screen.width, vpPos.y * Screen.height);

            // Clamp so the ENTIRE rect stays on-screen (accounts for pivot & canvas scale)
            screenPos = ClampScreenWithPivot(screenPos);

            // Convert screen → canvas local and assign anchoredPosition (robust with Canvas Scaler)
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPos, _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _cam, out Vector2 localPoint);
            rect.anchoredPosition = localPoint;

            // Arrow rotation points from screen center to the bubble
            Vector2 fromCenter = screenPos - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float angle = Mathf.Atan2(fromCenter.y, fromCenter.x) * Mathf.Rad2Deg - 90f;
            if (arrow) arrow.rotation = Quaternion.Euler(0, 0, angle);

            // Toggle bits
            if (arrow) arrow.gameObject.SetActive(!onScreen || !hideArrowWhenOnScreen);
            if (bodyText)
            {
                bool bodyVisible = !(hideBubbleBodyWhenOffScreen && !onScreen);
                bodyText.enabled = bodyVisible;
            }
        }

        private Vector2 ClampScreenWithPivot(Vector2 screenPos)
        {
            float scale = _canvas ? _canvas.scaleFactor : 1f;
            Vector2 size = rect.rect.size * scale;
            float minX = edgePadding + size.x * rect.pivot.x;
            float maxX = Screen.width - edgePadding - size.x * (1f - rect.pivot.x);
            float minY = edgePadding + size.y * rect.pivot.y;
            float maxY = Screen.height - edgePadding - size.y * (1f - rect.pivot.y);
            return new Vector2(Mathf.Clamp(screenPos.x, minX, maxX), Mathf.Clamp(screenPos.y, minY, maxY));
        }
    }
}