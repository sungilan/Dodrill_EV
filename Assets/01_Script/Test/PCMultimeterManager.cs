using UnityEngine;
using FishNet.Object;

public class PCMultimeterManager : MonoBehaviour
{
    [Header("참조")]
    public MultimeterMaster master;
    public MultimeterProbeSync blackProbeSync;
    public MultimeterProbeSync redProbeSync;

    [Header("레이어 설정")]
    public LayerMask terminalLayer;

    private SyncGrab _bodySyncGrab;
    private int _clickCount = 0;

    void Update()
    {
        // 1. 본체가 아직 연결되지 않았다면 자동 탐색 시도
        if(master == null || _bodySyncGrab == null)
        {
            FindSpawnedMultimeter();
            return; // 찾을 때까지 로직 중단
        }

        // 2. 멀티미터 본체를 내가 들고 있을 때만 작동
        if(!_bodySyncGrab.IsGrabbed || !_bodySyncGrab.IsOwner)
        {
            if(_clickCount > 0) ResetProbes();
            return;
        }

        // 3. 입력 처리
        if(Input.GetMouseButtonDown(0)) HandleClick();
        if(Input.GetMouseButtonDown(1)) ResetProbes();
    }

    /// <summary>
    /// 씬에 스폰된 멀티미터 본체를 찾아 컴포넌트를 연결합니다.
    /// </summary>
    private void FindSpawnedMultimeter()
    {
        // 씬에서 MultimeterMaster를 가진 오브젝트 검색
        var foundMaster = Object.FindFirstObjectByType<MultimeterMaster>();

        if(foundMaster != null)
        {
            master = foundMaster;
            _bodySyncGrab = master.GetComponent<SyncGrab>(); //

            // 본체 자식으로 붙어있는 프로브 동기화 컴포넌트들도 자동 연결
            blackProbeSync = master.blackProbe.GetComponent<MultimeterProbeSync>();
            redProbeSync = master.redProbe.GetComponent<MultimeterProbeSync>();

            Debug.Log($"[PC-Multimeter] 스폰된 멀티미터 감지 및 연결 완료: {master.name}");
        }
    }

    private void HandleClick()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if(Physics.Raycast(ray, out RaycastHit hit, 10f, terminalLayer))
        {
            var point = hit.collider.GetComponent<MeasurementPoint>();
            if(point == null) return;

            if(_clickCount == 0)
            {
                blackProbeSync.FlyToTerminal(point, hit.point, hit.normal);
                _clickCount = 1;
                Debug.Log($"[PC-Multimeter] 검은색 프로브 발사: {point.terminalId}");
            }
            else if(_clickCount == 1)
            {
                redProbeSync.FlyToTerminal(point, hit.point, hit.normal);
                _clickCount = 2;
                Debug.Log($"[PC-Multimeter] 빨간색 프로브 발사: {point.terminalId}");
            }
        }
    }

    private void ResetProbes()
    {
        if(_clickCount == 0) return;

        _clickCount = 0;
        if(blackProbeSync != null) blackProbeSync.ReturnToSocket();
        if(redProbeSync != null) redProbeSync.ReturnToSocket();

        Debug.Log("[PC-Multimeter] 모든 프로브 회수 시퀀스 시작");
    }
}