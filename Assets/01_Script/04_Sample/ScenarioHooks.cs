using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  ScenarioHooks.cs  — 시나리오 엔진의 "확장 지점" 모음 (모듈화 1단계, 2026-11-20)
//
//  엔진(ScenarioRunner·ScenarioSpawner·ScenarioBootstrapper …)은 EV·간호 같은 콘텐츠를 직접 알지 못한다.
//  콘텐츠 쪽이 시작할 때(RuntimeInitializeOnLoadMethod) 여기에 자기 기능을 등록한다.
//    · EV 콘텐츠 → Test/EvScenarioBinding.cs
//    · 간호술기   → 04_Sample/Modules/NursingModuleSet.cs
//  등록이 없으면(= 콘텐츠 없는 프로젝트) 아래 기본 동작으로 안전하게 동작한다.
//  다른 프로젝트에 엔진만 가져갈 때는 이 파일을 포함시키고, 그 프로젝트의 콘텐츠가 같은 방식으로 등록하면 된다.
// ============================================================

/// <summary>측정값 신호로 완료를 판정하는 모듈(엔진이 모듈 종류를 몰라도 부를 수 있게 한 인터페이스)</summary>
public interface IMeasureTaskModule : ITaskModule
{
    void HandleValueMeasured(string terminalId, float measuredValue);
    bool IsCompleted { get; }
}

public static class ScenarioHooks
{
    // ── 1) 모듈 공급자: ScenarioBootstrapper 가 ModuleRegistry 를 만들 때 순서대로 부른다 ──
    private struct Provider { public int order; public Action<ModuleRegistry> fn; }
    private static readonly Dictionary<string, Provider> _providers = new();

    /// <summary>콘텐츠 모듈 묶음을 등록한다. order 가 작은 것부터 등록된다(같은 moduleId 는 먼저 등록된 것이 이긴다).</summary>
    public static void AddModuleProvider(string key, int order, Action<ModuleRegistry> register)
    {
        if (string.IsNullOrEmpty(key) || register == null) return;
        _providers[key] = new Provider { order = order, fn = register };
    }

    /// <summary>등록된 공급자 수를 돌려준다(0 이면 모듈이 하나도 없는 것 — 호출 쪽에서 경고)</summary>
    public static int ApplyModuleProviders(ModuleRegistry registry)
    {
        var list = new List<KeyValuePair<string, Provider>>(_providers);
        list.Sort((a, b) => a.Value.order.CompareTo(b.Value.order));
        foreach (var kv in list) kv.Value.fn(registry);
        return list.Count;
    }

    // ── 2) 방금 오브젝트를 누른 사람(서버) ──
    public delegate bool RecentActorResolver(string objId, float within, out int clientId);
    public static RecentActorResolver RecentActor;

    public static bool TryGetRecentActor(string objId, float within, out int clientId)
    {
        if (RecentActor != null) return RecentActor(objId, within, out clientId);
        clientId = -1;
        return false;
    }

    // ── 3) 리프트 상승 버튼을 누른 사람(서버) ──
    public delegate bool LiftActorResolver(bool battery, float sinceTime, out int clientId);
    public static LiftActorResolver LiftActor;

    public static bool TryGetLiftActor(bool battery, float sinceTime, out int clientId)
    {
        if (LiftActor != null) return LiftActor(battery, sinceTime, out clientId);
        clientId = -1;
        return false;
    }

    // ── 4) 스폰 필터: (prefabId, scenarioId) → true 면 스폰하지 않는다 ──
    public static Func<string, string, bool> SpawnFilter;
    public static bool ShouldSkipSpawn(string prefabId, string scenarioId) => SpawnFilter != null && SpawnFilter(prefabId, scenarioId);

    // ── 5) 시나리오 목록 필터: scenarioId → false 면 목록에 보이지 않는다 ──
    public static Func<string, bool> ScenarioListFilter;
    public static bool ScenarioAllowed(string scenarioId) => ScenarioListFilter == null || ScenarioListFilter(scenarioId);

    // ── 6) 존 신호 필터(클라이언트): (zoneId, itemId) → false 면 서버로 보내지 않는다 ──
    public static Func<string, string, bool> ZoneSignalFilter;
    public static bool ZoneSignalAllowed(string zoneId, string itemId) => ZoneSignalFilter == null || ZoneSignalFilter(zoneId, itemId);

    // ── 7) 사람 수만큼 복사해서 스폰해야 하는 프리팹인가(예: 사람마다 따로 쓰는 공구) ──
    public static Func<Component, bool> IsPerPlayerPrefab;
    public static bool PerPlayerPrefab(Component prefab) => prefab != null && IsPerPlayerPrefab != null && IsPerPlayerPrefab(prefab);

    // ── 8) 지도 오브젝트(차량 등)가 스폰된 뒤 콘텐츠가 후처리(예: 리프트에 차량 연결) ──
    public static Action<GameObject> OnMapObjectSpawned;
}
