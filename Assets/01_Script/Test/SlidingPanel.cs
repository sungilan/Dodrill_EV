using UnityEngine;
using DG.Tweening;

/// <summary>
/// PC/Mobile (Screen Space Overlay) + VR (World Space) 모두 지원하는 슬라이딩 패널.
/// 버튼이 자식으로 포함되어도 버튼은 항상 보이도록 설계됨.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class SlidingPanel : MonoBehaviour
{
    private RectTransform _rect;
    private CanvasGroup _canvasGroup;

    [Header("공통 설정")]
    public float duration = 0.4f;
    public Ease showEase = Ease.OutBack;
    public Ease hideEase = Ease.InQuad;

    [Header("PC/Mobile 설정")]
    public float hiddenPosX = -1100f;
    public float visiblePosX = 0f;

    [Header("VR (World Space) 설정")]
    public Transform targetButtonTransform;
    public Vector3 offsetAboveButton = new Vector3(0, 0.3f, 0);
    public float vrPanelScale = 0.001f;

    private bool _isShown = false;
    // 실제 환경에 맞게 GameScenePlatformManager.IsVR 등으로 수정해서 사용하세요.
    private bool _isVR => false;
    private Tweener _positionTweener;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _canvasGroup = GetComponent<CanvasGroup>();

        InitState();
    }

    private void InitState()
    {
        // 핵심: SetActive(false)를 하지 않습니다. 
        // 대신 알파를 0으로 만들고 상호작용만 끕니다.
        _isShown = false;
        _canvasGroup.alpha = 0f;
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;

        if(_isVR)
        {
            transform.localScale = Vector3.zero;
        }
        else
        {
            _rect.anchoredPosition = new Vector2(hiddenPosX, _rect.anchoredPosition.y);
        }
    }

    public void TogglePanel()
    {
        if(_isShown) Hide();
        else Show();
    }

    public void Show()
    {
        _isShown = true;
        KillTweens();

        // 패널 상호작용 활성화
        _canvasGroup.blocksRaycasts = true;
        _canvasGroup.interactable = true;

        if(_isVR) ShowInVR();
        else ShowInPC();

        Log("패널 표시");
    }

    private void ShowInVR()
    {
        transform.position = GetTargetWorldPosition();

        // 스케일과 알파 동시 진행
        transform.DOScale(vrPanelScale, duration).SetEase(showEase);
        _canvasGroup.DOFade(1f, duration);

        _positionTweener = DOVirtual.Float(0, 1, duration, _ =>
        {
            if(_isShown && targetButtonTransform != null)
                transform.position = GetTargetWorldPosition();
        }).SetLoops(-1); // 패널이 켜져 있는 동안 계속 추적
    }

    private void ShowInPC()
    {
        _rect.DOAnchorPosX(visiblePosX, duration).SetEase(showEase);
        _canvasGroup.DOFade(1f, duration);
    }

    public void Hide()
    {
        _isShown = true; // 애니메이션 도중 중복 클릭 방지 등을 위해 내부 플래그는 미리 변경
        _isShown = false;

        KillTweens();

        // 패널 상호작용 비활성화 (투명해지는 동안 클릭 방지)
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;

        if(_isVR) HideInVR();
        else HideInPC();

        Log("패널 숨김");
    }

    private void HideInVR()
    {
        transform.DOScale(0f, duration).SetEase(hideEase);
        _canvasGroup.DOFade(0f, duration);
    }

    private void HideInPC()
    {
        _rect.DOAnchorPosX(hiddenPosX, duration).SetEase(hideEase);
        _canvasGroup.DOFade(0f, duration);
    }

    private void KillTweens()
    {
        DOTween.Kill(_rect);
        DOTween.Kill(_canvasGroup);
        if(_positionTweener != null)
        {
            _positionTweener.Kill();
            _positionTweener = null;
        }
    }

    private Vector3 GetTargetWorldPosition()
    {
        if(targetButtonTransform == null) return transform.position;
        return targetButtonTransform.position + offsetAboveButton;
    }

    private void Log(string msg) => Debug.Log($"[SlidingPanel] {msg}");
}