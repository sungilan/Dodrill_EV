using UnityEngine;
using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace ER.Selection
{
    public class VehicleSpinner : NetworkBehaviour
    {
        [Header("Rotation Settings")]
        [SerializeField] private float rotationSpeed = 15.0f;

        [Header("Spawn Settings")]
        [SerializeField] private Transform spawnPoint;

        // ★ 피쉬넷 4.0 방식: [SyncVar] 대신 readonly SyncVar<T> 사용
        // 선언 시점에 반드시 초기화해야 하며, 값 수정은 .Value를 통해 합니다.
        private readonly SyncVar<GameObject> _currentVehicleModel = new SyncVar<GameObject>();

        private void Update()
        {
            // SyncVar의 값을 읽을 때는 .Value를 사용합니다.
            if(_currentVehicleModel.Value != null)
            {
                _currentVehicleModel.Value.transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
            }
        }

        [Server]
        public void SpawnVehicle(GameObject vehiclePrefab)
        {
            ClearModel();

            if(vehiclePrefab != null && spawnPoint != null)
            {
                GameObject instance = Instantiate(vehiclePrefab, spawnPoint.position, spawnPoint.rotation);

                // 부모 설정
                instance.transform.SetParent(spawnPoint);

                // 네트워크 스폰
                InstanceFinder.ServerManager.Spawn(instance);

                // ★ 값 할당 시 .Value 사용
                _currentVehicleModel.Value = instance;
            }
        }

        [Server]
        public void ClearModel()
        {
            if(_currentVehicleModel.Value != null)
            {
                InstanceFinder.ServerManager.Despawn(_currentVehicleModel.Value);
                _currentVehicleModel.Value = null;
            }
        }
    }
}