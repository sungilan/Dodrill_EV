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

    [Header("가이드 설정")]
    public bool showGuide = true;       // 마커(화살표) 생성 여부
    public bool showGhost = true;
    public bool showOutline = true;// 고스트 오브젝트 생성 여부

    [Tooltip("비어있으면 현재 단계의 spawnObjects 중 첫 번째 아이템을 고스트로 사용합니다.")]
    public string customGhostPrefabId;  // 특정 오브젝트만 고스트로 띄우고 싶을 때 지정 (예: "BatteryCover")
}