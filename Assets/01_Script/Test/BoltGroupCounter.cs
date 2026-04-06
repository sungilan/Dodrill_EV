using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Object; // NetworkObject 사용을 위해 추가
using FishNet.Transporting;
using UnityEngine;

// ============================================================
// BoltGroupCounter.cs
//
// 볼트 제거 흐름 (Despawn 방식):
//   1. Bolt.OnBoltLoosened 이벤트 발생 (프로그레스 100%)
//   2. HandleBoltLoosened() -> 서버에 BoltHideBroadcast 요청
//   3. 서버: OnServerHide() 수신 -> 코루틴 실행
//   4. 서버: ejectDuration 대기 후 InstanceFinder.ServerManager.Despawn(볼트) 호출
//   5. 결과: 모든 클라이언트에서 해당 볼트 오브젝트가 파괴(Destroy)됨
// ============================================================

// ── 볼트 제거 요청/명령 Broadcast ──────────────────────────────────────
public struct BoltHideBroadcast : IBroadcast
{
    public string groupId;   // 어느 그룹의 볼트인지
    public int boltIndex;    // 리스트 내 인덱스
}

// ── 볼트 진행도 동기화 Broadcast ────────────────────────────────────
public struct BoltProgressBroadcast : IBroadcast
{
    public string groupId;
    public int removed;
    public int total;
}

public class BoltGroupCounter : MonoBehaviour
{
    [Header("식별 (씬 내 고유값)")]
    public string groupId = "BoltGroup_Default";

    [Header("완료 시 발신할 Task ID (moduleId)")]
    public string taskId = "";

    [Header("볼트 제거 연출")]
    [Tooltip("볼트가 위로 솟아오르는 시간 후 디스폰")]
    public float ejectDuration = 0.4f;

    [Header("상태 (읽기 전용)")]
    [SerializeField] private int _totalCount;
    [SerializeField] private int _removedCount;

    private List<Bolt> _bolts = new();
    private HashSet<int> _removedIndices = new(); // 인덱스로 중복 체크
    private bool _completed = false;

    // ── 생명주기 ──────────────────────────────────────────────

    private void Awake()
    {
        _bolts.Clear();
        _bolts.AddRange(GetComponentsInChildren<Bolt>());
        _totalCount = _bolts.Count;
        if(_totalCount == 0)
            Debug.LogWarning($"[BoltGroupCounter] {name}: 자식에 Bolt 컴포넌트가 없습니다.");
    }

    private void Start()
    {
        foreach(var bolt in _bolts)
        {
            if(bolt != null)
                bolt.OnBoltLoosened += HandleBoltLoosened;
        }

        RegisterBroadcast();
    }

    private void OnDestroy()
    {
        foreach(var bolt in _bolts)
        {
            if(bolt != null)
                bolt.OnBoltLoosened -= HandleBoltLoosened;
        }

        UnregisterBroadcast();
    }

    // ── Broadcast 등록 ────────────────────────────────────────

    private void RegisterBroadcast()
    {
        // 서버: 클라이언트로부터의 제거 요청 수신
        InstanceFinder.ServerManager?.RegisterBroadcast<BoltHideBroadcast>(OnServerHide);
        // 클라이언트: 진행도 업데이트 수신 (선택 사항)
        InstanceFinder.ClientManager?.RegisterBroadcast<BoltProgressBroadcast>(OnClientProgress);
    }

    private void UnregisterBroadcast()
    {
        InstanceFinder.ServerManager?.UnregisterBroadcast<BoltHideBroadcast>(OnServerHide);
        InstanceFinder.ClientManager?.UnregisterBroadcast<BoltProgressBroadcast>(OnClientProgress);
    }

    // ── 볼트 풀림 처리 (이벤트 핸들러) ──────────────────────

    private void HandleBoltLoosened(Bolt bolt)
    {
        if(_completed) return;

        int idx = _bolts.IndexOf(bolt);
        if(idx == -1 || _removedIndices.Contains(idx)) return;

        // [로컬 피드백] 애니메이션 즉시 실행
        var ca = bolt.GetComponent<ClickableAnimator>();
        if(ca != null) ca.Open();

        // 서버에게 "이 볼트 다 풀렸으니 삭제해달라"고 요청 전송
        if(InstanceFinder.IsClientStarted)
        {
            var msg = new BoltHideBroadcast { groupId = groupId, boltIndex = idx };
            InstanceFinder.ClientManager.Broadcast(msg);
        }
    }

    // ── 서버: 제거 요청 수신 → 물리적 Despawn 처리 ──────

    private void OnServerHide(FishNet.Connection.NetworkConnection conn, BoltHideBroadcast msg, Channel ch)
    {
        if(msg.groupId != groupId) return;
        if(_removedIndices.Contains(msg.boltIndex)) return;

        // 서버에서 제거 확정
        _removedIndices.Add(msg.boltIndex);

        // 연출 시간(ejectDuration) 대기 후 서버 권위로 Despawn 실행
        StartCoroutine(DespawnAfterDelay(msg.boltIndex));
    }

    private IEnumerator DespawnAfterDelay(int boltIndex)
    {
        yield return new WaitForSeconds(ejectDuration);

        if(boltIndex >= 0 && boltIndex < _bolts.Count)
        {
            var boltObj = _bolts[boltIndex];
            if(boltObj != null && boltObj.gameObject != null)
            {
                // ★ 핵심: 서버가 NetworkObject를 디스폰함. 모든 클라이언트에서 자동 파괴됨.
                if(InstanceFinder.IsServerStarted)
                {
                    Debug.Log($"[BoltGroupCounter] 서버 권위로 볼트 Despawn: {boltObj.name}");
                    InstanceFinder.ServerManager.Despawn(boltObj.gameObject);
                }
            }
        }

        // 카운트 증가 및 완료 체크
        ApplyCountAndCheckCompletion();
    }

    // ── 카운트 처리 및 완료 보고 ──────────────────────────────

    private void ApplyCountAndCheckCompletion()
    {
        _removedCount = _removedIndices.Count;

        // 서버는 진행도를 모든 클라이언트에 브로드캐스트 (UI 동기화용)
        if(InstanceFinder.IsServerStarted)
        {
            InstanceFinder.ServerManager.Broadcast(new BoltProgressBroadcast
            {
                groupId = groupId,
                removed = _removedCount,
                total = _totalCount
            });
        }

        if(_removedCount >= _totalCount && !_completed)
        {
            _completed = true;
            Debug.Log($"[BoltGroupCounter] {groupId} 모든 볼트 제거됨 → Task 완료 ({taskId})");

            // 배터리 리프트 해제 상태 연동 (서버에서만 실행)
            if(InstanceFinder.IsServerStarted)
            {
                var liftCtrl = Object.FindFirstObjectByType<BatteryLiftController>();
                //if(liftCtrl != null) liftCtrl.SetBoltUnlocked(true);

                if(!string.IsNullOrEmpty(taskId))
                    InteractionEvents.FireTaskConfirmed(taskId);
            }
        }
    }

    private void OnClientProgress(BoltProgressBroadcast msg, Channel ch)
    {
        if(msg.groupId != groupId) return;

        // 클라이언트 로컬 카운트 변수 갱신 (HUD 업데이트용)
        _removedCount = msg.removed;
    }

    // ── 리셋 (참고: Despawn된 물체는 서버에서 다시 Spawn해줘야 함) ─────────

    public void ResetCounter()
    {
        _removedCount = 0;
        _completed = false;
        _removedIndices.Clear();
        // Despawn된 물체는 리스트에서 참조가 끊겼을 수 있으므로 씬 재로드나 별도 스폰 로직 필요
    }

    public int RemovedCount => _removedCount;
    public int TotalCount => _totalCount;
}