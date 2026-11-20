using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모든 태스크 상호작용 이벤트의 정적 허브
/// 씬 컴포넌트와 순수 C# 모듈을 연결
/// </summary>
public static class InteractionEvents
{
    // ── 구역 유효성 훅 (모듈화 2단계) ───────────────────────────
    // TaskInteractionZone 이 "지금 단계에서 이 구역·아이템이 유효한가"를 물을 때 쓴다. 시나리오 엔진이 시작할 때 등록한다
    // (EngineInteractionBinding). 등록이 없으면 모두 유효로 본다.
    public static Func<string, string, bool> ZoneValidator;
    public static bool IsZoneValid(string zoneId, string itemId) => ZoneValidator == null || ZoneValidator(zoneId, itemId);

    // ── 서버 중계 표시 (11/18) ─────────────────────────────────
    // 같은 이벤트를 두 뜻으로 쓴다: 클라이언트에선 '내가 집었다/썼다'(→ NetworkInteractionBridge·PlayerEquipmentSync 가 서버로 전송),
    // 서버에선 ScenarioRunner 가 받은 신호를 모듈에 알리려고 다시 발생시킨다.
    // 전용 서버는 프로세스가 달라 문제없지만, 혼자 하기(호스트 = 서버+클라이언트 한 프로세스)에서는 서버가 다시 발생시킨 이벤트를
    // 클라이언트 전송기가 또 서버로 보내 무한 반복했다(그랩 한 번에 초당 수천 줄 로그 → 심한 렉).
    // 서버가 다시 발생시키는 동안 이 값이 true — 클라이언트 → 서버 전송기는 이때 보내지 않는다.
    private static int s_serverRelay;
    public static bool IsServerRelay => s_serverRelay > 0;

    /// <summary>서버(ScenarioRunner)가 받은 신호를 모듈에 알릴 때 쓴다 — 클라이언트 전송기가 다시 보내지 않는다.</summary>
    public static void RelayFromServer(Action fire)
    {
        s_serverRelay++;
        try { fire(); }
        finally { s_serverRelay--; }
    }

    /// <summary>
    /// 서버 쪽 훈련 모듈(ConfirmTask·GrabUse·GrabAll·SequentialStep …)이 집기/사용/완료 알림을 건너뛸지.
    /// 혼자 하기(호스트)에서는 클라이언트가 낸 알림이 같은 프로세스의 모듈에 바로 한 번, 브리지 → 서버 → RelayFromServer 로 또 한 번 도착해
    /// 다단계 모듈이 한 번 조작에 두 단계씩 넘어갔다. 전용 서버와 똑같이 서버가 검증해서 다시 알린 것(중계)만 받는다.
    /// (전용 서버·클라이언트 전용에서는 항상 false)
    /// </summary>
    public static bool SkipForServerModule =>
        s_serverRelay == 0 && FishNet.InstanceFinder.IsServerStarted && FishNet.InstanceFinder.IsClientStarted;

    // ── Confirm (UI 완료 버튼) ─────────────────────────────────
    /// <summary>플레이어가 UI 완료 버튼을 누를 때 발생. moduleId 전달</summary>
    public static event Action<string> OnTaskConfirmed;

    // ── GrabUse (아이템을 잡고 사용/펌프) ─────────────────────
    /// <summary>Grabbable을 사용(Use/펌프)할 때 발생. prefabId 전달</summary>
    public static event Action<string> OnItemUsed;

    // ── GrabAll (아이템 집기) ──────────────────────────────────
    /// <summary>Grabbable을 집을 때 발생. prefabId 전달</summary>
    public static event Action<string> OnItemGrabbed;

    // ── GrabZone (아이템/손이 존에 진입) ──────────────────────
    /// <summary>아이템 또는 손이 TaskInteractionZone에 진입할 때 발생
    /// <para>itemId: 들고 있는 아이템의 prefabId. 빈 문자열이면 빈손</para>
    /// </summary>
    public static event Action<string, string> OnZoneActivated; // (zoneId, itemId)

    public static event Action<string, float> OnValueMeasured; // (terminalId, value)

    // ── 발행 메서드 ───────────────────────────────────────────

    public static void FireTaskConfirmed(string moduleId)
    {
        Debug.Log($"[InteractionEvents] TaskConfirmed: {moduleId}");
        OnTaskConfirmed?.Invoke(moduleId);
    }

    public static void FireItemUsed(string prefabId)
    {
        Debug.Log($"[InteractionEvents] ItemUsed: {prefabId}");
        OnItemUsed?.Invoke(prefabId);
    }

    public static void FireItemGrabbed(string prefabId)
    {
        Debug.Log($"[InteractionEvents] ItemGrabbed: {prefabId}");
        OnItemGrabbed?.Invoke(prefabId);
    }

    public static void FireZoneActivated(string zoneId, string itemId)
    {
        Debug.Log($"[InteractionEvents] ZoneActivated: zone={zoneId}, item={itemId}");
        OnZoneActivated?.Invoke(zoneId, itemId);
    }

    // ── ValueMeasured (멀티미터 측정값) ───────────────────────
    /// <summary>
    /// MultimeterMaster가 양 프로브 접촉 시 발행.
    /// ZoneAndMeasureModule이 수신해서 targetValue와 비교 후 완료 판정.
    /// </summary>
    /// <param name="terminalId">빨간 프로브 단자 ID (targetZoneId/targetObjName과 매칭)</param>
    /// <param name="value">측정된 값 (V 또는 Ω)</param>
    
    public static void FireValueMeasured(string terminalId, float value)
    {
        Debug.Log($"[InteractionEvents] ValueMeasured: terminal={terminalId}, value={value:F2}");
        OnValueMeasured?.Invoke(terminalId, value);
    }

    // ── StepAdvanced (다단계 Task 스텝 진행) ──────────────────
    /// <summary>SequentialStepModule이 다음 스텝으로 진행할 때 발생
    /// <para>stepIndex: 새로 활성화된 스텝 인덱스 / totalSteps: 전체 스텝 수</para>
    /// </summary>
    public static event Action<int, int> OnStepAdvanced; // (stepIndex, totalSteps)

    public static void FireStepAdvanced(int stepIndex, int totalSteps)
    {
#if UNITY_EDITOR
        Debug.Log($"[InteractionEvents] StepAdvanced: {stepIndex + 1}/{totalSteps}");
#endif
        OnStepAdvanced?.Invoke(stepIndex, totalSteps);
    }
}
