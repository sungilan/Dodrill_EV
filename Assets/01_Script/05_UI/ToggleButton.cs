using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Sirenix.OdinInspector;

public class ToggleButton : MonoBehaviour, IPointerClickHandler
{
    [Tooltip("켜면 포인터 클릭으로 내부 상태를 바꾸지 않습니다. Button·BottomButtonsPanel 등에서 SetSelected로만 맞출 때 사용.")]
    [SerializeField] private bool ignorePointerClick;

    /// <summary>외부(SetSelected 등)만으로 시각을 맞출 때 — VR 핑거 피드백에서 토글 스프라이트를 건너뜁니다.</summary>
    public bool UsesExternalVisualOnly => ignorePointerClick;

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

    /// <summary>
    /// VR 손가락 + Button.onClick 조합용 — <see cref="OnPointerClick"/>과 동일하게 시각만 토글합니다.
    /// (<see cref="UsesExternalVisualOnly"/>가 true면 호출하지 마세요.)
    /// </summary>
    public void FingerTapToggleVisual()
    {
        _isSelected = !_isSelected;
        UpdateVisual();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (ignorePointerClick)
            return;

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