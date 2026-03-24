using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Sirenix.OdinInspector;
using UnityEngine.Localization.Settings;

public class ClickButton : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    [Header("배경 이미지")]
    [SerializeField] private Image buttonImage;
    [SerializeField] private Sprite normalSprite;
    [SerializeField] private Sprite pressedSprite;

    [Header("아이콘 이미지")]
    [SerializeField] private Image iconImage;
    [SerializeField] private Sprite normalIcon;
    [SerializeField] private Sprite pressedIcon;
    [SerializeField] private bool changeIconColor = true;

    [Header("텍스트")]
    [SerializeField] private TMP_Text buttonText;
    [SerializeField] private TMP_FontAsset normalFont;
    [SerializeField] private TMP_FontAsset pressedFont;

    private Color _normalColor;
    private Color _pressedColor;
    private Material _normalMaterial;
    private Material _pressedMaterial;

    void Awake()
    {
        InitializeReferences();

        ColorUtility.TryParseHtmlString("#002371", out _normalColor);
        ColorUtility.TryParseHtmlString("#FFFFFF", out _pressedColor);

        if (buttonText != null)
        {
            // normalFont 기반 머티리얼 복사
            if (normalFont != null) buttonText.font = normalFont;
            _normalMaterial = new Material(buttonText.fontMaterial);
            _normalMaterial.EnableKeyword("UNDERLAY_ON");
            _normalMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0, 0, 0, 0.5f));
            _normalMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 2f);
            _normalMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -2f);
            _normalMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0f);

            // pressedFont 기반 머티리얼 복사
            if (pressedFont != null)
            {
                buttonText.font = pressedFont;
                _pressedMaterial = new Material(buttonText.fontMaterial);
                _pressedMaterial.EnableKeyword("UNDERLAY_ON");
                _pressedMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0, 0, 0, 0.5f));
                _pressedMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 2f);
                _pressedMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -2f);
                _pressedMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0f);

                // 다시 normalFont로 복구
                buttonText.font = normalFont;
            }
            else
            {
                _pressedMaterial = new Material(_normalMaterial);
            }

            // 최종 normalMaterial 세팅
            buttonText.fontMaterial = _normalMaterial;
        }
    }

    void OnEnable()
    {
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    }

    void OnDisable()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
    }

    private void OnLocaleChanged(UnityEngine.Localization.Locale locale)
    {
        StartCoroutine(ReapplyShadowNextFrame());
    }

    private System.Collections.IEnumerator ReapplyShadowNextFrame()
    {
        yield return null;
        if (buttonText != null)
            buttonText.fontMaterial = _normalMaterial;
    }

    private void InitializeReferences()
    {
        if (buttonImage == null) buttonImage = GetComponent<Image>();
        if (buttonText == null) buttonText = GetComponentInChildren<TMP_Text>();
        if (iconImage == null)
        {
            var images = GetComponentsInChildren<Image>();
            foreach (var img in images)
            {
                if (img != buttonImage)
                {
                    iconImage = img;
                    break;
                }
            }
        }
    }

    [Button("⚙️ 자동 설정 적용", ButtonSizes.Large)]
    private void AutoSetup()
    {
        var btn = GetComponent<Button>();
        if (btn != null)
            btn.transition = Selectable.Transition.None;

        InitializeReferences();

        if (buttonText != null)
        {
            ColorUtility.TryParseHtmlString("#002371", out Color normalColor);
            buttonText.color = normalColor;
            buttonText.fontStyle = FontStyles.Normal;
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(gameObject);
        Debug.Log("✅ ClickButton & Icon 자동 설정 완료");
#endif
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        OnPointerDown(eventData);
        OnPointerUp(eventData);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (buttonImage != null && pressedSprite != null)
            buttonImage.sprite = pressedSprite;

        if (iconImage != null)
        {
            if (pressedIcon != null) iconImage.sprite = pressedIcon;
            if (changeIconColor) iconImage.color = _pressedColor;
        }

        if (buttonText != null)
        {
            buttonText.color = _pressedColor;
            if (pressedFont != null) buttonText.font = pressedFont;
            buttonText.fontMaterial = _pressedMaterial;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (buttonImage != null && normalSprite != null)
            buttonImage.sprite = normalSprite;

        if (iconImage != null)
        {
            if (normalIcon != null) iconImage.sprite = normalIcon;
            if (changeIconColor) iconImage.color = _normalColor;
        }

        if (buttonText != null)
        {
            buttonText.color = _normalColor;
            if (normalFont != null) buttonText.font = normalFont;
            buttonText.fontStyle = FontStyles.Normal;
            buttonText.fontMaterial = _normalMaterial;
        }
    }
}