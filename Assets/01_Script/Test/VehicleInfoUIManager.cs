using UnityEngine;
using TMPro; // TextMeshPro 라이브러리 필수
using UnityEngine.UI;
using UnityEngine.Events;

namespace ER.Selection
{
    // 차량 데이터를 담는 간단한 구조체 (SO로 대체 가능)
    [System.Serializable]
    public struct VehicleData
    {
        public string modelName;
        public string batteryCapacity; // 예: "77.4 kWh"
        public string driveType;       // 예: "AWD (사륜구동)"
        public string maxRange;        // 예: "480 km"
        //public string description;
        public GameObject modelPrefab; // Spinner가 사용할 프리팹
    }

    public class VehicleInfoUIManager : MonoBehaviour
    {
        [Header("UI Components (TextMeshPro)")]
        [SerializeField] private TextMeshProUGUI modelNameText;
        [SerializeField] private TextMeshProUGUI batteryTypeText;
        [SerializeField] private TextMeshProUGUI driveTypeText;
        [SerializeField] private TextMeshProUGUI rangeText;

        [Header("Interaction Components")]
        [SerializeField] private Button selectButton; // VR 레이저로 클릭할 버튼

        [Header("Events")]
        [Tooltip("사용자가 '선택 완료' 버튼을 눌렀을 때 발생")]
        public UnityEvent<VehicleData> OnVehicleSelected;

        private VehicleData _currentData;

        private void Awake()
        {
            // 1. 버튼 리스너 등록
            if(selectButton != null)
            {
                selectButton.onClick.AddListener(ConfirmSelection);
            }
        }

        /// <summary>
        /// UI 데이터를 갱신합니다.
        /// </summary>
        /// <param name="data">표시할 차량 데이터</param>
        public void UpdateUI(VehicleData data)
        {
            _currentData = data;

            // 2. TMP 컴포넌트에 데이터 주입
            if(modelNameText != null) modelNameText.text = data.modelName;
            if(batteryTypeText != null) batteryTypeText.text = $"배터리: {data.batteryCapacity}";
            if(driveTypeText != null) driveTypeText.text = $"구동: {data.driveType}";
            if(rangeText != null) rangeText.text = $"주행거리: {data.maxRange}";
        }

        /// <summary>
        /// 사용자가 선택 완료 버튼을 눌렀을 때 호출됩니다.
        /// </summary>
        private void ConfirmSelection()
        {
            Debug.Log($"<color=lime>[Selection]</color> {_currentData.modelName} 선택 완료!");

            // 3. 시나리오 매니저 등에 선택된 데이터를 넘겨주며 훈련 시작
            OnVehicleSelected?.Invoke(_currentData);
        }
    }
}