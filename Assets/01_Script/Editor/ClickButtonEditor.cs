using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Sirenix.OdinInspector;

public class ClickButton : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler
{
    [Header("이미지")]
    [SerializeField] private Image buttonImage;
    [SerializeField] private Sprite normalSprite;
    [SerializeField] private Sprite pressedSprite;

    [Header("텍스트")]
    [SerializeField] private TMP_Text buttonText;
    [SerializeField] private TMP_FontAsset normalFont;
    [SerializeField] private TMP_FontAsset pressedFont;

    private Color _normalTextColor;
    private Color _pressedTextColor;

    void Awake()
    {
        if (buttonImage == null)
            buttonImage = GetComponent<Image>();

        if (buttonText == null)
            buttonText = GetComponentInChildren<TMP_Text>();

        ColorUtility.TryParseHtmlString("#002371", out _normalTextColor);
        ColorUtility.TryParseHtmlString("#FFFFFF", out _pressedTextColor);
    }

    [Button("⚙️ 자동 설정 적용", ButtonSizes.Large)]
    private void AutoSetup()
    {
        // Button Transition → None
        var btn = GetComponent<Button>();
        if (btn != null)
            btn.transition = Selectable.Transition.None;

        // Image 자동 할당
        if (buttonImage == null)
            buttonImage = GetComponent<Image>();

        // Text 자동 할당
        if (buttonText == null)
            buttonText = GetComponentInChildren<TMP_Text>();

        // 텍스트 색상 → #002371
        if (buttonText != null)
        {
            ColorUtility.TryParseHtmlString("#002371", out Color normalColor);
            buttonText.color = normalColor;
            buttonText.fontStyle = FontStyles.Normal;
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(gameObject);
        Debug.Log("✅ ClickButton 자동 설정 완료");
#endif
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (buttonImage != null && pressedSprite != null) buttonImage.sprite = pressedSprite;
        if (buttonText != null)
        {
            buttonText.color = _pressedTextColor;
            if (pressedFont != null) buttonText.font = pressedFont;
            buttonText.fontStyle = FontStyles.Bold;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (buttonImage != null && normalSprite != null) buttonImage.sprite = normalSprite;
        if (buttonText != null)
        {
            buttonText.color = _normalTextColor;
            if (normalFont != null) buttonText.font = normalFont;
            buttonText.fontStyle = FontStyles.Normal;
        }
    }
}