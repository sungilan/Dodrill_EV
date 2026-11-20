using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(TMP_Dropdown))]
public class ClickDropdown : MonoBehaviour
{
    [Header("배경 이미지")]
    [SerializeField] private Image buttonImage;
    [SerializeField] private Sprite normalSprite;
    [SerializeField] private Sprite openedSprite;

    [Header("텍스트")]
    [SerializeField] private TMP_Text captionText;

    private TMP_Dropdown _dropdown;
    private Color _normalColor;
    private Color _openedColor;
    private bool _wasOpen = false;

    protected void Awake()
    {
        _dropdown = GetComponent<TMP_Dropdown>();
        UnityEngine.ColorUtility.TryParseHtmlString("#002371", out _normalColor);
        UnityEngine.ColorUtility.TryParseHtmlString("#FFFFFF", out _openedColor);
    }

    void LateUpdate()
    {
        // 실제 생성된 드랍다운 목록 오브젝트 감지
        bool isOpen = transform.Find("Dropdown List") != null;

        if (isOpen != _wasOpen)
        {
            _wasOpen = isOpen;
            SetOpenState(isOpen);
        }
    }

    private void SetOpenState(bool isOpen)
    {
        if (buttonImage != null)
            buttonImage.sprite = isOpen ? openedSprite : normalSprite;

        if (captionText != null)
            captionText.color = isOpen ? _openedColor : _normalColor;
    }
}