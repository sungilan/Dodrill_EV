//using DG.Tweening;
//using DoDrill;
//using FishNet;
//using FishNet.Object;
//using FishNet.Object.Synchronizing;
//using System.Collections;
//using UnityEngine;

//public class BatteryLiftController : LiftControllerBase
//{
//    [Header("── TransformBased 전용 ──")]
//    public Transform liftPlate;
//    public float maxHeight = 0.5f;

//    [Header("── 배터리팩 연동 ──")]
//    public Transform batteryPackTransform;

//    [Header("배터리 상태")]
//    private readonly SyncVar<bool> _syncBatteryAttached = new SyncVar<bool>();
//    public bool IsBatteryAttached => _syncBatteryAttached.Value;

//    private Vector3 _plateOrigin;
//    private Vector3 _batteryOriginWorldPos;

//    protected override float GetMaxHeight() => maxHeight;

//    public override void OnStartClient()
//    {
//        base.OnStartClient();
//        _syncBatteryAttached.OnChange += OnBatteryAttachmentChanged;
//    }

//    public override void OnStopClient()
//    {
//        // 1. 부모 클래스의 종료 로직 실행 (이벤트 해제 등)
//        base.OnStopClient();

//        // 2. ★ 중요: 이 객체가 파괴될 때 실행 중인 모든 코루틴을 즉시 중단
//        // 삭제된 유령 객체가 서버에 RPC를 보내어 강제 킥 당하는 것을 방지함
//        StopAllCoroutines();

//        _syncBatteryAttached.OnChange -= OnBatteryAttachmentChanged;
//        Debug.Log("<color=red>[BatteryLift]</color> OnStopClient: 모든 프로세스 및 코루틴 종료");
//    }

//    private void OnBatteryAttachmentChanged(bool prev, bool next, bool asServer)
//    {
//        string status = next ? "<color=green>강제 부착됨</color>" : "<color=red>분리됨</color>";
//        Debug.Log($"<color=lime>[BatteryLift]</color> 배터리 상태 동기화: {status}");
//    }

//    protected override void OnLiftStart()
//    {
//        if(liftMode == LiftMode.TransformBased)
//        {
//            if(liftAnimator != null) liftAnimator.enabled = false;
//            if(liftPlate != null) _plateOrigin = liftPlate.localPosition;
//        }

//        // 호스트나 서버에서만 탐색을 시작하거나, 클라이언트라면 초기화 확인 후 시작
//        if(IsClientInitialized)
//        {
//            Debug.Log("<color=cyan>[BatteryLift]</color> ✓ OnLiftStart: 배터리 자동 탐색 시작");
//            StartCoroutine(AutoSearchAndAttachBattery());
//        }
//    }

//    private IEnumerator AutoSearchAndAttachBattery()
//    {
//        var wait = new WaitForSeconds(0.5f);
//        int attemptCount = 0;

//        while(true)
//        {
//            // ★ 중요: 객체가 네트워크에서 해제(Despawn) 중이거나 종료되었다면 루프 즉시 탈출
//            if(!IsClientInitialized || IsDeinitializing) yield break;

//            attemptCount++;
//            var battery = Object.FindFirstObjectByType<Battery>();

//            if(battery != null)
//            {
//                NetworkObject batteryNetObj = battery.GetComponent<NetworkObject>();

//                // ★ 중요: RPC를 쏘기 직전에 내가 아직 서버와 연결된 유효한 상태인지 재확인
//                if(batteryNetObj != null && IsClientInitialized && !IsDeinitializing)
//                {
//                    Debug.Log($"<color=lime>[BatteryLift]</color> ✓ 배터리 발견: {battery.name}. RPC 호출 시도.");
//                    ServerAttachBatteryRpc(batteryNetObj);
//                    yield break; // 성공했으므로 루프 종료
//                }
//            }

//            if(attemptCount % 10 == 0)
//                Debug.Log($"<color=yellow>[BatteryLift]</color> 배터리 탐색 중... (시도 #{attemptCount})");

//            yield return wait;
//        }
//    }

//    protected override void ApplyHeightTransform(float h)
//    {
//        if(liftPlate != null)
//        {
//            liftPlate.localPosition = _plateOrigin + Vector3.up * h;
//        }
//    }

//    protected override void SyncPayloadLateUpdate() { }

//    protected override void OnHeightChanged(float newHeight, float prevHeight) { }

//    protected override void OnReachedBottom()
//    {
//        if(IsServerInitialized && IsBatteryAttached)
//        {
//            Debug.Log("<color=orange>[BatteryLift]</color> 최하단 도달: 배터리 분리");
//            ServerDetachBattery();
//        }
//    }

//    [ServerRpc(RequireOwnership = false)]
//    private void ServerAttachBatteryRpc(NetworkObject batteryNetObj)
//    {
//        // 서버에서도 전달받은 오브젝트가 유효한지 확인
//        if(batteryNetObj == null || !InstanceFinder.IsServerStarted) return;

//        Transform batteryTransform = batteryNetObj.transform;
//        _batteryOriginWorldPos = batteryTransform.position;

//        var rb = batteryTransform.GetComponent<Rigidbody>();
//        if(rb != null)
//        {
//            rb.isKinematic = true;
//            rb.linearVelocity = Vector3.zero;
//            rb.angularVelocity = Vector3.zero;
//        }

//        var lnt = batteryTransform.GetComponent<LocalNetworkTransform>();
//        if(lnt != null) lnt.enabled = false;

//        batteryTransform.SetParent(liftPlate);
//        batteryTransform.localPosition = liftPlate.InverseTransformPoint(_batteryOriginWorldPos);
//        batteryPackTransform = batteryTransform;

//        _syncBatteryAttached.Value = true;
//        InteractionEvents.FireZoneActivated("BatteryJack_Attached", "BatteryJack");

//        Debug.Log($"<color=lime>[BatteryLift]</color> ★ 배터리 부착 완료!");
//    }

//    [ServerRpc(RequireOwnership = false)]
//    private void ServerDetachBattery()
//    {
//        if(batteryPackTransform == null) return;

//        Vector3 detachWorldPos = batteryPackTransform.position;
//        batteryPackTransform.SetParent(null);
//        batteryPackTransform.position = detachWorldPos;

//        var rb = batteryPackTransform.GetComponent<Rigidbody>();
//        if(rb != null) rb.isKinematic = false;

//        var lnt = batteryPackTransform.GetComponent<LocalNetworkTransform>();
//        if(lnt != null) lnt.enabled = true;

//        _syncBatteryAttached.Value = false;
//        InteractionEvents.FireZoneActivated("BatteryJack_Detached", "BatteryJack");
//    }
//}

using DG.Tweening;
using DoDrill;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using System.Collections;
using UnityEngine;

public class BatteryLiftController : LiftControllerBase
{
    [Header("── 리프트 상판 설정 ──")]
    public Transform liftPlate;
    public float maxHeight = 0.5f;

    private Vector3 _plateOrigin;

    protected override float GetMaxHeight() => maxHeight;

    public override void OnStartClient()
    {
        base.OnStartClient();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        // 실행 중인 코루틴이 있다면 안전하게 종료
        StopAllCoroutines();
    }

    protected override void OnLiftStart()
    {
        // 리프트 모드 설정 및 초기 위치 저장
        if(liftMode == LiftMode.TransformBased)
        {
            if(liftAnimator != null) liftAnimator.enabled = false;
            if(liftPlate != null) _plateOrigin = liftPlate.localPosition;
        }

        Debug.Log("<color=cyan>[BatteryLift]</color> 리프트 초기화 완료. (배터리 자동 부착 기능 비활성화)");
    }

    protected override void ApplyHeightTransform(float h)
    {
        // 리프트 상판 이동 (로컬 Y축)
        if(liftPlate != null)
        {
            liftPlate.localPosition = _plateOrigin + Vector3.up * h;
        }
    }

    protected override void SyncPayloadLateUpdate()
    {
        // 추가 동기화 필요 시 작성
    }

    protected override void OnHeightChanged(float newHeight, float prevHeight)
    {
        // 높이 변화에 따른 추가 로직이 필요 없다면 비워둡니다.
    }

    protected override void OnReachedBottom()
    {
        // 리프트가 바닥에 도달했을 때의 로직
        Debug.Log("<color=orange>[BatteryLift]</color> 리프트가 최하단에 도달했습니다.");
    }

    protected override void OnReachedTop()
    {
        // 리프트가 최고점에 도달했을 때의 로직
        Debug.Log("<color=orange>[BatteryLift]</color> 리프트가 최고점에 도달했습니다.");
    }
}