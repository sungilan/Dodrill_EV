using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(TMP_InputField))]
public class OnScreenKeyboardInputfield1 : MonoBehaviour
{
    public OnScreenKeyboard targetOnScreenKeyboard;
    private TMP_InputField _inputField;
    public string inputtedString;

    private void Awake()
    {
        _inputField = GetComponent<TMP_InputField>();
        
        if (_inputField == null)
            return;

        _inputField.shouldHideMobileInput = true;
    }

    public void SaveInputedString(string _inputStr)
    {
        inputtedString = _inputStr;
    }



    public void OnPointerDown()
    {
        if (targetOnScreenKeyboard)
        {
            targetOnScreenKeyboard.gameObject.SetActive(true);
            targetOnScreenKeyboard.ShowKeyboard(_inputField, this);
            targetOnScreenKeyboard.SetTarget(_inputField);
        }
    }
}
