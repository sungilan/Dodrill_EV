using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;

// ── Broadcasts ────────────────────────────────────────────────────────
public struct BoltHideBroadcast : IBroadcast
{
    public string groupId;
    public int boltIndex;
}

public struct BoltFallBroadcast : IBroadcast
{
    public string groupId;
    public int boltIndex;
}

public struct BoltProgressBroadcast : IBroadcast
{
    public string groupId;
    public int removed;
    public int total;
}

// ── 볼트 진행도 중계 (돌리는 클라 → 서버 → 다른 클라) ─────────────────────
// 볼트를 도구로 돌리는 사람의 클라이언트만 진행도·위치·체결 완료 초록 아웃라인을 계산해서, 다른 플레이어에게는 볼트가 전혀 안 움직였다.
public struct BoltProgressSyncBroadcast : IBroadcast
{
    public string groupId;
    public int boltIndex;
    public float progress;   // 0..1
    public int senderId;     // 서버가 채운다(보낸 사람 클라이언트 ID) — 본인에게 되돌아온 것은 무시
}

// ── 조립 복원 명령 (서버 → 전체 클라) ──────────────────────────────
public struct BoltGroupResetBroadcast : IBroadcast
{
    public string groupId;
}

// ============================================================
//  BoltGroupCounter.cs
//
//  [분해 모드] assembleMode = false
//    볼트 풀림 → 낙하 연출(BoltFallBroadcast) → Bolt.Deactivate()
//    모든 볼트 제거 → targetPart.SetActive(false)
//
//  [조립 모드] assembleMode = true
//    ResetForAssemble() 호출 → 볼트 ReactivateForAssemble()
//    볼트 체결 → 완료 → targetPart.SetActive(true)
//
//  네트워크:
//    클라 → BoltHideBroadcast → 서버 → BoltFallBroadcast / BoltProgressBroadcast
//    서버 → BoltGroupResetBroadcast → 클라 (조립 모드 전환)
// ============================================================
public class BoltGroupCounter : MonoBehaviour
{
    [Header("설정")]
    public bool assembleMode = false;
    public string groupId = "BoltGroup_Default";
    public string taskId = "";
    public float ejectDuration = 0.5f;

    [Header("부품 참조")]
    public GameObject targetPart;
    public float partToggleDelay = 1.0f;

    [SerializeField] private int _totalCount;
    [SerializeField] private int _removedCount;
    private List<Bolt> _bolts = new();
    private HashSet<int> _removedIndices = new();
    private bool _completed = false;

    private void Awake()
    {
        _bolts.Clear();
        _bolts.AddRange(GetComponentsInChildren<Bolt>(true));
        _totalCount = _bolts.Count;
    }

    private void Start()
    {
        foreach (var bolt in _bolts)
        {
            bolt.OnBoltLoosened += HandleBoltLoosened;
            bolt.OnProgressChanged += HandleBoltProgressChanged;
            bolt.isAssembleMode = this.assembleMode;
            // 체결 단계: 볼트를 처음부터 '덜 조인' 위치에 둔다. 예전에는 다 조인 위치에 있다가
            // 드릴을 대는 순간 빠진 위치로 툭 튀어나온 뒤 다시 들어가 보였다.
            if (this.assembleMode && bolt.gameObject.activeInHierarchy && bolt.Progress <= 0f) bolt.ShowAssembleStart();
        }
        RegisterBroadcast();
    }

    private void RegisterBroadcast()
    {
        // 서버 핸들러는 서버에서만 등록
        if(InstanceFinder.IsServerStarted)
        {
            InstanceFinder.ServerManager.RegisterBroadcast<BoltHideBroadcast>(OnServerHide);
            InstanceFinder.ServerManager.RegisterBroadcast<BoltProgressSyncBroadcast>(OnServerProgressSync);
            _syncServerRegistered = true;
            Debug.Log($"<color=cyan>[Server]</color> BoltHideBroadcast 핸들러 등록 완료");
        }

        // 클라이언트 핸들러는 클라이언트에서만 등록
        if(InstanceFinder.IsClientStarted)
        {
            InstanceFinder.ClientManager.RegisterBroadcast<BoltProgressBroadcast>(OnClientProgress);
            InstanceFinder.ClientManager.RegisterBroadcast<BoltFallBroadcast>(OnClientBoltFall);
            InstanceFinder.ClientManager.RegisterBroadcast<BoltProgressSyncBroadcast>(OnClientProgressSync);
            _syncClientRegistered = true;
            Debug.Log($"<color=cyan>[Client]</color> 브로드캐스트 핸들러 등록 완료");
        }
    }

    private bool _syncServerRegistered, _syncClientRegistered;

    private void OnDestroy()
    {
        foreach (var b in _bolts)
        {
            if (b == null) continue;
            b.OnBoltLoosened -= HandleBoltLoosened;
            b.OnProgressChanged -= HandleBoltProgressChanged;
        }
        if (_syncServerRegistered && InstanceFinder.ServerManager != null)
            InstanceFinder.ServerManager.UnregisterBroadcast<BoltProgressSyncBroadcast>(OnServerProgressSync);
        if (_syncClientRegistered && InstanceFinder.ClientManager != null)
            InstanceFinder.ClientManager.UnregisterBroadcast<BoltProgressSyncBroadcast>(OnClientProgressSync);
    }

    // ── 볼트 진행도 중계 ─────────────────────────────────────
    private readonly Dictionary<int, float> _lastSentProgress = new();
    private readonly Dictionary<int, float> _lastSentTime = new();
    private const float SyncMinDelta = 0.03f;      // 진행도가 이만큼 바뀌어야 보낸다
    private const float SyncMinInterval = 0.08f;   // 최소 전송 간격(초)

    private void HandleBoltProgressChanged(Bolt bolt)
    {
        if (!InstanceFinder.IsClientStarted) return;
        int idx = _bolts.IndexOf(bolt);
        if (idx < 0) return;

        float p = bolt.Progress;
        _lastSentProgress.TryGetValue(idx, out float last);
        _lastSentTime.TryGetValue(idx, out float lastT);

        bool done = p >= 1f && last < 1f;
        if (!done && (p - last < SyncMinDelta || Time.unscaledTime - lastT < SyncMinInterval)) return;

        _lastSentProgress[idx] = p;
        _lastSentTime[idx] = Time.unscaledTime;
        InstanceFinder.ClientManager.Broadcast(new BoltProgressSyncBroadcast { groupId = groupId, boltIndex = idx, progress = p });
    }

    private void OnServerProgressSync(NetworkConnection conn, BoltProgressSyncBroadcast msg, Channel ch)
    {
        if (msg.groupId != groupId) return;
        if (msg.boltIndex < 0 || msg.boltIndex >= _bolts.Count) return;
        msg.senderId = conn != null ? conn.ClientId : -1;
        InstanceFinder.ServerManager.Broadcast(msg);
    }

    private void OnClientProgressSync(BoltProgressSyncBroadcast msg, Channel ch)
    {
        if (msg.groupId != groupId) return;
        if (msg.boltIndex < 0 || msg.boltIndex >= _bolts.Count) return;

        // 내가 돌린 것이 되돌아온 것은 이미 로컬에서 처리했다
        var conn = InstanceFinder.ClientManager != null ? InstanceFinder.ClientManager.Connection : null;
        if (conn != null && conn.IsValid && conn.ClientId == msg.senderId) return;

        var bolt = _bolts[msg.boltIndex];
        if (bolt != null) bolt.ApplyRemoteProgress(msg.progress);
    }

    private void HandleBoltLoosened(Bolt bolt)
    {
        if (_completed) return;
        int idx = _bolts.IndexOf(bolt);
        if (idx == -1 || _removedIndices.Contains(idx)) return;

        if (InstanceFinder.IsClientStarted)
            InstanceFinder.ClientManager.Broadcast(new BoltHideBroadcast { groupId = groupId, boltIndex = idx });
    }

    private void OnServerHide(NetworkConnection conn, BoltHideBroadcast msg, Channel ch)
    {
        // 그룹 ID 및 인덱스 검증
        if(msg.groupId != groupId)
        {
            Debug.LogWarning($"<color=yellow>[Server]</color> 잘못된 groupId: {msg.groupId}");
            return;
        }

        if(msg.boltIndex < 0 || msg.boltIndex >= _bolts.Count)
        {
            Debug.LogError($"<color=red>[Server]</color> 인덱스 범위 초과: {msg.boltIndex}, 전체: {_bolts.Count}");
            return;
        }

        if(_removedIndices.Contains(msg.boltIndex))
        {
            Debug.LogWarning($"<color=yellow>[Server]</color> 이미 제거된 볼트: {msg.boltIndex}");
            return;
        }

        _removedIndices.Add(msg.boltIndex);

        if(!assembleMode)
        {
            InstanceFinder.ServerManager.Broadcast(new BoltFallBroadcast
            {
                groupId = groupId,
                boltIndex = msg.boltIndex
            });
            StartCoroutine(DeactivateBoltAfterDelay(msg.boltIndex));
        }

        ApplyCountAndCheckCompletion();
    }

    private void OnClientBoltFall(BoltFallBroadcast msg, Channel ch)
    {
        if(msg.groupId != groupId) return;

        // 인덱스 범위 검증
        if(msg.boltIndex < 0 || msg.boltIndex >= _bolts.Count)
        {
            Debug.LogError($"<color=red>[Client]</color> 인덱스 범위 초과: {msg.boltIndex}");
            return;
        }

        var bolt = _bolts[msg.boltIndex];
        if(bolt == null)
        {
            Debug.LogError($"<color=red>[Client]</color> Bolt가 null: 인덱스 {msg.boltIndex}");
            return;
        }

        bolt.PlayFallEffect();
        StartCoroutine(LocalDeactivateBolt(bolt));
    }

    private IEnumerator LocalDeactivateBolt(Bolt bolt)
    {
        yield return new WaitForSeconds(ejectDuration);
        bolt.Deactivate();
    }

    private void OnClientProgress(BoltProgressBroadcast msg, Channel ch)
    {
        if (msg.groupId != groupId) return;
        _removedCount = msg.removed;

        // ✅ 클라이언트도 진행도가 다 차면 부품을 끔/켬
        if (_removedCount >= _totalCount && targetPart != null)
        {
            //Debug.Log($"<color=lime>[BoltGroup-Client]</color> 모든 볼트 완료 감지 -> 부품 상태 변경");
            StartCoroutine(LocalTogglePart(assembleMode));
        }
    }

    private IEnumerator LocalTogglePart(bool active)
    {
        yield return new WaitForSeconds(partToggleDelay);
        // 체결 단계가 끝나면 볼트별 초록 표시를 걷는다
        foreach (var b in _bolts) if (b != null) b.MarkDone(false);
        if (targetPart != null) targetPart.SetActive(active);
    }

    private void ApplyCountAndCheckCompletion()
    {
        _removedCount = _removedIndices.Count;
        Debug.Log($"<color=yellow>[BoltGroupCounter-ApplyCount]</color> {groupId}: 제거됨={_removedCount}, 전체={_totalCount}");

        // 진행도 브로드캐스트
        InstanceFinder.ServerManager.Broadcast(new BoltProgressBroadcast
        {
            groupId = groupId,
            removed = _removedCount,
            total = _totalCount
        });
        Debug.Log($"<color=cyan>[BoltGroupCounter]</color> BoltProgressBroadcast 송신: {_removedCount}/{_totalCount}");

        // 완료 체크
        if(_removedCount >= _totalCount && !_completed)
        {
            _completed = true;
            Debug.Log($"<color=lime>[BoltGroupCounter-Completion]</color> {groupId} 완료 감지!");

            if(!string.IsNullOrEmpty(taskId))
            {
                Debug.Log($"<color=lime>[BoltGroupCounter]</color> TaskConfirmed 신호 발생: {taskId}");
                InteractionEvents.FireTaskConfirmed(taskId);
            }
            else
            {
                Debug.LogWarning($"<color=orange>[BoltGroupCounter]</color> taskId가 비어있음 - TaskConfirmed 신호 미발생");
            }

            if(targetPart != null)
            {
                Debug.Log($"<color=lime>[BoltGroupCounter]</color> 부품 토글 코루틴 시작");
                StartCoroutine(TogglePartServer(!assembleMode));
            }
            else
            {
                Debug.LogWarning($"<color=orange>[BoltGroupCounter]</color> targetPart가 null - 부품 토글 불가");
            }
        }
    }

    private IEnumerator TogglePartServer(bool active)
    {
        yield return new WaitForSeconds(partToggleDelay);
        targetPart.SetActive(active);
    }

    private IEnumerator DeactivateBoltAfterDelay(int idx)
    {
        yield return new WaitForSeconds(ejectDuration);

        // 코루틴 실행 중 bolt가 파괴되었을 수 있으므로 검증
        if(idx >= 0 && idx < _bolts.Count && _bolts[idx] != null)
        {
            _bolts[idx].Deactivate();
        }
        else
        {
            Debug.LogWarning($"<color=yellow>[Server]</color> 볼트 비활성화 실패: 인덱스 {idx}");
        }
    }

    // ── 조립 모드 전환을 위한 외부 호출 메서드 ──────────────────────

    /// <summary>
    /// 조립 단계를 위해 볼트 그룹을 초기화하고 조립 모드로 전환합니다.
    /// </summary>
    public void ResetForAssemble()
    {
        // 서버에서만 초기화 로직 실행 (상태 동기화 고려)
        if (FishNet.InstanceFinder.IsServerStarted)
        {
            assembleMode = true;
            _completed = false;
            _removedCount = 0;
            _removedIndices.Clear();

            // 모든 자식 볼트들을 조립 대기 상태로 복원
            foreach (var bolt in _bolts)
            {
                if (bolt != null)
                {
                    bolt.isAssembleMode = true;
                    bolt.ReactivateForAssemble(); // 볼트 원위치 및 활성화
                }
            }

            // 모든 클라이언트에게 조립 모드 전환 및 리셋 알림 방송
            FishNet.InstanceFinder.ServerManager.Broadcast(new BoltGroupResetBroadcast { groupId = groupId });

            Debug.Log($"<color=lime>[BoltGroup]</color> {groupId} 조립 모드 리셋 완료");
        }
    }

    // ── 가이드 시스템 및 외부 참조를 위한 공개 프로퍼티 ──────────────────────

    /// <summary>현재까지 처리(제거 또는 체결)된 볼트의 수</summary>
    public int RemovedCount => _removedCount;

    /// <summary>이 그룹에 속한 전체 볼트의 수</summary>
    public int TotalCount => _totalCount;

    /// <summary>진행도를 "남은수/전체수" 형태의 텍스트로 반환</summary>
    public string GetProgressText() => $"{TotalCount - RemovedCount}/{TotalCount}";
}