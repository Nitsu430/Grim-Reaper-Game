using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DialogueSystem
{
    public class DecisionPanelUI : MonoBehaviour
    {
        [System.Serializable] public class BoolEvent : UnityEvent<bool> { }

        public GameObject root;
        public TMP_Text promptText;
        public Button takeLifeButton;
        public Button leaveAloneButton;
        public BoolEvent OnChoice = new BoolEvent();
        public UIKawaseBlurController blurController;

        void Awake()
        {
            if (takeLifeButton) takeLifeButton.onClick.AddListener(() => Choose(true));
            if (leaveAloneButton) leaveAloneButton.onClick.AddListener(() => Choose(false));
            Hide();
        }

        public void Show(string prompt)
        {
            if (promptText) promptText.text = prompt;
            if (root)
            {
                root.SetActive(true);
                // Bring to front so it isn't hidden by other panels
                root.transform.SetAsLastSibling();
                // Select a default button for keyboard/controller
                var sel = takeLifeButton ? takeLifeButton.gameObject : (leaveAloneButton ? leaveAloneButton.gameObject : null);
                if (sel) UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(sel);

                blurController.TweenRadius(2.5f, 1f);   // animate in
            }
            
        }

        public void Hide()
        {
            if(blurController.isActiveAndEnabled) blurController.TweenRadius(0f, 1f);
            if (root) root.SetActive(false);
        }


       

        private void Choose(bool takeLife)
        {
            OnChoice.Invoke(takeLife);
            Hide();
        }
    }
}