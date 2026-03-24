using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Sirenix.OdinInspector;

public class CustomInputField : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    [Header("배경 이미지")]
    [SerializeField] private Image bgImage;
    [SerializeField] private Sprite bgOff;
    [SerializeField] private Sprite bgOn;

    [Header("인풋 필드")]
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private TMP_FontAsset offFont;   // Pretendard Medium
    [SerializeField] private TMP_FontAsset onFont;    // Pretendard SemiBold

    private Color _offColor;
    private Color _onColor;

    void Awake()
    {
        if (bgImage == null)
            bgImage = GetComponent<Image>();

        if (inputField == null)
            inputField = GetComponent<TMP_InputField>();

        ColorUtility.TryParseHtmlString("#9A9A9A", out _offColor);
        ColorUtility.TryParseHtmlString("#000000", out _onColor);

        inputField.onValueChanged.AddListener(OnValueChanged);

        // 초기 상태 적용
        ApplyOffState();
    }

    [Button("⚙️ 자동 설정 적용", ButtonSizes.Large)]
    private void AutoSetup()
    {
        if (inputField == null)
            inputField = GetComponent<TMP_InputField>();

        if (bgImage == null)
            bgImage = GetComponent<Image>();

        // Transition → None
        inputField.transition = Selectable.Transition.None;

        // 폰트 사이즈 40
        if (inputField.textComponent != null)
            inputField.textComponent.fontSize = 40;

        // Placeholder 색상 → #9A9A9A
        var placeholder = inputField.placeholder?.GetComponent<TMP_Text>();
        if (placeholder != null)
        {
            ColorUtility.TryParseHtmlString("#9A9A9A", out Color placeholderColor);
            placeholder.color = placeholderColor;
            placeholder.fontSize = 40;
            if (offFont != null) placeholder.font = offFont;
        }

        // 초기 배경 이미지 → bgOff
        if (bgImage != null && bgOff != null)
            bgImage.sprite = bgOff;

        // 초기 텍스트 색상 → #9A9A9A
        if (inputField.textComponent != null)
        {
            ColorUtility.TryParseHtmlString("#9A9A9A", out Color offColor);
            inputField.textComponent.color = offColor;
            if (offFont != null) inputField.textComponent.font = offFont;
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(gameObject);
        Debug.Log("✅ CustomInputField 자동 설정 완료");
#endif
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (bgImage != null && bgOn != null)
            bgImage.sprite = bgOn;
    }

    public void OnDeselect(BaseEventData eventData)
    {
        if (bgImage != null && bgOff != null)
            bgImage.sprite = bgOff;

        if (inputField != null && string.IsNullOrEmpty(inputField.text))
            ApplyOffState();
    }

    private void OnValueChanged(string value)
    {
        if (inputField.textComponent == null) return;

        if (!string.IsNullOrEmpty(value))
            ApplyOnState();
        else
            ApplyOffState();
    }

    private void ApplyOnState()
    {
        if (inputField.textComponent == null) return;
        ColorUtility.TryParseHtmlString("#000000", out Color onColor);
        inputField.textComponent.color = onColor;
        if (onFont != null) inputField.textComponent.font = onFont;
    }

    private void ApplyOffState()
    {
        if (inputField.textComponent == null) return;
        ColorUtility.TryParseHtmlString("#9A9A9A", out Color offColor);
        inputField.textComponent.color = offColor;
        if (offFont != null) inputField.textComponent.font = offFont;
    }
}