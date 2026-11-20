using UnityEngine;

// ============================================================
//  EvAvatarBinding.cs — EV 콘텐츠가 아바타 모듈(AvatarHooks)에 자기 기능을 등록 (모듈화 2단계, 2026-11-20)
//
//  아바타가 EV 클래스(ClickableAnimator·ImpactWrench·LadderProp·ClimbSurface·EquipFx)를 직접 부르던 것을 이 파일로 모았다.
//  씬이 로드되기 전에 한 번 실행된다.
// ============================================================
public static class EvAvatarBinding
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        VrUi.IsVRProvider = () => GameScenePlatformManager.IsVR;   // VrUi(공통 UI 도우미)가 EV 플랫폼 관리자를 직접 모르게
        AvatarHooks.ToolBusy = () => ImpactWrench.RecentlyWorking;
        AvatarHooks.GroundBonus = LadderProp.BonusAt;
        AvatarHooks.ClimbStep = LadderProp.TryClimbStep;   // PC Q/E·모바일 서기/앉기 버튼: 사다리 가까이면 한 칸 오르내리기
        AvatarHooks.NoticeHandler = (key, title, message, warn, show, cooldown) =>
            EquipFx.Notice(key, title, message, warn ? EquipFx.Warn : EquipFx.Info, show, cooldown);
        // ClickableAnimator.OnPCClick 이 AvatarHooks.RaiseWorldClicked 를 직접 부른다(EV → 아바타 방향이라 문제 없음)
    }
}
