using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class LiftingTool : XRGrabInteractable
{
    public Transform attachPoint; // 모듈과 맞닿을 끝부분
    public bool isLocked = false; // 잠금 레버 상태
    private InteractablePart targetModule; // 현재 결합된 모듈

    // 1. 모듈 근처에 도구를 가져가면 호출 (Trigger)
    private void OnTriggerEnter(Collider other)
    {
        if(other.CompareTag("ModuleSlot") && targetModule == null)
        {
            // 도구를 모듈 소켓 위치에 '착' 붙임 (Snap)
            SnapToModule(other.transform.parent.GetComponent<InteractablePart>());
        }
    }

    private void SnapToModule(InteractablePart module)
    {
        targetModule = module;
        // 도구의 위치를 모듈의 소켓 위치로 고정
        transform.position = module.assemblyTarget.position + Vector3.up * 0.2f;
        // [사운드] '철컥' 결합음
    }

    // 2. 잠금 레버 작동 (VR 버튼이나 트리거 입력)
    public void ToggleLock()
    {
        if(targetModule == null) return;

        isLocked = !isLocked;
        if(isLocked)
        {
            // 도구와 모듈을 하나로 묶음 (Parenting)
            targetModule.transform.SetParent(this.transform);
            targetModule.GetComponent<Rigidbody>().isKinematic = true;
            Debug.Log("모듈이 리프팅 툴에 잠금 고정되었습니다.");
        }
        else
        {
            targetModule.transform.SetParent(null);
            targetModule = null;
        }
    }
}