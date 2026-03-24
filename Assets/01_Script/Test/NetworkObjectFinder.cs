using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;

// ============================================================
//  NetworkObjectFinder.cs
//  이름(string) → GameObject 매핑 레지스트리.
//
//  필요 이유:
//    CSV/ScriptableObject 에는 GameObject 레퍼런스를 직접 담을 수 없어서
//    targetObjName(문자열)으로 저장함.
//    TrainingFlowManager 가 ProcessStepAction 을 호출할 때 이 이름으로
//    씬의 실제 오브젝트를 찾을 수 있어야 함.
//
//  등록 방법:
//    각 부품의 SyncGrab.OnStartClient / InteractablePart.Awake 에서
//    NetworkObjectFinder.Instance.Register(gameObject.name, gameObject) 호출.
//    (Clone) 접미사는 자동 제거됨.
//
//  기존 구조 대응:
//    간호 시뮬레이션의 ScenarioBootstrapper.RegisterModules() 와 유사한
//    '런타임 자동 등록' 패턴.
// ============================================================

public class NetworkObjectFinder : MonoBehaviour
{
    public static NetworkObjectFinder Instance { get; private set; }

    private readonly Dictionary<string, GameObject> _registry = new();

    private void Awake()
    {
        if(Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── 등록 / 해제 ───────────────────────────

    /// <summary>
    /// 오브젝트를 이름 기반으로 등록.
    /// SyncGrab.OnStartClient, InteractablePart.Awake 에서 호출.
    /// </summary>
    public void Register(string objName, GameObject obj)
    {
        // (Clone) 접미사 제거
        string key = CleanName(objName);

        if(string.IsNullOrEmpty(key)) return;

        if(_registry.ContainsKey(key))
        {
            // 같은 이름이 이미 등록된 경우 — 최신 오브젝트로 갱신
            _registry[key] = obj;
            return;
        }

        _registry.Add(key, obj);
        Debug.Log($"[ObjectFinder] 등록: {key}");
    }

    /// <summary>오브젝트 Despawn / Destroy 시 해제</summary>
    public void Unregister(string objName)
    {
        string key = CleanName(objName);
        if(_registry.Remove(key))
            Debug.Log($"[ObjectFinder] 해제: {key}");
    }

    // ── 조회 ─────────────────────────────────

    /// <summary>
    /// 이름으로 오브젝트 조회. 없으면 null.
    /// TrainingFlowManager.ProcessStepAction 에서 사용.
    /// </summary>
    public GameObject Get(string objName)
    {
        string key = CleanName(objName);
        _registry.TryGetValue(key, out var obj);
        return obj;
    }

    /// <summary>InteractablePart 컴포넌트가 붙어 있는 경우 반환</summary>
    public InteractablePart GetInteractablePart(string objName)
    {
        var obj = Get(objName);
        return obj != null ? obj.GetComponent<InteractablePart>() : null;
    }

    public bool Contains(string objName) => _registry.ContainsKey(CleanName(objName));

    public int Count => _registry.Count;

    // ── DTCData 스텝 캐시 배치 ────────────────

    /// <summary>
    /// DTCData 에 포함된 모든 RepairStep 의 cachedTarget 을
    /// 현재 등록된 오브젝트로 일괄 채움.
    /// TrainingFlowManager.StartTraining() 직후 호출 권장.
    /// </summary>
    public void CacheStepTargets(DTCData data)
    {
        if(data?.repairSteps == null) return;

        foreach(var step in data.repairSteps)
        {
            if(string.IsNullOrEmpty(step.targetObjName)) continue;
            step.cachedTarget = Get(step.targetObjName);

            if(step.cachedTarget == null)
                Debug.LogWarning($"[ObjectFinder] '{step.targetObjName}' 오브젝트를 찾을 수 없음 — 씬에 등록됐는지 확인");
        }
    }

    // ── 유틸 ─────────────────────────────────

    private static string CleanName(string raw)
    {
        if(string.IsNullOrEmpty(raw)) return string.Empty;
        return raw.Replace("(Clone)", "").Trim();
    }
}