using MasterServerToolkit.MasterServer;
using RootMotion.FinalIK;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Experimental.XR.Interaction;

public enum UpperState
{
    Lower, Upper, CapsLock
}

public enum KeyboardState
{
    Language, Number
}

public enum LanguageState
{
    English, Hangle
}

public class VRKeyboard : MonoBehaviour
{
    public static VRKeyboard Instance { get; private set; }
    public TMP_InputField targetInput;
    public UpperState upperState = UpperState.Lower;
    public KeyboardState keyboardState = KeyboardState.Language;
    public LanguageState languageState = LanguageState.English;
    [SerializeField] private VRKeyboardButton[] alphabets;
    [SerializeField] private VRKeyboardButton[] hangles;
    [SerializeField] private VRKeyboardButton[] shifts;

    public GameObject englishPanel, numberPanel, hanglePanel;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        numberPanel.SetActive(false);
        hanglePanel.SetActive(false);
    }

    public UpperState SetUpperCase()
    {
        switch (upperState)
        {
            case UpperState.Lower:
                upperState = UpperState.Upper;
                foreach (var ele in GetCurrentLanguageButtons())
                    ele.SetUpperOneTime(ClickUpperOnetime);
                SetShiftColor(true);
                foreach (var shift in shifts)
                    shift.textField.text = "↑";
                break;
            case UpperState.Upper:
                upperState = UpperState.CapsLock;
                foreach (var ele in GetCurrentLanguageButtons())
                    ele.SetCapsLock();
                SetShiftColor(true);
                foreach (var shift in shifts)
                    shift.textField.text = "⇧";
                break;
            case UpperState.CapsLock:
                upperState = UpperState.Lower;
                foreach (var ele in GetCurrentLanguageButtons())
                    ele.SetLower();
                SetShiftColor(false);
                foreach (var shift in shifts)
                    shift.textField.text = "↑";
                break;
        }
        return upperState;
    }

    // 현재 언어에 맞는 버튼 배열 반환
    private VRKeyboardButton[] GetCurrentLanguageButtons()
    {
        if (keyboardState == KeyboardState.Number)
            return new VRKeyboardButton[0];
        return languageState == LanguageState.Hangle ? hangles : alphabets;
    }

    public void ClickUpperOnetime()
    {
        if (upperState == UpperState.Upper)
        {
            upperState = UpperState.Lower;
            foreach (var ele in GetCurrentLanguageButtons())
                ele.SetLower();
        }
    }

    public void SetShiftColor(bool hover)
    {
        foreach (var shift in shifts)
            shift.SetColor(hover);
    }

    public void SetShiftColor2(bool hover)
    {
        if (upperState == UpperState.CapsLock)
            return;
        foreach (var shift in shifts)
            shift.SetColor(hover);
    }

    // 숫자/특수문자 패널 토글 버튼
    public void ToggleNumberPanel()
    {
        SetShiftColor(false);
        if (keyboardState == KeyboardState.Language)
        {
            keyboardState = KeyboardState.Number;
            englishPanel.SetActive(false);
            hanglePanel.SetActive(false);
            numberPanel.SetActive(true);
        }
        else
        {
            keyboardState = KeyboardState.Language;
            UpdateLanguagePanel();
        }
    }

    // 한글/영어 스왑 버튼
    public void ToggleLanguagePanel()
    {
        if (keyboardState == KeyboardState.Number)
        {
            keyboardState = KeyboardState.Language;

        }
        else
        {
            if (languageState == LanguageState.English)
                languageState = LanguageState.Hangle;
            else
                languageState = LanguageState.English;
        }

        SetShiftColor(false);
        UpdateLanguagePanel();
    }

    // 패널 상태 갱신
    private void UpdateLanguagePanel()
    {
        foreach(var alphabet in alphabets)
        {
            alphabet.SetLower();
        }
        foreach (var hangle in hangles)
        {
            hangle.SetLower();
        }
        if (keyboardState == KeyboardState.Number)
        {
            englishPanel.SetActive(false);
            hanglePanel.SetActive(false);
            numberPanel.SetActive(true);
        }
        else
        {
            numberPanel.SetActive(false);
            if (languageState == LanguageState.English)
            {
                englishPanel.SetActive(true);
                hanglePanel.SetActive(false);
            }
            else
            {
                englishPanel.SetActive(false);
                hanglePanel.SetActive(true);
            }
        }
    }

    public void SetTarget(GameObject inputField)
    {
        if (inputField == null)
        {
            gameObject.SetActive(false);
            return;
        }
        else
        {
            gameObject.SetActive(true);
            targetInput = inputField.GetComponent<TMP_InputField>();
        }
    }
}
