using UnityEngine;
using DG.Tweening;

/// <summary>
/// PC/Mobile (Screen Space Overlay) + VR (World Space) 모두 지원하는 슬라이딩 패널.
/// VR에서는 버튼 위에 떠오르며, PC에서는 화면 옆에서 슬라이드됨.
/// </summary>
public class SlidingPanel : MonoBehaviour
{
    private RectTransform _rect;
    private CanvasGroup _canvasGroup;
    private Canvas _canvas;

    [Header("공통 설정")]
    public float duration = 0.4f;
    public Ease showEase = Ease.OutBack;
    public Ease hideEase = Ease.InQuad;

    [Header("PC/Mobile (Screen Space Overlay) 설정")]
    public float hiddenPosX = -1100f;
    public float visiblePosX = 0f;

    [Header("VR (World Space) 설정")]
    [Tooltip("VR에서 버튼 위에 떠오를 때 사용")]
    public Transform targetButtonTransform;  // ★ 버튼 Transform
    [Tooltip("버튼으로부터의 오프셋 (위쪽)")]
    public Vector3 offsetAboveButton = new Vector3(0, 0.3f, 0);
    [Tooltip("VR에서 사용할 월드 스페이스 크기")]
    public float vrPanelScale = 0.001f;  // 1mm = 1 UI unit

    private bool _isShown = false;
    private bool _isVR => GameScenePlatformManager.IsVR;
    private Tweener _positionTweener;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();
        _canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

        InitState();
    }

    private void InitState()
    {
        if(_isVR)
        {
            // VR: 스케일 0에서 시작
            transform.localScale = Vector3.zero;
            _canvasGroup.alpha = 0f;
        }
        else
        {
            // PC/Mobile: 왼쪽 밖에서 시작
            _rect.anchoredPosition = new Vector2(hiddenPosX, _rect.anchoredPosition.y);
            _canvasGroup.alpha = 0f;
        }
        _isShown = false;
        gameObject.SetActive(false);
    }

    public void TogglePanel()
    {
        if(_isShown) Hide();
        else Show();
    }

    /// <summary>
    /// 패널 표시 (버튼 또는 특정 위치 위에)
    /// </summary>
    public void Show()
    {
        gameObject.SetActive(true);
        _isShown = true;
        DOTween.Kill(_rect);
        DOTween.Kill(_canvasGroup);
        DOTween.Kill(_positionTweener);

        if(_isVR)
        {
            ShowInVR();
        }
        else
        {
            ShowInPC();
        }
    }

    /// <summary>VR: 버튼 위에 떠오르는 연출</summary>
    private void ShowInVR()
    {
        // ★ 목표 위치: 버튼 위 + 오프셋
        Vector3 targetWorldPos = GetTargetWorldPosition();

        // ★ 1단계: 위치 즉시 설정
        transform.position = targetWorldPos;

        // ★ 2단계: 스케일 애니메이션 (0 → vrPanelScale)
        transform.localScale = Vector3.zero;
        transform.DOScale(vrPanelScale, duration).SetEase(showEase);

        // ★ 3단계: 페이드인
        _canvasGroup.alpha = 0f;
        _canvasGroup.DOFade(1f, duration);

        // ★ 4단계: 매 프레임 위치 추적 (버튼이 움직일 수 있으므로)
        _positionTweener = DOVirtual.Float(0, 1, duration, _ =>
        {
            if(_isShown && targetButtonTransform != null)
                transform.position = GetTargetWorldPosition();
        });

        Log($"[VR Show] 버튼 위에 나타남 @ {targetWorldPos}");
    }

    /// <summary>PC/Mobile: 왼쪽에서 슬라이딩</summary>
    private void ShowInPC()
    {
        _rect.anchoredPosition = new Vector2(hiddenPosX, _rect.anchoredPosition.y);
        _canvasGroup.alpha = 0f;

        _rect.DOAnchorPosX(visiblePosX, duration).SetEase(showEase);
        _canvasGroup.DOFade(1f, duration);

        Log($"[PC Show] 슬라이드 애니메이션 시작");
    }

    /// <summary>패널 숨김</summary>
    public void Hide()
    {
        _isShown = false;
        DOTween.Kill(_rect);
        DOTween.Kill(_canvasGroup);
        DOTween.Kill(_positionTweener);

        if(_isVR)
        {
            HideInVR();
        }
        else
        {
            HideInPC();
        }
    }

    /// <summary>VR: 축소되며 사라짐</summary>
    private void HideInVR()
    {
        transform.DOScale(0f, duration).SetEase(hideEase);
        _canvasGroup.DOFade(0f, duration).OnComplete(() => gameObject.SetActive(false));

        Log("[VR Hide] 축소되며 사라짐");
    }

    /// <summary>PC/Mobile: 왼쪽으로 슬라이드되며 사라짐</summary>
    private void HideInPC()
    {
        _rect.DOAnchorPosX(hiddenPosX, duration).SetEase(hideEase);
        _canvasGroup.DOFade(0f, duration).OnComplete(() => gameObject.SetActive(false));

        Log("[PC Hide] 슬라이드 애니메이션");
    }

    /// <summary>
    /// ★ 핵심: 버튼 위의 월드 좌표 계산
    /// Canvas가 World Space일 때 정확한 위치를 반환합니다.
    /// </summary>
    private Vector3 GetTargetWorldPosition()
    {
        if(targetButtonTransform == null)
        {
            Log("[Error] targetButtonTransform이 설정되지 않았습니다!");
            return transform.position;
        }

        // ★ 버튼 위치 + 오프셋
        Vector3 buttonWorldPos = targetButtonTransform.position;
        Vector3 targetPos = buttonWorldPos + offsetAboveButton;

        return targetPos;
    }

    /// <summary>
    /// 외부에서 버튼을 지정하고 Show 호출
    /// </summary>
    public void ShowAboveButton(Transform buttonTransform)
    {
        targetButtonTransform = buttonTransform;
        Show();
    }

    /// <summary>
    /// 특정 월드 좌표에 표시 (버튼이 없을 때)
    /// </summary>
    public void ShowAtPosition(Vector3 worldPosition)
    {
        targetButtonTransform = null;
        gameObject.SetActive(true);
        _isShown = true;

        DOTween.Kill(_rect);
        DOTween.Kill(_canvasGroup);

        if(_isVR)
        {
            transform.position = worldPosition;
            transform.localScale = Vector3.zero;
            transform.DOScale(vrPanelScale, duration).SetEase(showEase);
            _canvasGroup.alpha = 0f;
            _canvasGroup.DOFade(1f, duration);
        }
        else
        {
            _rect.anchoredPosition = new Vector2(hiddenPosX, _rect.anchoredPosition.y);
            _rect.DOAnchorPosX(visiblePosX, duration).SetEase(showEase);
            _canvasGroup.alpha = 0f;
            _canvasGroup.DOFade(1f, duration);
        }
    }

    private void Log(string msg)
    {
        Debug.Log($"[SlidingPanel] {msg}");
    }
}
