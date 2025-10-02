using TMPro;
using UnityEngine;

namespace DialogueSystem
{
    public class OmniBubbleUI : MonoBehaviour
    {
        public GameObject root;
        public TMP_Text speakerLabel;
        public TMP_Text bodyText;

        public void SetVisible(bool v) => root.SetActive(v);

        public void SetContent(string speaker, string body)
        {
            if (speakerLabel) speakerLabel.text = speaker;
            if (bodyText) bodyText.text = body;
        }
    }
}