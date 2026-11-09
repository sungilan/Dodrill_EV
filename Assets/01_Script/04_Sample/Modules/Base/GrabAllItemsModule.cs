using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 필요 아이템을 모두 집으면 완료되는 Task 베이스
/// 주 용도: PrepareMedication (약물 준비), VerifyMedicationOrder 등
///
/// PerPlayer = true 이면(보호구 착용처럼 '사람마다' 해야 하는 단계) 서버가 접속자별로 누가 무엇을 집었는지 센다.
/// 한 명이 집었다고 다음 단계로 넘어가지 않고, 접속 중인 모든 사람이 필요한 아이템을 집어야 완료된다.
/// (ScenarioRunner 가 연결(conn)의 ClientId 로 NotifyGrabbedBy 를 불러 준다 — 클라이언트가 보낸 값은 믿지 않는다)
/// </summary>
public abstract class GrabAllItemsModule : ITaskModule
{
    public abstract string ModuleId { get; }

    /// <summary>true: 접속자 각자가 필요한 아이템을 모두 집어야 완료(서버가 사람별로 확인).</summary>
    public virtual bool PerPlayer => false;

    public enum GrabResult
    {
        /// <summary>이 단계와 관계없는 아이템이거나 이미 끝난 단계</summary>
        Ignored,
        /// <summary>이 사람은 이미 필요한 아이템을 다 집었음(남는 아이템을 또 집은 경우)</summary>
        AlreadyDone,
        /// <summary>인정했지만 이 사람은 아직 집어야 할 아이템이 남음</summary>
        Counted,
        /// <summary>이 사람이 방금 필요한 아이템을 모두 채움</summary>
        PlayerCompleted,
    }

    private HashSet<string> _remainingItems;
    private HashSet<string> _requiredItems;
    private readonly Dictionary<int, HashSet<string>> _grabbedByClient = new Dictionary<int, HashSet<string>>();
    private Action          _onComplete;
    private Action          _onFail;
    private bool            _completed;

    public bool IsCompleted => _completed;

    public void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        _remainingItems = new HashSet<string>(config?.requiredItems ?? new List<string>());
        _requiredItems  = new HashSet<string>(_remainingItems);
        _grabbedByClient.Clear();
        _onComplete     = onComplete;
        _onFail         = onFail;
        _completed      = false;

        if (_remainingItems.Count == 0)
        {
            Debug.LogWarning($"[{ModuleId}] requiredItems가 비어있음 — 즉시 완료 처리");
            onComplete?.Invoke();
            return;
        }

        // 사람별 확인 단계는 '누가' 집었는지 알 수 없는 정적 이벤트를 쓰지 않는다 (NotifyGrabbedBy 로만 진행)
        if (!PerPlayer)
            InteractionEvents.OnItemGrabbed += HandleItemGrabbed;
    }

    private void HandleItemGrabbed(string prefabId)
    {
        if (_completed) return;
        if (!_remainingItems.Remove(prefabId)) return;

        Debug.Log($"[{ModuleId}] 아이템 획득: {prefabId} (남은 항목: {_remainingItems.Count})");

        if (_remainingItems.Count == 0)
        {
            _completed = true;
            _onComplete?.Invoke();
        }
    }

    // ── 사람별 확인 (PerPlayer) ─────────────────────────────

    private bool HasAll(int clientId)
    {
        if (_requiredItems == null || _requiredItems.Count == 0) return false;
        if (!_grabbedByClient.TryGetValue(clientId, out var set)) return false;
        foreach (var item in _requiredItems)
            if (!set.Contains(item)) return false;
        return true;
    }

    /// <summary>이 사람이 필요한 아이템을 모두 집었는지</summary>
    public bool HasCompleted(int clientId) => PerPlayer && HasAll(clientId);

    /// <summary>
    /// [서버] clientId 가 itemId 를 집었다고 기록한다. requiredClients = 지금 접속 중인 모든 클라이언트 ID.
    /// 접속자 전원이 끝나면 완료 콜백을 부른다.
    /// </summary>
    public GrabResult NotifyGrabbedBy(int clientId, string itemId, IReadOnlyCollection<int> requiredClients)
    {
        if (!PerPlayer || _completed || _requiredItems == null) return GrabResult.Ignored;
        if (!_requiredItems.Contains(itemId)) return GrabResult.Ignored;

        if (HasAll(clientId)) return GrabResult.AlreadyDone;

        if (!_grabbedByClient.TryGetValue(clientId, out var set))
            _grabbedByClient[clientId] = set = new HashSet<string>();
        set.Add(itemId);

        if (!HasAll(clientId))
        {
            Debug.Log($"[{ModuleId}] Client#{clientId} {itemId} 획득 (이 사람 남은 항목 있음)");
            return GrabResult.Counted;
        }

        Debug.Log($"[{ModuleId}] Client#{clientId} 필요 항목 모두 완료 ({CountDone(requiredClients)}/{requiredClients?.Count ?? 0})");
        Recheck(requiredClients);
        return GrabResult.PlayerCompleted;
    }

    /// <summary>접속 중인 모든 사람이 끝났는지 확인하고, 끝났으면 완료한다. (접속자가 빠졌을 때도 다시 불린다)</summary>
    public void Recheck(IReadOnlyCollection<int> requiredClients)
    {
        if (!PerPlayer || _completed || requiredClients == null || requiredClients.Count == 0) return;
        foreach (int id in requiredClients)
            if (!HasAll(id)) return;

        _completed = true;
        Debug.Log($"<color=lime>[{ModuleId}]</color> 접속자 {requiredClients.Count}명 모두 완료");
        _onComplete?.Invoke();
    }

    private int CountDone(IReadOnlyCollection<int> clients)
    {
        int n = 0;
        if (clients != null) foreach (int id in clients) if (HasAll(id)) n++;
        return n;
    }

    public void OnUpdate(float deltaTime) { }

    public void OnComplete() => InteractionEvents.OnItemGrabbed -= HandleItemGrabbed;
    public void OnFail()     => InteractionEvents.OnItemGrabbed -= HandleItemGrabbed;
}
