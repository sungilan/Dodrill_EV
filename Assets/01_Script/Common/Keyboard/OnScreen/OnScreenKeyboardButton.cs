using UnityEngine.UI;
using UnityEngine;

public class OnScreenKeyboardButton : MonoBehaviour
{
    Button myBtn;
    public string keycode;
    private OnScreenKeyboard _onScreenKeyboard;

    private void Awake() {
        myBtn = GetComponent<Button>();
        _onScreenKeyboard = GetComponentInParent<OnScreenKeyboard>();
        
        myBtn.onClick.AddListener(() => {
            _onScreenKeyboard?.SendKey(keycode);
        });

        keycode = ExtractAfterFirstUnderscore(name);
    }

    string ExtractAfterFirstUnderscore(string text)
    {
        int idx = text.IndexOf('_');
        if (idx >= 0 && idx < text.Length - 1)
        {
            return text.Substring(idx + 1);
        }
        return string.Empty;
    }
}
