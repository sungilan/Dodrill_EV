using UnityEngine;
using System.Collections.Generic;

namespace ER.Selection
{
    public class VehicleSelectionController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private VehicleSpinner spinner;

        [Header("Vehicle Database")]
        [SerializeField] private List<VehicleData> vehicleList = new List<VehicleData>();

        private int _currentIndex = 0;

        private void Start()
        {
            Debug.Log("<color=cyan>[System]</color> 차량 선택 컨트롤러 초기화");

            // 1. 이벤트 리스너 등록 (UIManager가 Awake에서 Instance를 만든 후 실행됨)
            if(UIManager.Instance != null)
            {
                UIManager.Instance.OnVehicleSelected.AddListener(OnConfirmSelection);
            }

            // 2. 초기 데이터 로드
            if(vehicleList != null && vehicleList.Count > 0)
            {
                _currentIndex = 0;
                UpdateDisplay();
            }
        }

        public void NextVehicle()
        {
            if(vehicleList.Count == 0) return;
            _currentIndex = (_currentIndex + 1) % vehicleList.Count;
            UpdateDisplay();
        }

        public void PreviousVehicle()
        {
            if(vehicleList.Count == 0) return;
            _currentIndex = (_currentIndex - 1 + vehicleList.Count) % vehicleList.Count;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            var data = vehicleList[_currentIndex];

            if(spinner != null) spinner.SpawnVehicle(data.modelPrefab);

            // 통합된 UIManager를 통해 UI 갱신
            if(UIManager.Instance != null)
            {
                UIManager.Instance.UpdateUI(data);
            }
        }

        public void OnConfirmSelection(VehicleData selectedData)
        {
            StartTraining(selectedData);
        }

        private void StartTraining(VehicleData data)
        {
            // 1. 전시용 모델 삭제 (부모인 Spinner가 꺼지기 전에 삭제하는 게 안전합니다)
            if(spinner != null)
            {
                spinner.ClearModel();
            }

            // 2. StartupManager에 훈련 시작 알림 (여기서 Spawner가 새 모델 생성)
            if(StartupManager.Instance != null)
            {
                StartupManager.Instance.InitializeTraining(data);
            }

            // 3. 선택 UI 오브젝트 비활성화 (이제 마음놓고 꺼도 됩니다)
            this.gameObject.SetActive(false);
        }
    }
}