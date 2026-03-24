using UnityEngine;
using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;

public class VehicleSpawner : NetworkBehaviour
{
    public Transform spawnPoint;        // 차량이 소환될 위치
    public GameObject selectedPrefab;   // 유저가 선택한 차량 프리팹 (PrefabManager에 등록 필수)

    // 피쉬넷 4.0 방식의 SyncVar 선언
    private readonly SyncVar<GameObject> _spawnedVehicle = new SyncVar<GameObject>();

    // 서버가 시작될 때 자동으로 스폰하고 싶다면 OnStartServer를 사용합니다.
    public override void OnStartServer()
    {
        base.OnStartServer();
        SpawnAndInitialize();
    }

    [Server] // 서버 권위로만 실행
    public void SpawnAndInitialize()
    {
        if(selectedPrefab == null || spawnPoint == null) return;

        // 1. 차량 생성 및 네트워크 스폰
        GameObject vehicle = Instantiate(selectedPrefab, spawnPoint.position, spawnPoint.rotation);

        // 피쉬넷 서버 매니저를 통한 스폰 (이때 모든 클라이언트에 생성됨)
        InstanceFinder.ServerManager.Spawn(vehicle);

        vehicle.name = "Target_Vehicle";
        _spawnedVehicle.Value = vehicle;

        // 2. 서버 측 매니저에 부품 등록
        // (주의: 클라이언트에서도 등록이 필요하다면 별도의 로직이나 각 부품의 OnStartClient 활용 권장)
        RegisterPartsOnServer(vehicle);
    }

    [Server]
    private void RegisterPartsOnServer(GameObject vehicle)
    {
        // 소환된 차량 내부의 모든 InteractablePart 찾기
        InteractablePart[] parts = vehicle.GetComponentsInChildren<InteractablePart>();

        // 3. DisassemblyManager(서버 측)에 리스트 전달
        if(DisassemblyManager.Instance != null)
        {
            DisassemblyManager.Instance.allParts = new List<InteractablePart>(parts);
            Debug.Log($"<color=cyan>[서버 전용]</color> {parts.Length}개의 정비 부품이 등록되었습니다.");
        }
    }
}