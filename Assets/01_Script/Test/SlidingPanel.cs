using UnityEngine;
using DG.Tweening;

public class SlidingPanel : MonoBehaviour
{
    private RectTransform _rect;
    private CanvasGroup _canvasGroup;

    [Header("공통 설정")]
    public float duration = 0.4f;
    public Ease showEase = Ease.OutBack;
    public Ease hideEase = Ease.InQuad;

    [Header("PC/Mobile (Overlay) 설정")]
    public float hiddenPosX = -1100f; // 화면 왼쪽 밖
    public float visiblePosX = 0f;    // 화면 중앙(앵커 기준)

    [Header("VR (World Space) 설정")]
    [Tooltip("VR에서는 위치 이동보다 크기(Scale) 변화가 더 자연스럽습니다.")]
    public bool useScaleInVR = true;

    private bool _isShown = false;
    private bool _isVR => GameScenePlatformManager.IsVR;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

        // 초기 상태: 숨김
        InitState();
    }

    private void InitState()
    {
        if(_isVR)
        {
            if(useScaleInVR) transform.localScale = Vector3.zero;
            _canvasGroup.alpha = 0f;
        }
        else
        {
            _rect.anchoredPosition = new Vector2(hiddenPosX, _rect.anchoredPosition.y);
        }
        _isShown = false;
        gameObject.SetActive(false);
    }

    public void TogglePanel()
    {
        if(_isShown) Hide();
        else Show();
    }

    public void Show()
    {
        gameObject.SetActive(true);
        _isShown = true;
        DOTween.Kill(_rect);
        DOTween.Kill(_canvasGroup);

        if(_isVR)
        {
            // VR 연출: 플레이어 정면으로 재배치 후 Scale/Fade-In
            GameScenePlatformManager.Instance.ForcePositionInFront(GetComponentInParent<Canvas>());

            if(useScaleInVR)
            {
                // PlatformManager에서 설정한 vrCanvasScale을 목표값으로 사용
                float targetScale = GameScenePlatformManager.Instance.vrCanvasScale;
                transform.DOScale(targetScale, duration).SetEase(showEase);
            }
            _canvasGroup.DOFade(1f, duration);
        }
        else
        {
            // PC/Mobile 연출: 왼쪽에서 슬라이딩
            _rect.DOAnchorPosX(visiblePosX, duration).SetEase(showEase);
            _canvasGroup.DOFade(1f, duration);
        }
    }

    public void Hide()
    {
        _isShown = false;
        DOTween.Kill(_rect);
        DOTween.Kill(_canvasGroup);

        if(_isVR)
        {
            if(useScaleInVR) transform.DOScale(0f, duration).SetEase(hideEase);
            _canvasGroup.DOFade(0f, duration).OnComplete(() => gameObject.SetActive(false));
        }
        else
        {
            _rect.DOAnchorPosX(hiddenPosX, duration).SetEase(hideEase);
            _canvasGroup.DOFade(0f, duration).OnComplete(() => gameObject.SetActive(false));
        }
    }
}