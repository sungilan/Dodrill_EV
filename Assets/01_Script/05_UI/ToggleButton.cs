using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Sirenix.OdinInspector;

public class ToggleButton : MonoBehaviour, IPointerClickHandler
{
    [Header("배경 이미지")]
    [SerializeField] private Image bgImage;
    [SerializeField] private Sprite bgNormal;
    [SerializeField] private Sprite bgSelected;

    [Header("아이콘 이미지")]
    [SerializeField] private Image iconImage;
    [SerializeField] private Sprite iconNormal;
    [SerializeField] private Sprite iconSelected;

    private bool _isSelected = true;

    void Awake()
    {
        if (bgImage == null)
            bgImage = GetComponent<Image>();
    }

    [Button("⚙️ 자동 설정 적용", ButtonSizes.Large)]
    private void AutoSetup()
    {
        var btn = GetComponent<Button>();
        if (btn != null)
            btn.transition = Selectable.Transition.None;

        if (bgImage == null)
            bgImage = GetComponent<Image>();

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(gameObject);
        Debug.Log("✅ ToggleButton 자동 설정 완료");
#endif
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        UpdateVisual();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        _isSelected = !_isSelected;
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (bgImage != null && bgNormal != null && bgSelected != null)
            bgImage.sprite = _isSelected ? bgSelected : bgNormal;

        if (iconImage != null && iconNormal != null && iconSelected != null)
            iconImage.sprite = _isSelected ? iconSelected : iconNormal;
    }
}