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
            bolt.isAssembleMode = this.assembleMode;
        }
        RegisterBroadcast();
    }

    private void RegisterBroadcast()
    {
        InstanceFinder.ServerManager?.RegisterBroadcast<BoltHideBroadcast>(OnServerHide);
        InstanceFinder.ClientManager?.RegisterBroadcast<BoltProgressBroadcast>(OnClientProgress);
        InstanceFinder.ClientManager?.RegisterBroadcast<BoltFallBroadcast>(OnClientBoltFall);
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
        if (msg.groupId != groupId || _removedIndices.Contains(msg.boltIndex)) return;
        _removedIndices.Add(msg.boltIndex);

        if (!assembleMode)
        {
            InstanceFinder.ServerManager.Broadcast(new BoltFallBroadcast { groupId = groupId, boltIndex = msg.boltIndex });
            StartCoroutine(DeactivateBoltAfterDelay(msg.boltIndex));
        }
        ApplyCountAndCheckCompletion();
    }

    private void OnClientBoltFall(BoltFallBroadcast msg, Channel ch)
    {
        if (msg.groupId != groupId) return;
        var bolt = _bolts[msg.boltIndex];
        if (bolt != null)
        {
            bolt.PlayFallEffect();
            // ✅ 클라이언트도 자기 화면에서 볼트를 꺼야 함
            StartCoroutine(LocalDeactivateBolt(bolt));
        }
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
            Debug.Log($"<color=lime>[BoltGroup-Client]</color> 모든 볼트 완료 감지 -> 부품 상태 변경");
            StartCoroutine(LocalTogglePart(assembleMode));
        }
    }

    private IEnumerator LocalTogglePart(bool active)
    {
        yield return new WaitForSeconds(partToggleDelay);
        if (targetPart != null) targetPart.SetActive(active);
    }

    private void ApplyCountAndCheckCompletion()
    {
        _removedCount = _removedIndices.Count;
        InstanceFinder.ServerManager.Broadcast(new BoltProgressBroadcast { groupId = groupId, removed = _removedCount, total = _totalCount });

        if (_removedCount >= _totalCount && !_completed)
        {
            _completed = true;
            if (!string.IsNullOrEmpty(taskId)) InteractionEvents.FireTaskConfirmed(taskId);
            if (targetPart != null) StartCoroutine(TogglePartServer(assembleMode));
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
        _bolts[idx].Deactivate();
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