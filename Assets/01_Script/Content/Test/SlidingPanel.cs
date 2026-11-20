using UnityEngine;
using DG.Tweening;

[RequireComponent(typeof(CanvasGroup))]
public class SlidingPanel : MonoBehaviour
{
    private RectTransform _rect;
    private CanvasGroup _canvasGroup;

    [Header("공통 설정")]
    public float duration = 0.4f;
    public Ease showEase = Ease.OutBack;
    public Ease hideEase = Ease.InQuad;

    [Header("PC/Mobile (Screen Space Overlay) 설정")]
    public float hiddenPosX = -1100f;
    public float visiblePosX = 0f;

    [Header("VR (World Space / 부모 기준) 설정")]
    [Tooltip("VR에서 이 패널의 기준이 될 부모 패널")]
    public Transform vrParentTransform;
    [Tooltip("VR 숨김 위치 (부모 기준 로컬 좌표)")]
    public Vector2 vrHiddenPos = new Vector2(0, -500f);
    [Tooltip("VR 노출 위치 (부모 기준 로컬 좌표)")]
    public Vector2 vrVisiblePos = Vector2.zero;

    private bool _isShown = false;
    public bool startVisible = true;
    // 기존 설정된 플랫폼 매니저의 IsVR을 참조하세요.
    private bool _isVR => GameScenePlatformManager.IsVR;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _canvasGroup = GetComponent<CanvasGroup>();

        InitState();
    }

    

    private void InitState()
    {
        _isShown = startVisible;
        _canvasGroup.alpha = startVisible ? 1f : 0f;
        _canvasGroup.blocksRaycasts = startVisible;
        _canvasGroup.interactable = startVisible;

        if(_isVR)
        {
            if(vrParentTransform != null) transform.SetParent(vrParentTransform, false);
            _rect.anchoredPosition = startVisible ? vrVisiblePos : vrHiddenPos;
        }
        else
        {
            float targetX = startVisible ? visiblePosX : hiddenPosX;
            _rect.anchoredPosition = new Vector2(targetX, _rect.anchoredPosition.y);
        }
    }

    // 숨김 상태인데 다른 스크립트(예: TaskListPanel 의 대화 중 흐림 효과 — 같은 CanvasGroup 의 alpha 를 1 로 되돌린다)가
    // 알파를 올려 'VR 에서 내려 둔 미션 리스트가 계속 보이던' 문제 방지: 숨김 중에는 알파 0 을 유지한다.
    private void LateUpdate()
    {
        if(_isShown || _canvasGroup == null) return;
        if(_canvasGroup.alpha > 0f && !DOTween.IsTweening(_canvasGroup))
            _canvasGroup.alpha = 0f;
    }

    // Awake 전에(비활성 오브젝트였다가) 버튼 이벤트로 먼저 호출되면 _canvasGroup 이 null 이라 NRE 가 났다 → 필요할 때 보충한다.
    private bool EnsureRefs()
    {
        if(_rect == null) _rect = GetComponent<RectTransform>();
        if(_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
        return _rect != null && _canvasGroup != null;
    }

    public void TogglePanel()
    {
        if(!EnsureRefs()) return;
        if(_isShown) Hide();
        else Show();
    }

    public void Show()
    {
        if(!EnsureRefs()) return;
        _isShown = true;
        KillTweens();

        _canvasGroup.blocksRaycasts = true;
        _canvasGroup.interactable = true;

        if(_isVR)
        {
            // VR: 부모 내 로컬 슬라이딩
            _rect.DOAnchorPos(vrVisiblePos, duration).SetEase(showEase);
            _canvasGroup.DOFade(1f, duration);
        }
        else
        {
            // PC: 기존 X축 슬라이딩
            _rect.DOAnchorPosX(visiblePosX, duration).SetEase(showEase);
            _canvasGroup.DOFade(1f, duration);
        }

        Log("패널 표시");
    }

    public void Hide()
    {
        if(!EnsureRefs()) return;
        _isShown = false;
        KillTweens();

        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;

        if(_isVR)
        {
            // VR: 부모 내 로컬 숨김
            _rect.DOAnchorPos(vrHiddenPos, duration).SetEase(hideEase);
            _canvasGroup.DOFade(0f, duration);
        }
        else
        {
            // PC: 기존 X축 숨김
            _rect.DOAnchorPosX(hiddenPosX, duration).SetEase(hideEase);
            _canvasGroup.DOFade(0f, duration);
        }

        Log("패널 숨김");
    }

    private void KillTweens()
    {
        _rect.DOKill();
        _canvasGroup.DOKill();
    }

    private void Log(string msg) => Debug.Log($"[SlidingPanel] {msg}");
}