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

    [Header("★ 완료 시 디스폰할 오브젝트")]
    [Tooltip("모든 볼트가 처리되면 이 오브젝트를 서버에서 디스폰 (동적 생성 부품)")]
    public GameObject targetDespawnObject;
    [Tooltip("디스폰 전 대기 시간 (초)")]
    public float despawnDelay = 1.5f;

    [Header("상태 (읽기 전용)")]
    [SerializeField] private int _totalCount;
    [SerializeField] private int _removedCount;

    private List<Bolt> _bolts = new();
    private HashSet<int> _removedIndices = new();
    private bool _completed = false;
    private VehiclePartsManager _vehicleManager;

    private void Awake()
    {
        _bolts.Clear();
        _bolts.AddRange(GetComponentsInChildren<Bolt>());
        _totalCount = _bolts.Count;
        _vehicleManager = GetComponentInParent<VehiclePartsManager>();

        Debug.Log($"[BoltGroup] Awake: {groupId} - {_totalCount}개 볼트 발견");
    }

    private void Start()
    {
        // VehiclePartsManager에서 부품 자동 할당
        if(_vehicleManager == null)
        {
            _vehicleManager = GetComponentInParent<VehiclePartsManager>();
        }

        if(targetDespawnObject == null && _vehicleManager != null)
        {
            targetDespawnObject = _vehicleManager.GetPartByBoltGroup(this);
            if(targetDespawnObject != null)
            {
                Debug.Log($"[BoltGroup] ✓ 차량 매니저에서 부품 찾음: {targetDespawnObject.name}");
            }
            else
            {
                Debug.LogWarning($"[BoltGroup] ⚠️ 차량 매니저에서 부품을 찾을 수 없음 (groupId: {groupId})");
            }
        }

        foreach(var bolt in _bolts)
        {
            if(bolt != null)
            {
                bolt.OnBoltLoosened += HandleBoltLoosened;
                bolt.isAssembleMode = this.assembleMode;
            }
        }
        RegisterBroadcast();

        Debug.Log($"[BoltGroup] Start 완료: {groupId}");
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
        Debug.Log($"[BoltGroup] HandleBoltLoosened 호출: {bolt.name}");

        if(_completed)
        {
            Debug.LogWarning($"[BoltGroup] 이미 완료됨 - 무시");
            return;
        }

        int idx = _bolts.IndexOf(bolt);
        if(idx == -1)
        {
            Debug.LogError($"[BoltGroup] ❌ 볼트를 리스트에서 찾을 수 없음: {bolt.name}");
            return;
        }

        if(_removedIndices.Contains(idx))
        {
            Debug.LogWarning($"[BoltGroup] 볼트 {idx}번은 이미 처리됨");
            return;
        }

        // [로컬] 애니메이션/사운드 실행
        var ca = bolt.GetComponent<ClickableAnimator>();
        if(ca != null)
        {
            ca.Open();
            Debug.Log($"[BoltGroup] 볼트 {idx}번 애니메이션 실행");
        }

        if(InstanceFinder.IsClientStarted)
        {
            // 서버에 이 볼트 작업 끝났다고 보고
            var msg = new BoltHideBroadcast { groupId = groupId, boltIndex = idx };
            InstanceFinder.ClientManager.Broadcast(msg);
            Debug.Log($"[BoltGroup] 서버에 보고: groupId={groupId}, boltIndex={idx}");
        }
    }

    private void OnServerHide(FishNet.Connection.NetworkConnection conn, BoltHideBroadcast msg, Channel ch)
    {
        Debug.Log($"[BoltGroup] OnServerHide 수신: groupId={msg.groupId}, boltIndex={msg.boltIndex}");

        if(msg.groupId != groupId)
        {
            Debug.Log($"[BoltGroup] groupId 불일치 (받은: {msg.groupId}, 내: {groupId}) → 무시");
            return;
        }

        if(_removedIndices.Contains(msg.boltIndex))
        {
            Debug.Log($"[BoltGroup] 볼트 {msg.boltIndex}는 이미 처리됨 → 무시");
            return;
        }

        _removedIndices.Add(msg.boltIndex);
        Debug.Log($"[BoltGroup] {groupId}: 볼트 {msg.boltIndex}번 추가 완료 → 현재: {_removedIndices.Count}/{_totalCount}");

        if(assembleMode)
        {
            Debug.Log($"[BoltGroup] {groupId} 조립 모드 → ApplyCountAndCheckCompletion 호출");
            ApplyCountAndCheckCompletion();
        }
        else
        {
            Debug.Log($"[BoltGroup] {groupId} 분해 모드 → DespawnAfterDelay 시작");
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
        Debug.Log($"[BoltGroup] DespawnAfterDelay 시작: boltIndex={boltIndex}, 대기시간={ejectDuration}초");
        yield return new WaitForSeconds(ejectDuration);
        Debug.Log($"[BoltGroup] {ejectDuration}초 대기 완료 → 볼트 {boltIndex}번 디스폰");

        if(boltIndex >= 0 && boltIndex < _bolts.Count)
        {
            var boltObj = _bolts[boltIndex];
            if(boltObj != null && InstanceFinder.IsServerStarted)
            {
                Debug.Log($"[BoltGroup] 볼트 {boltIndex}번 디스폰: {boltObj.name}");
                InstanceFinder.ServerManager.Despawn(boltObj.gameObject);
            }
            else
            {
                Debug.LogError($"[BoltGroup] ❌ 볼트 디스폰 실패: boltObj={boltObj}, IsServer={InstanceFinder.IsServerStarted}");
            }
        }
        else
        {
            Debug.LogError($"[BoltGroup] ❌ boltIndex 범위 초과: {boltIndex} >= {_bolts.Count}");
        }

        Debug.Log($"[BoltGroup] ApplyCountAndCheckCompletion 호출 (DespawnAfterDelay 후)");
        ApplyCountAndCheckCompletion();
    }

    private void ApplyCountAndCheckCompletion()
    {
        _removedCount = _removedIndices.Count;
        Debug.Log($"[BoltGroup] ApplyCountAndCheckCompletion: {_removedCount}/{_totalCount} 완료됨");

        if(InstanceFinder.IsServerStarted)
        {
            InstanceFinder.ServerManager.Broadcast(new BoltProgressBroadcast
            {
                groupId = groupId,
                removed = _removedCount,
                total = _totalCount
            });

            var runner = UnityEngine.Object.FindFirstObjectByType<ScenarioRunner>();
            string currentModuleId = runner != null ? runner.CurrentModuleId : "Runner 없음";
            Debug.Log($"[BoltCheck] {groupId} ({_removedCount}/{_totalCount}) | Mode: {(assembleMode ? "조립" : "분해")} | Task: {taskId}");
        }

        // ★ 완료 조건 체크
        Debug.Log($"[BoltGroup] 완료 조건 체크: _removedCount({_removedCount}) >= _totalCount({_totalCount}) && !_completed({!_completed})");

        if(_removedCount >= _totalCount)
        {
            _completed = true;
            Debug.Log($"[BoltGroup] ========== 모든 볼트 완료! ==========");

            if(InstanceFinder.IsServerStarted)
            {
                 //★ Task 완료 신호 발신
                if(!string.IsNullOrEmpty(taskId))
                {
                    InteractionEvents.FireTaskConfirmed(taskId);
                    Debug.Log($"[BoltGroup] ✅ Task '{taskId}' 완료 신호 발신");
                }

                // ★ 타겟 부품 디스폰 상태 확인
                Debug.Log($"[BoltGroup] targetDespawnObject: {(targetDespawnObject != null ? targetDespawnObject.name : "NULL")}");

                if(targetDespawnObject != null)
                {
                    Debug.Log($"[BoltGroup] 🗑️ DespawnTargetObject 코루틴 시작 (delay={despawnDelay}초)");
                    StartCoroutine(DespawnTargetObject());
                }
                else
                {
                    Debug.LogError($"[BoltGroup] ❌❌❌ targetDespawnObject가 NULL입니다! 할당되지 않음!!");
                }

                // 조립 모드에서는 그룹 루트를 삭제하지 않음 (차에 붙어 있어야 하므로)
                if(!assembleMode)
                {
                    Debug.Log($"[BoltGroup] 분해 모드 → DespawnGroupRoot 코루틴 시작");
                    StartCoroutine(DespawnGroupRoot());
                }
            }
        }
        else
        {
            Debug.Log($"[BoltGroup] 아직 완료 안 됨 (조건 불만족)");
        }
    }

    /// <summary>
    /// 지정된 타겟 오브젝트를 서버에서 디스폰 (동적 생성 부품용)
    /// </summary>
    private IEnumerator DespawnTargetObject()
    {
        Debug.Log($"[BoltGroup] DespawnTargetObject 코루틴 시작: {targetDespawnObject.name}, 대기={despawnDelay}초");
        yield return new WaitForSeconds(despawnDelay);
        Debug.Log($"[BoltGroup] {despawnDelay}초 대기 완료");

        if(targetDespawnObject != null && InstanceFinder.IsServerStarted)
        {
            Debug.Log($"[BoltGroup] 🗑️🗑️ 부품 디스폰 실행: {targetDespawnObject.name}");
            InstanceFinder.ServerManager.Despawn(targetDespawnObject);
        }
        else
        {
            Debug.LogError($"[BoltGroup] ❌ 부품 디스폰 실패: targetDespawnObject={targetDespawnObject}, IsServer={InstanceFinder.IsServerStarted}");
        }
    }

    private IEnumerator DespawnGroupRoot()
    {
        Debug.Log($"[BoltGroup] DespawnGroupRoot 코루틴 시작: {gameObject.name}, 대기={despawnDelay}초");
        yield return new WaitForSeconds(despawnDelay);
        Debug.Log($"[BoltGroup] {despawnDelay}초 대기 완료");

        if(gameObject != null && InstanceFinder.IsServerStarted)
        {
            Debug.Log($"[BoltGroup] 🗑️🗑️ 볼트 그룹 디스폰: {gameObject.name}");
            InstanceFinder.ServerManager.Despawn(gameObject);
        }
        else
        {
            Debug.LogError($"[BoltGroup] ❌ 그룹 디스폰 실패: gameObject={gameObject}, IsServer={InstanceFinder.IsServerStarted}");
        }
    }

    private void OnClientBoltFall(BoltFallBroadcast msg, Channel ch)
    {
        if(msg.groupId != groupId) return;

        if(msg.boltIndex >= _bolts.Count || msg.boltIndex < 0)
        {
            Debug.LogError($"[BoltGroup] ❌ boltIndex 범위 초과: {msg.boltIndex}");
            return;
        }

        var bolt = _bolts[msg.boltIndex];
        if(bolt == null)
        {
            Debug.LogError($"[BoltGroup] ❌ 볼트 null: index={msg.boltIndex}");
            return;
        }

        Rigidbody rb = bolt.GetComponent<Rigidbody>();
        if(rb == null) rb = bolt.gameObject.AddComponent<Rigidbody>();

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.AddForce(Vector3.down * 2f, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * 5f, ForceMode.Impulse);

        Debug.Log($"[BoltGroup] 볼트 {msg.boltIndex}번 낙하 애니메이션 실행");
    }

    private void OnClientProgress(BoltProgressBroadcast msg, Channel ch)
    {
        if(msg.groupId != groupId) return;
        _removedCount = msg.removed;
        Debug.Log($"[BoltGroup] 클라이언트 진행도 업데이트: {_removedCount}/{msg.total}");
    }

    public void ResetCounter()
    {
        _removedCount = 0;
        _completed = false;
        _removedIndices.Clear();
        Debug.Log($"[BoltGroup] {groupId} 리셋");
    }

    public int RemovedCount => _removedCount;
    public int TotalCount => _totalCount;

    // ★ 진행도 텍스트 반환
    public string GetProgressText()
    {
        return $"{TotalCount - RemovedCount}/{TotalCount}";
    }
}
