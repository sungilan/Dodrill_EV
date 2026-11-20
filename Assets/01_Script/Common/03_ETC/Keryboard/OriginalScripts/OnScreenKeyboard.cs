using System;
using System.Collections;
using System.Text;
using HangulVirtualKeynoard;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class OnScreenKeyboard : MonoBehaviour
{
    public enum CurLang
    {
        KR,
        EN,
    }

    public enum CurType
    {
        Lang,Number
    }

    public enum Caps
    {
        Caps,
        Uncaps,
        Upper
    }

    // private OnScreenKeyboardInputfield currentOskInputfield;
    private OnScreenKeyboardInputfield1 currentOskInputfield;

    public TMP_InputField targetInputField;
    //private InputField targetInputField;
    private string inputtedString, currentString;


    [SerializeField] 
    private CurLang curLang;
    [SerializeField] 
    private Caps curCaps;
    [SerializeField]
    private CurType curType;

    [SerializeField] private Button bgCloseBtn;

    // [SerializeField] private TMP_Text showTextField;
    [SerializeField] private GameObject KrNormal, KrNormalCaps, EnNormal, EnNormalCaps, Number;

    Event fakeEvent;

    [SerializeField]
    Image[] capsImages;
    [SerializeField] private Color unCapsColor;
    [SerializeField] private Color capsColor;


    void Awake()
    {
        AddListenerToButtons();

        curLang = CurLang.KR;
        curCaps = Caps.Uncaps;
        ClearAllStrValue();
        CloseKeyboard();
    }
    void AddListenerToButtons()
    {
    }

    // ShowKeyboard, SetTarget, Initialize, CloseKeyboard, SendKey 변경(로그 + 초기화)
    public void SendKey(string value)
    {
        Debug.Log($"[OSK] SendKey called value='{value}' BEFORE inputtedString='{inputtedString}' currentString='{currentString}'");

        switch (value)
        {
            case "Backspace":
                // 한글 모드: 조합 버퍼(inputtedString)가 있으면 조합 버퍼에서만 삭제,
                // 조합 버퍼가 비어있으면 확정된 currentString에서 삭제.
                if (curLang == CurLang.KR)
                {
                    if (!string.IsNullOrEmpty(inputtedString))
                    {
                        inputtedString = inputtedString.Length > 1
                            ? inputtedString.Substring(0, inputtedString.Length - 1)
                            : "";
                        // 즉시 한글 조합 결과 갱신
                        StartCoroutine(HangulDelayUpdate());
                    }
                    else if (!string.IsNullOrEmpty(currentString))
                    {
                        currentString = currentString.Substring(0, Math.Max(0, currentString.Length - 1));
                        SetInputFieldText(currentString);
                    }
                }
                else // 영어/기타 모드
                {
                    // 확정된 문자열에서 삭제하고 키 이벤트로 처리
                    if (!string.IsNullOrEmpty(currentString))
                        currentString = currentString.Substring(0, Math.Max(0, currentString.Length - 1));

                    fakeEvent = Event.KeyboardEvent("backspace");
                    fakeEvent.keyCode = KeyCode.Backspace;
                    InputFieldProcessUpdate(fakeEvent);
                    // 강제로 라벨 업데이트
                    targetInputField.ForceLabelUpdate();
                }

                Debug.Log($"[OSK] Backspace AFTER inputtedString='{inputtedString}' currentString='{currentString}'");
                return; // 여기서 종료: 더 이상 InputProcess(value) 호출 금지

            // (기존의 다른 케이스들: Space, UP, Language, Number, Enter, default)
            case "Space":
                fakeEvent = Event.KeyboardEvent(value);
                fakeEvent.keyCode = KeyCode.Space;
                InputFieldProcessUpdate(fakeEvent);
                value = " ";
                break;
            case "UP":
                switch (curCaps)
                {
                    case Caps.Uncaps:
                        ChangeKeyboardType(curLang, Caps.Upper);
                        break;
                    case Caps.Upper:
                        ChangeKeyboardType(curLang, Caps.Caps);
                        break;
                    case Caps.Caps:
                        ChangeKeyboardType(curLang, Caps.Uncaps);
                        break;
                }
                return;
            case "Language":
                switch (curLang)
                {
                    case CurLang.EN:
                        ChangeKeyboardType(CurLang.KR, Caps.Uncaps);
                        break;
                    case CurLang.KR:
                        ChangeKeyboardType(CurLang.EN, Caps.Uncaps);
                        break;
                }
                return;
            case "Number":
                ShowNumberPanel();
                return;
            case "Enter":
                gameObject.SetActive(false);
                return;
            default:
                if (value.Length != 1)
                {
                    Debug.LogError("Ignoring spurious multi-character key value: " + value);
                }

                if (!value.Contains("#") && !value.Contains("&"))
                {
                    fakeEvent = Event.KeyboardEvent(value);
                }
                char keyChar = value[0];
                fakeEvent.character = keyChar;
                if (Char.IsUpper(keyChar))
                {
                    fakeEvent.modifiers |= EventModifiers.Shift;
                }

                if (curCaps == Caps.Upper)
                    ChangeKeyboardType(curLang, Caps.Uncaps);
                break;
        }

        // 일반 문자 처리: value를 내부 버퍼에 추가하고 화면 갱신
        InputProcess(value);
        Debug.Log($"[OSK] SendKey AFTER inputtedString='{inputtedString}' currentString='{currentString}'");
    }

    // public void ShowKeyboard(TMP_InputField inputField, OnScreenKeyboardInputfield oskInputField)
    public void ShowKeyboard(TMP_InputField inputField, OnScreenKeyboardInputfield1 oskInputField)
    {
        Debug.Log($"[OSK] ShowKeyboard called. inputField={inputField?.name} restoreFromOsk={(oskInputField != null)}");
        Initialize(inputField, oskInputField);
        // 초기화 후 상태 확인 로그
        Debug.Log($"[OSK] After Initialize inputtedString='{inputtedString}' currentString='{currentString}'");
        // InputProcess() 대신 초기화 테스트 시 주석 처리 가능
        InputProcess();
        gameObject.SetActive(true);
    }

    public void CloseKeyboard()
    {
        // 필요 시 조합 상태만 저장하거나 저장하지 않음.
        // 현재는 기존 동작 유지(조합 버퍼 저장). 원치 않으면 아래 줄 주석 처리하세요.
        currentOskInputfield?.SaveInputedString(inputtedString);
        Debug.Log($"[OSK] CloseKeyboard saving inputtedString='{inputtedString}'");
        gameObject.SetActive(false);
    }

    private void InputProcess(string value)
    {
        inputtedString += value;
        currentString += value;

        InputProcess();
    }

    private void InputProcess()
    {
        InputFieldProcessUpdate(fakeEvent);

        if (curLang == CurLang.KR)
        {
            StartCoroutine(HangulDelayUpdate());
        }
        else
        {
            targetInputField.text = currentString;
            currentString = GetInputFieldText();
        }
    }

    // private void Initialize(TMP_InputField inputField, OnScreenKeyboardInputfield oskInputField)
    private void Initialize(TMP_InputField inputField, OnScreenKeyboardInputfield1 oskInputField)
    {
        targetInputField = inputField;
        currentOskInputfield = oskInputField;

        // 이전에 입력된 필드 텍스트는 보존
        currentString = targetInputField != null ? targetInputField.text : "";

        // 조합 버퍼만 초기화 (직전 키보드의 미완성 조합은 제거)
        inputtedString = "";

        // 만약 '직전에 사용하던 키보드 조합 상태'를 복원하고 싶다면 아래 라인을 사용하세요 (기본은 주석)
        // if (currentOskInputfield != null && !string.IsNullOrEmpty(currentOskInputfield.inputtedString))
        // {
        //     inputtedString = currentOskInputfield.inputtedString;
        // }

        Debug.Log($"[OSK] Initialize target={inputField?.name} restoredText='{currentString}' inputtedString cleared");

        //Set default state
        if (string.IsNullOrEmpty(targetInputField.text))
        {
            curCaps = Caps.Uncaps;
            curLang = CurLang.KR;
        }

        ChangeKeyboardType(curLang, curCaps);
    }

    void ShowNumberPanel() 
    {
        KrNormal.gameObject.SetActive(false);
        KrNormalCaps.gameObject.SetActive(false);
        EnNormal.gameObject.SetActive(false);
        EnNormalCaps.gameObject.SetActive(false);
        Number.gameObject.SetActive(true);
    }

    void ChangeKeyboardType(CurLang curLang, Caps caps)
    {
        KrNormal.gameObject.SetActive(false);
        KrNormalCaps.gameObject.SetActive(false);
        EnNormal.gameObject.SetActive(false);
        EnNormalCaps.gameObject.SetActive(false);
        Number.gameObject.SetActive(false);


        this.curLang = curLang;
        this.curCaps = caps;
        if (caps == Caps.Uncaps)
            foreach (var cap in capsImages)
                cap.color = unCapsColor;
        else
            foreach (var cap in capsImages)
                cap.color = capsColor;

        switch (curLang)
        {
            case CurLang.KR:
                switch (caps)
                {
                    case Caps.Caps:
                        KrNormalCaps.gameObject.SetActive(true);
                        break;
                    case Caps.Uncaps:
                        KrNormal.gameObject.SetActive(true);
                        break;
                    case Caps.Upper:
                        KrNormalCaps.gameObject.SetActive(true);
                        break;
                }
                break;
            case CurLang.EN:
                switch (caps)
                {
                    case Caps.Caps:
                        EnNormalCaps.gameObject.SetActive(true);
                        break;
                    case Caps.Uncaps:
                        EnNormal.gameObject.SetActive(true);
                        break;
                    case Caps.Upper:
                        EnNormalCaps.gameObject.SetActive(true);
                        break;
                }
                break;
        }

    }

    private void SetInputFieldText(string str)
    {
        if (targetInputField)
            targetInputField.text = str;
    }

    private string GetInputFieldText()
    {
        if (targetInputField)
            return targetInputField.text;
        else
            return "";
    }

    private void InputFieldProcessUpdate(Event fakeEvent)
    {
        if (targetInputField)
        {
            if (fakeEvent != null)
                targetInputField.ProcessEvent(fakeEvent);
            targetInputField.ForceLabelUpdate();
        }
    }

    IEnumerator HangulDelayUpdate()
    {
        HangulHelper helper = new HangulHelper();
        StringBuilder stringBuilder = new StringBuilder();

        for (int i = 0; i < inputtedString.Length; i++)
        {
            char c = inputtedString[i];

            // 한글 자모 범위면 조합기에 넣기
            if ((c >= 0x1100 && c <= 0x11FF)   // 초성/자모
             || (c >= 0x3130 && c <= 0x318F)   // 호환 자모
             || (c >= 0xAC00 && c <= 0xD7AF))  // 완성형 한글
            {
                helper.Input(stringBuilder, c);
            }
            else
            {
                // 영어, 숫자, 기호 등은 그대로 붙임
                stringBuilder.Append(c);
            }
        }

        SetInputFieldText(stringBuilder.ToString());

        yield return null;
        currentString = GetInputFieldText();
    }

    public void SetTarget(TMP_InputField inputField)
    {
        if (inputField != null)
        {
            // SetTarget 경로로도 키보드를 띄울 때 기존 텍스트를 지우지 않음
            // 단지 키보드 내부 조합 상태만 초기화
            targetInputField = inputField;
            currentString = targetInputField.text;
            inputtedString = "";
            gameObject.SetActive(true);
            enabled = true;
            Debug.Log($"[OSK] SetTarget called target={inputField.name} preservedText='{currentString}'");
        }
    }

    private void ClearAllStrValue()
    {
        // 내부 조합 버퍼만 초기화. targetInputField.text는 보존.
        inputtedString = "";
        currentString = targetInputField != null ? targetInputField.text : "";

        // 이전 구현은 targetInputField.text를 지웠음 — 이제는 지우지 않음.
        // if (targetInputField)
        //     targetInputField.text = "";
    }
}