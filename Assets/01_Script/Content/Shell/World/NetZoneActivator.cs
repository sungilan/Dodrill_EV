using UnityEngine;
using FishNet.Object;

public class NetZoneActivator : NetworkBehaviour
{
    [Header("연동 설정")]
    public string targetZoneId = "OBD_Zone";
    public string requiredItemId = "GDS_Tablet";

    [Header("활성화 대상")]
    public GameObject visualObject;

    private void OnEnable()
    {
        InteractionEvents.OnZoneActivated += HandleZoneActivated;
    }

    private void OnDisable()
    {
        InteractionEvents.OnZoneActivated -= HandleZoneActivated;
    }

    private void HandleZoneActivated(string zoneId, string itemId)
    {
        // 1. 조건을 만족하는지 로컬에서 먼저 체크
        if(zoneId == targetZoneId && itemId == requiredItemId)
        {
            // 2. 서버에 알림 (서버만 RPC를 쏠 권한이 있음)
            RequestActivateServer();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestActivateServer()
    {
        // 3. 서버가 모든 클라이언트에게 "이거 켜!"라고 명령함
        RpcSetVisualActive(true);
    }

    [ObserversRpc]
    private void RpcSetVisualActive(bool isActive)
    {
        // 4. 모든 플레이어의 화면에서 실행됨
        if(visualObject != null)
        {
            visualObject.SetActive(isActive);
            Debug.Log($"🌐 [NetZone] 모두의 화면에서 {visualObject.name} 상태 변경: {isActive}");
            Debug.Log($"실제 하이어라키 상태: {visualObject.activeInHierarchy}");
        }
    }
}