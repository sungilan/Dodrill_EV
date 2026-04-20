using UnityEngine;

public class LiftAnimationEventHandler : MonoBehaviour
{
    public string taskId; // 인스펙터에서 현재 단계 ID 설정 (예: BatteryJack_Lowering)
    private VehiclePartsManager _partsManager;

    // 프로퍼티를 통해 안전하게 접근
    private VehiclePartsManager PartsManager
    {
        get
        {
            // 찾은 적이 없거나, 찾았는데 파괴되었다면 다시 시도
            if(_partsManager == null)
            {
                _partsManager = GameObject.FindAnyObjectByType<VehiclePartsManager>(FindObjectsInactive.Include);

                if(_partsManager == null)
                    Debug.LogWarning("[LiftEvent] VehiclePartsManager를 씬에서 찾을 수 없습니다.");
            }
            return _partsManager;
        }
    }

    public void OnEvent_HideBattery()
    {
        if(PartsManager != null)
        {
            PartsManager.HideBattery();
            Debug.Log("<color=yellow>[LiftEvent]</color> 배터리 숨기기 실행");
        }
    }

    public void OnEvent_ShowBattery()
    {
        if(PartsManager != null)
        {
            PartsManager.ShowBattery();
            Debug.Log("<color=yellow>[LiftEvent]</color> 배터리 표시 실행");
        }
    }

    // 애니메이션 타임라인 끝부분에 이 함수를 이벤트로 등록하세요
    public void OnLiftAnimationComplete()
    {
        if(!string.IsNullOrEmpty(taskId))
        {
            Debug.Log($"<color=lime>[LiftEvent]</color> 애니메이션 완료! 신호 송신: {taskId}");
            InteractionEvents.FireTaskConfirmed(taskId);
        }
    }
}