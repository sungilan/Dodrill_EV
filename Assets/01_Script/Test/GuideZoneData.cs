using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;

public class GuideZoneData : MonoBehaviour
{
    [Header("가이드 팝업 설정")]
    public Sprite targetImage;

    // 명시적으로 전체 이름을 적어줍니다.
    [Tooltip("타겟 명칭")]
    public UnityEngine.Localization.LocalizedString targetName;

    [Tooltip("상세 가이드 문구")]
    public UnityEngine.Localization.LocalizedString guideDescription;

    [Header("진행도 설정")]
    public bool showProgress = false;
    public bool showGuide = false;
}