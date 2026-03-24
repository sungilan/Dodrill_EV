using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class LOTOSystem : MonoBehaviour
{
    public static LOTOSystem Instance;

    [Header("LOTO Objects")]
    public GameObject lotoSocket;      // 자물쇠를 끼울 위치 (XRSocketInteractor)
    public GameObject lotoTag;         // '작업 중' 태그 오브젝트
    public bool isLOCKED = false;      // 최종 잠금 여부

    //void Awake() => Instance = true;

    // MSD가 제거되었을 때 호출 (MSDPart에서 연결)
    public void OnMSDRemoved()
    {
        lotoSocket.SetActive(true); // 이제 자물쇠를 걸 수 있는 소켓 활성화
        Debug.Log("MSD 제거됨. LOTO 자물쇠를 체결하십시오.");
    }

    // 소켓에 자물쇠가 들어왔을 때 호출 (Socket Event)
    public void OnLockInteracted(SelectEnterEventArgs args)
    {
        isLOCKED = true;
        lotoTag.SetActive(true); // 태그가 보이게 설정
        Debug.Log("LOTO 체결 완료. 고전압 차단이 승인되었습니다.");

        // 다음 단계(방전 대기) 시작 가능 알림
        DischargeTimerUI.Instance.StartDischarge();
    }
}