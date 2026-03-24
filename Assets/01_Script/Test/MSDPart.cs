using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class MSDPart : InteractablePart // 기존 로직 상속
{
    [Header("MSD Specific")]
    public GameObject powerOffEffect; // 차단 시 불 꺼지는 연출 등

    // 부품이 완전히 탈거되었을 때 호출 (기존 OnPartReleased 수정 또는 확장)
    public void OnMSDDetached()
    {
        if(currentState == PartState.Detached)
        {
            ElectricSafetyManager.Instance.isMSDRemoved = true;
            Debug.Log("고전압 전원이 차단되었습니다.");

            if(powerOffEffect != null) powerOffEffect.SetActive(true);
            // [사운드] 전원 차단되는 '지잉- 탁' 소리 재생
        }
    }
}