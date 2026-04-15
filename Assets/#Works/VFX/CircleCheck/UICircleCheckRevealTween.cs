using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class UICircleCheckRevealTween : MonoBehaviour
{
    #region Inspector

    [Header("대상 이미지")]
    [SerializeField] private Image circleImage;
    [SerializeField] private Image checkImage;

    [Header("재생 설정")]
    [SerializeField] private float totalDuration = 2f;

    #endregion

    #region Const

    private const float StartDelay = 0.1f;

    private const float BaseCircleDuration = 1.15f;
    private const float BaseCheckDuration = 0.75f;
    private const float BaseAnimationDuration = BaseCircleDuration + BaseCheckDuration;

    #endregion

    #region Private

    private Sequence _sequence;

    #endregion

    #region Unity Event

    private void Awake()
    {
        ApplyImageSetting();
        ResetVisual();
    }

    private void OnEnable()
    {
        PlaySequence();
    }

    private void OnDisable()
    {
        KillSequence();
    }

    #endregion

    #region Init

    private void ApplyImageSetting()
    {
        if (circleImage != null)
        {
            circleImage.type = Image.Type.Filled;
            circleImage.fillMethod = Image.FillMethod.Radial360;
            circleImage.fillClockwise = false;
            circleImage.fillOrigin = 2;
        }

        if (checkImage != null)
        {
            checkImage.type = Image.Type.Filled;
            checkImage.fillMethod = Image.FillMethod.Horizontal;
            checkImage.fillOrigin = 0;
        }
    }

    #endregion

    #region Play

    private void PlaySequence()
    {
        KillSequence();
        ResetVisual();

        float safeTotalDuration = Mathf.Max(totalDuration, StartDelay + 0.01f);
        float remainDuration = safeTotalDuration - StartDelay;

        float circleDuration = remainDuration * (BaseCircleDuration / BaseAnimationDuration);
        float checkDuration = remainDuration * (BaseCheckDuration / BaseAnimationDuration);

        _sequence = DOTween.Sequence();
        _sequence.SetUpdate(false);

        _sequence.AppendInterval(StartDelay);

        if (circleImage != null)
        {
            _sequence.Append(
                circleImage.DOFillAmount(1f, circleDuration).SetEase(Ease.Linear)
            );
        }

        if (checkImage != null)
        {
            _sequence.Append(
                checkImage.DOFillAmount(1f, checkDuration).SetEase(Ease.Linear)
            );
        }
    }

    private void KillSequence()
    {
        if (_sequence == null)
        {
            return;
        }

        _sequence.Kill();
        _sequence = null;
    }

    #endregion

    #region Visual

    private void ResetVisual()
    {
        if (circleImage != null)
        {
            circleImage.fillAmount = 0f;
        }

        if (checkImage != null)
        {
            checkImage.fillAmount = 0f;
        }
    }

    #endregion
}