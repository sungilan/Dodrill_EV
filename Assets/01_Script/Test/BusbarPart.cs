// BusbarPart.cs (InteractablePart를 상속받거나 유사하게 구성)
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class BusbarPart : InteractablePart
{
    [Header("Busbar Specific")]
    public List<Bolt> mountingBolts; // 버스바를 고정하는 2~4개의 볼트
    public GameObject linkedModule;   // 이 버스바가 덮고 있는 모듈

    void Update()
    {
        // 모든 볼트가 풀렸는지 실시간 체크
        CheckAllBoltsLoosened();
    }

    private void CheckAllBoltsLoosened()
    {
        bool readyToLift = true;
        foreach(var bolt in mountingBolts)
        {
            if(!bolt.isloosened)
            {
                readyToLift = false;
                break;
            }
        }

        // 모든 볼트가 풀리면 그랩(Grab) 활성화
        //grabInteractable.enabled = readyToLift;
    }

    // 버스바가 제거되었을 때 호출되는 이벤트
    public void OnBusbarRemoved()
    {
        // 이제 밑에 있는 모듈을 들어 올릴 수 있도록 모듈의 잠금 해제
        linkedModule.GetComponent<ModulePart>().UnlockModuleFromBusbar();
    }
}