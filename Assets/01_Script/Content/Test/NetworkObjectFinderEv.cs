using UnityEngine;

// ============================================================
//  NetworkObjectFinderEv.cs — NetworkObjectFinder(공통 이름 레지스트리)의 EV 전용 확장 (모듈화 2단계, 2026-11-20)
//  호출 방식은 그대로(finder.GetInteractablePart(name) / finder.CacheStepTargets(data)).
// ============================================================
public static class NetworkObjectFinderEv
{
    /// <summary>InteractablePart 컴포넌트가 붙어 있는 경우 반환</summary>
    public static InteractablePart GetInteractablePart(this NetworkObjectFinder finder, string objName)
    {
        var obj = finder.Get(objName);
        return obj != null ? obj.GetComponent<InteractablePart>() : null;
    }

    /// <summary>DTCData 의 모든 RepairStep cachedTarget 을 현재 등록된 오브젝트로 일괄 채운다. TrainingFlowManager.StartTraining() 직후 호출 권장.</summary>
    public static void CacheStepTargets(this NetworkObjectFinder finder, DTCData data)
    {
        if(data?.repairSteps == null) return;

        foreach(var step in data.repairSteps)
        {
            if(string.IsNullOrEmpty(step.targetObjName)) continue;
            step.cachedTarget = finder.Get(step.targetObjName);

            if(step.cachedTarget == null)
                Debug.LogWarning($"[ObjectFinder] '{step.targetObjName}' 오브젝트를 찾을 수 없음 — 씬에 등록됐는지 확인");
        }
    }
}
