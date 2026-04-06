using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

// ── 볼트 제거 요청/명령 Broadcast ──────────────────────────────────────
public struct BoltHideBroadcast : IBroadcast
{
    public string groupId;   // 어느 그룹의 볼트인지
    public int boltIndex;    // 리스트 내 인덱스
}

// ── 볼트 낙하 연출 명령 ────────────────────────────────────
public struct BoltFallBroadcast : IBroadcast
{
    public string groupId;
    public int boltIndex;
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
    [Header("모드 설정")]
    [Tooltip("True: 조립 모드(볼트 유지), False: 분해 모드(볼트 삭제)")]
    public bool assembleMode = false;

    [Header("식별 (씬 내 고유값)")]
    public string groupId = "BoltGroup_Default";

    [Header("완료 시 발신할 Task ID (moduleId)")]
    public string taskId = "";

    [Header("볼트 제거 연출 (분해 전용)")]
    [Tooltip("볼트가 낙하 연출을 보여주는 시간 후 디스폰")]
    public float ejectDuration = 1.0f;

    [Header("상태 (읽기 전용)")]
    [SerializeField] private int _totalCount;
    [SerializeField] private int _removedCount; // 조립 시에는 '체결된 개수'로 의미 변경

    private List<Bolt> _bolts = new();
    private HashSet<int> _removedIndices = new(); // 처리가 완료된 볼트 인덱스들
    private bool _completed = false;

    private void Awake()
    {
        _bolts.Clear();
        _bolts.AddRange(GetComponentsInChildren<Bolt>());
        _totalCount = _bolts.Count;
    }

    private void Start()
    {
        foreach(var bolt in _bolts)
        {
            if(bolt != null)
            {
                bolt.OnBoltLoosened += HandleBoltLoosened;
                // Bolt 스크립트에도 현재 모드를 전달 (UI 문구 변경용)
                bolt.isAssembleMode = this.assembleMode;
            }
        }
        RegisterBroadcast();
    }

    private void OnDestroy()
    {
        foreach(var bolt in _bolts)
        {
            if(bolt != null) bolt.OnBoltLoosened -= HandleBoltLoosened;
        }
        UnregisterBroadcast();
    }

    private void RegisterBroadcast()
    {
        InstanceFinder.ServerManager?.RegisterBroadcast<BoltHideBroadcast>(OnServerHide);
        InstanceFinder.ClientManager?.RegisterBroadcast<BoltProgressBroadcast>(OnClientProgress);
        InstanceFinder.ClientManager?.RegisterBroadcast<BoltFallBroadcast>(OnClientBoltFall);
    }

    private void UnregisterBroadcast()
    {
        InstanceFinder.ServerManager?.UnregisterBroadcast<BoltHideBroadcast>(OnServerHide);
        InstanceFinder.ClientManager?.UnregisterBroadcast<BoltProgressBroadcast>(OnClientProgress);
        InstanceFinder.ClientManager?.UnregisterBroadcast<BoltFallBroadcast>(OnClientBoltFall);
    }

    // 볼트가 100% 풀리거나(분해) 100% 조여졌을 때(조립) 호출
    private void HandleBoltLoosened(Bolt bolt)
    {
        if(_completed) return;

        int idx = _bolts.IndexOf(bolt);
        if(idx == -1 || _removedIndices.Contains(idx)) return;

        // [로컬] 애니메이션/사운드 실행
        var ca = bolt.GetComponent<ClickableAnimator>();
        if(ca != null) ca.Open();

        if(InstanceFinder.IsClientStarted)
        {
            // 서버에 이 볼트 작업 끝났다고 보고
            var msg = new BoltHideBroadcast { groupId = groupId, boltIndex = idx };
            InstanceFinder.ClientManager.Broadcast(msg);
        }
    }

    private void OnServerHide(FishNet.Connection.NetworkConnection conn, BoltHideBroadcast msg, Channel ch)
    {
        if(msg.groupId != groupId) return;
        if(_removedIndices.Contains(msg.boltIndex)) return;

        _removedIndices.Add(msg.boltIndex);

        if(assembleMode)
        {
            // [조립 모드] 볼트를 삭제하지 않고 카운트만 업데이트
            Debug.Log($"[BoltGroup] 볼트 {msg.boltIndex}번 조립 완료");
            ApplyCountAndCheckCompletion();
        }
        else
        {
            // [분해 모드] 기존 낙하 연출 및 삭제 로직 실행
            InstanceFinder.ServerManager.Broadcast(new BoltFallBroadcast
            {
                groupId = groupId,
                boltIndex = msg.boltIndex
            });
            StartCoroutine(DespawnAfterDelay(msg.boltIndex));
        }
    }

    private IEnumerator DespawnAfterDelay(int boltIndex)
    {
        yield return new WaitForSeconds(ejectDuration);

        if(boltIndex >= 0 && boltIndex < _bolts.Count)
        {
            var boltObj = _bolts[boltIndex];
            if(boltObj != null && InstanceFinder.IsServerStarted)
            {
                InstanceFinder.ServerManager.Despawn(boltObj.gameObject);
            }
        }
        ApplyCountAndCheckCompletion();
    }

    private void ApplyCountAndCheckCompletion()
    {
        _removedCount = _removedIndices.Count;

        if(InstanceFinder.IsServerStarted)
        {
            InstanceFinder.ServerManager.Broadcast(new BoltProgressBroadcast
            {
                groupId = groupId,
                removed = _removedCount,
                total = _totalCount
            });

            var runner = Object.FindFirstObjectByType<ScenarioRunner>();
            string currentModuleId = runner != null ? runner.CurrentModuleId : "Runner 없음";
            Debug.Log($"[BoltCheck] {groupId} ({_removedCount}/{_totalCount}) | Mode: {(assembleMode ? "조립" : "분해")} | Task: {taskId}");
        }

        if(_removedCount >= _totalCount && !_completed)
        {
            _completed = true;
            if(InstanceFinder.IsServerStarted)
            {
                if(!string.IsNullOrEmpty(taskId))
                    InteractionEvents.FireTaskConfirmed(taskId);

                // 조립 모드에서는 그룹 루트를 삭제하지 않음 (차에 붙어 있어야 하므로)
                if(!assembleMode)
                {
                    StartCoroutine(DespawnGroupRoot());
                }
            }
        }
    }

    private IEnumerator DespawnGroupRoot()
    {
        yield return new WaitForSeconds(1.5f);
        if(gameObject != null && InstanceFinder.IsServerStarted)
            InstanceFinder.ServerManager.Despawn(gameObject);
    }

    private void OnClientBoltFall(BoltFallBroadcast msg, Channel ch)
    {
        if(msg.groupId != groupId) return;
        var bolt = _bolts[msg.boltIndex];
        if(bolt == null) return;

        Rigidbody rb = bolt.GetComponent<Rigidbody>();
        if(rb == null) rb = bolt.gameObject.AddComponent<Rigidbody>();

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.AddForce(Vector3.down * 2f, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * 5f, ForceMode.Impulse);
    }

    private void OnClientProgress(BoltProgressBroadcast msg, Channel ch)
    {
        if(msg.groupId != groupId) return;
        _removedCount = msg.removed;
    }

    public void ResetCounter()
    {
        _removedCount = 0;
        _completed = false;
        _removedIndices.Clear();
    }

    public int RemovedCount => _removedCount;
    public int TotalCount => _totalCount;
}