using DG.Tweening;
using UnityEngine;

/// <summary>
/// VR 미션 리스트(VR_UICanvas/ViewPort/Left_TaskListPanel) 표시/숨김 토글.
///
/// VR 의 '리스트' 버튼은 SlidingPanel_01(TaskProgressUI_VR 아래, 내용이 비어 있는 패널)만 위아래로 움직이고
/// 실제 미션 리스트(TaskListPanel 이 채우는 Left_TaskListPanel)는 건드리지 않아 '움직이기만 하고 사라지지 않는' 현상이 있었다.
/// 이 컴포넌트를 Left_TaskListPanel 에 붙이고 리스트 버튼 onClick 에 <see cref="Toggle"/> 을 연결한다.
///
/// · 숨김은 알파 0 + 레이캐스트/손가락 콜라이더 끄기(오브젝트를 끄면 TaskListPanel 갱신이 멈춰서 SetActive 는 쓰지 않는다).
/// · 다른 스크립트(대화 중 흐림 효과 등)가 알파를 올려도 숨김 상태면 0 으로 되돌린다.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class VrTaskListToggle : MonoBehaviour
{
    [SerializeField] private bool startVisible = true;
    [SerializeField] private float duration = 0.25f;
    [Tooltip("나타날 때 이만큼 위에서 아래로 내려오며 나타난다(UI 단위). 메뉴 바가 위에 있어서 바 쪽에서 펼쳐지는 느낌을 준다.")]
    [SerializeField] private float slideDistance = 120f;

    private RectTransform _rt;
    private Vector2 _basePos;
    private bool _baseSaved;

    private CanvasGroup _group;
    private Collider[] _colliders;
    private bool _shown;

    private void Awake()
    {
        EnsureRefs();
        Apply(startVisible, instant: true);
    }

    private void EnsureRefs()
    {
        if (_group == null) _group = GetComponent<CanvasGroup>();
        if (_colliders == null) _colliders = GetComponentsInChildren<Collider>(true);
        if (_rt == null) _rt = transform as RectTransform;
        if (!_baseSaved && _rt != null) { _basePos = _rt.anchoredPosition; _baseSaved = true; }
    }

    public void Toggle() { EnsureRefs(); Apply(!_shown, instant: false); }
    /// <summary>BottomButtonsPanel.onOpenStateChanged(bool) 에 연결: 열림=표시, 닫힘=완전히 숨김.</summary>
    public void SetShown(bool show) { EnsureRefs(); Apply(show, false); }

    public void Show() { EnsureRefs(); Apply(true, false); }
    public void Hide() { EnsureRefs(); Apply(false, false); }

    private void Apply(bool show, bool instant)
    {
        _shown = show;
        _group.DOKill();
        _group.blocksRaycasts = show;
        _group.interactable = show;
        if (_colliders != null)
            foreach (var c in _colliders) if (c != null) c.enabled = show;

        if (_rt != null) _rt.DOKill();
        Vector2 up = _basePos + Vector2.up * slideDistance;

        if (instant || duration <= 0f)
        {
            _group.alpha = show ? 1f : 0f;
            if (_rt != null) _rt.anchoredPosition = _basePos;
            return;
        }

        _group.DOFade(show ? 1f : 0f, duration);
        if (_rt == null) return;
        if (show)
        {
            // 바 쪽(위)에서 아래로 스르륵 내려오며 나타난다
            if (_group.alpha < 0.05f) _rt.anchoredPosition = up;
            _rt.DOAnchorPos(_basePos, duration * 1.4f).SetEase(Ease.OutCubic);
        }
        else
        {
            // 닫힐 때는 다시 위로 말려 올라가며 사라진다
            _rt.DOAnchorPos(up, duration).SetEase(Ease.InCubic)
               .OnComplete(() => { if (!_shown && _rt != null) _rt.anchoredPosition = _basePos; });
        }
    }

    private void LateUpdate()
    {
        if (_shown || _group == null) return;
        if (_group.alpha > 0f && !DOTween.IsTweening(_group)) _group.alpha = 0f;
    }
}
