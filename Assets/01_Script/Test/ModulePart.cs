using UnityEngine;

public class ModulePart : InteractablePart // 상속
{
    [Header("Module Specific")]
    public bool isBusbarRemoved = false;
    public bool isFaulty = false;  // 불량 여부
    public float voltage = 3.8f;   // 전압 값
    public int slotIndex;          // 배터리 팩 내 몇 번째 슬롯인지 (0~15 등)

    // 새 모듈로 교체되었을 때 데이터를 초기화하는 함수
    public void ReplaceWithNewData()
    {
        isFaulty = false;
        voltage = 3.8f; // 정상 전압으로 복구
        Debug.Log($"{slotIndex}번 슬롯 모듈이 새것으로 교체되었습니다.");
    }

    // 버스바가 제거될 때 호출될 함수
    public void UnlockModuleFromBusbar()
    {
        isBusbarRemoved = true;
        Debug.Log($"{gameObject.name}: 버스바가 제거되어 추출 준비 완료.");
    }

    // 리프팅 툴이 추출을 시도할 때 체크하는 함수
    public bool CanLift()
    {
        if(!isBusbarRemoved)
        {
            if(SafetyUIHandler.Instance != null)
                SafetyUIHandler.Instance.TriggerWarning("버스바를 먼저 제거해야 합니다!");
            return false;
        }
        return true;
    }

    // [오버라이드] 부품이 잡혔을 때 추가 로직이 필요하다면 사용
    //protected override void OnSelectEntered(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
    //{
    //    // 리프팅 툴 없이 맨손으로 잡으려 할 때의 경고 로직 등을 여기에 추가 가능
    //    if(!CanLift()) return;

    //    base.OnSelectEntered(args);
    //}
    private void OnCollisionEnter(Collision collision)
    {
        // 일정 속도(Impact) 이상으로 바닥이나 차체에 부딪혔을 때
        if(collision.relativeVelocity.magnitude > 5.0f)
        {
            EmergencyEventManager.Instance.TriggerThermalRunaway("배터리 모듈 물리적 충격으로 인한 셀 파손");
        }
    }
}