using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

// SlotManager.cs (각 모듈 슬롯 오브젝트에 부착)
public class SlotManager : MonoBehaviour
{
    public int slotIndex; // 이 슬롯의 번호

    public void OnModuleSnapped(SelectEnterEventArgs args)
    {
        // 소켓에 들어온 오브젝트에서 ModulePart 컴포넌트를 가져옴
        ModulePart snappedModule = args.interactableObject.transform.GetComponent<ModulePart>();

        if(snappedModule != null)
        {
            // 만약 들어온 모듈이 '정상(Spare)' 모듈이라면
            if(!snappedModule.isFaulty)
            {
                Debug.Log($"{slotIndex}번 슬롯에 정상 모듈 장착 완료!");
                // DiagnosticSystem에 수리 완료 알림 등을 보낼 수 있습니다.
            }
            else
            {
                Debug.LogWarning("불량 모듈을 다시 끼우셨습니다!");
            }
        }
    }
}