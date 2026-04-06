using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모든 태스크 상호작용 이벤트의 정적 허브
/// 씬 컴포넌트와 순수 C# 모듈을 연결
/// </summary>
public static class InteractionEvents
{
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
