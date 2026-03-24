using ER.Selection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [Header("Training UI (Progress)")]
    public TextMeshProUGUI progressText;
    public Slider progressBar;
    public GameObject completionPanel;

    [Header("Selection UI (Vehicle Info)")]
    public TextMeshProUGUI modelNameText;
    public TextMeshProUGUI batteryTypeText;
    public TextMeshProUGUI driveTypeText;
    public TextMeshProUGUI rangeText;
    public Button selectButton; // 인스펙터에서 '선택' 버튼 연결

    [Header("Events")]
    [HideInInspector]
    public UnityEvent<VehicleData> OnVehicleSelected = new UnityEvent<VehicleData>();

    private VehicleData _currentDisplayData;
    private bool _hasData = false;

    void Awake()
    {
        // 싱글톤 초기화
        if(Instance == null) { Instance = this; }
        else { Destroy(gameObject); return; }

        if(completionPanel != null) completionPanel.SetActive(false);

        // 버튼 리스너 등록
        if(selectButton != null)
        {
            selectButton.onClick.AddListener(ConfirmSelection);
        }
    }

    /// <summary>
    /// 차량 선택 화면의 UI를 갱신합니다.
    /// </summary>
    public void UpdateUI(VehicleData data)
    {
        _currentDisplayData = data;
        _hasData = !string.IsNullOrEmpty(data.modelName);

        if(modelNameText != null) modelNameText.text = data.modelName;
        if(batteryTypeText != null) batteryTypeText.text = $"배터리: {data.batteryCapacity}";
        if(driveTypeText != null) driveTypeText.text = $"구동: {data.driveType}";
        if(rangeText != null) rangeText.text = $"주행거리: {data.maxRange}";

        Debug.Log($"<color=cyan>[UI]</color> {data.modelName} 정보 표시 중 (HasData: {_hasData})");
    }

    /// <summary>
    /// 최종 선택 확정 (버튼 클릭 또는 T 키 입력)
    /// </summary>
    public void ConfirmSelection()
    {
        Debug.Log($"<color=white>[Confirm]</color> 데이터 유효성 체크: {_hasData}");

        if(_hasData)
        {
            Debug.Log("<color=green>[Success]</color> 정비 훈련 시나리오를 요청합니다.");
            OnVehicleSelected.Invoke(_currentDisplayData);
        }
        else
        {
            Debug.LogWarning("<color=red>[Fail]</color> 선택된 차량 데이터가 없습니다.");
        }
    }

    // --- 훈련 진행도 관련 함수들 ---
    public void UpdateProgress(int current, int total)
    {
        if(progressText != null) progressText.text = $"진행도: {current} / {total}";
        if(progressBar != null && total > 0) progressBar.value = (float)current / total;
    }

    public void ShowCompleteUI() => completionPanel.SetActive(true);

    private void Update()
    {
        // 테스트용 단축키
        if(Input.GetKeyDown(KeyCode.T))
        {
            Debug.Log("<color=yellow>[Test]</color> T 키 입력으로 선택 확정");
            ConfirmSelection();
        }
    }
}