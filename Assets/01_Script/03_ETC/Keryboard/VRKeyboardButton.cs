using Autohand;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class VRKeyboardButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private VRKeyboard keyboard;
    [SerializeField] private string keyValue;            // 버튼 값 (예: "A", "1", "@", "BACK")
    [SerializeField] private Image image;
    [SerializeField] private Color hoverColor;
    [SerializeField] private Color originColor;
    public TextMeshProUGUI textField;
    private Action upperOneTimeEvent;
    private bool upHover = false;
    private HandTouchButton touchButton;
    private HandTouchEvent touchEvent;
    // 클래스 멤버로 추가
    private static HangulComposer hangulComposer = new HangulComposer();


    private void Awake()
    {
        image = GetComponent<Image>();
        textField = GetComponentInChildren<TextMeshProUGUI>();
        originColor = image.color;
        Button button = GetComponent<Button>();
        button.onClick.AddListener(OnClick);

        //touchButton = GetComponent<HandTouchButton>();
        //touchEvent = GetComponent<HandTouchEvent>();
        //touchButton.touchEvent = touchEvent;
        //touchButton.button = transform;
        //touchButton.pressColor = hoverColor;
        //touchButton.unpressColor = originColor;
        ////touchButton.toggle = false;
        //touchButton.OnPressed.AddListener((Hand h) =>
        //{
        //    OnClick();
        //});
        //touchButton.OnUnpressed.AddListener((Hand h) =>
        //{
        //    if (upHover == true)
        //        return;
        //    image.color = originColor;
        //});
    }

    public void OnClick()
    {
        if (keyboard.targetInput == null) return;

        if (keyValue == "Backspace")
        {
            if (keyboard.targetInput.text.Length > 0)
            {
                keyboard.targetInput.text =
                    keyboard.targetInput.text.Substring(0, keyboard.targetInput.text.Length - 1);
                hangulComposer.Backspace();
            }
        }
        else if (keyValue == "Enter")
        {
            Debug.Log("입력 완료: " + keyboard.targetInput.text);
            keyboard.gameObject.SetActive(false);
        }
        else if (keyValue == "Up") keyboard.SetUpperCase();
        else if (keyValue == "Number") keyboard.ToggleNumberPanel();
        else if (keyValue == "Language") keyboard.ToggleLanguagePanel();
        else
        {
            if (keyboard.languageState == LanguageState.Hangle)
            {
                string lastChar = keyboard.targetInput.text.Length > 0 ?
                                  keyboard.targetInput.text.Substring(keyboard.targetInput.text.Length - 1) : "";
                hangulComposer.Input(keyValue[0], lastChar);
                string composed = hangulComposer.GetComposed();

                if (!string.IsNullOrEmpty(lastChar) && IsHangul(lastChar[0]))
                    keyboard.targetInput.text =
                        keyboard.targetInput.text.Substring(0, keyboard.targetInput.text.Length - 1) + composed;
                else
                    keyboard.targetInput.text += composed;
            }
            else
            {
                keyboard.targetInput.text += keyValue;
            }

            keyboard.SetShiftColor2(false);
            upperOneTimeEvent?.Invoke();
        }
    }

    private bool IsHangul(char c)
    {
        return (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0x3131 && c <= 0x318E);
    }

    private void OnDisable()
    {
        image.color = originColor;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (upHover == true)
            return;
        image.color = hoverColor;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (upHover == true)
            return;
        image.color = originColor;
    }

    public void SetUpperOneTime(Action callback)
    {
        // 한글 쌍자음/쌍모음 변환 처리
        if (keyboard.languageState == LanguageState.Hangle)
        {
            keyValue = ConvertHangleShift(keyValue, true);
        }
        else
        {
            keyValue = keyValue.ToUpper();
        }
        upperOneTimeEvent -= callback;
        upperOneTimeEvent += callback;
        textField.text = keyValue;
    }

    public void SetCapsLock()
    {
        if (keyboard.languageState == LanguageState.Hangle)
        {
            keyValue = ConvertHangleShift(keyValue, true);
        }
        else
        {
            keyValue = keyValue.ToUpper();
        }
        textField.text = keyValue;
    }

    public void SetLower()
    {
        if (keyboard.languageState == LanguageState.Hangle)
        {
            keyValue = ConvertHangleShift(keyValue, false);
        }
        else
        {
            keyValue = keyValue.ToLower();
        }
        textField.text = keyValue;
    }

    // 한글 쌍자음/쌍모음 변환 함수
    private string ConvertHangleShift(string value, bool shift)
    {
        // 변환 매핑 테이블
        string normal = "ㅂㅈㄷㄱㅅㅐㅔ";
        string shifted = "ㅃㅉㄸㄲㅆㅒㅖ";

        Dictionary<char, char> normalToShifted = new Dictionary<char, char>();
        Dictionary<char, char> shiftedToNormal = new Dictionary<char, char>();

        for (int i = 0; i < normal.Length; i++)
        {
            normalToShifted[normal[i]] = shifted[i];
            shiftedToNormal[shifted[i]] = normal[i];
        }

        if (string.IsNullOrEmpty(value))
            return value;

        char c = value[0]; // 한 글자만 처리
        if (shift)
        {
            if (normalToShifted.ContainsKey(c))
                return normalToShifted[c].ToString();
        }
        else
        {
            if (shiftedToNormal.ContainsKey(c))
                return shiftedToNormal[c].ToString();
        }

        // 매핑 안 된 글자는 그대로 반환
        return value;
    }

    public void SetColor(bool hover)
    {
        if(hover == true)
        {
            upHover = true;
            if (image == null)
                image = GetComponent<Image>();
            image.color = hoverColor;
        }
        else
        {
            upHover = false;
            if (image == null)
                image = GetComponent<Image>();
            image.color = originColor;
        }
    }

    
}
