using UnityEngine;
using DG.Tweening;

public class BatteryJack : MonoBehaviour
{
    public Transform supportPlate; // 배터리를 받치는 판
    public float raiseHeight = 1.2f; // 배터리에 닿는 높이
    public float lowerHeight = 0.1f; // 완전히 내린 높이

    private GameObject attachedBattery; // 현재 받치고 있는 배터리
    private bool isRaised = false;

    // 1. 배터리 잭을 배터리 밑으로 이동시킨 후 상승 버튼 클릭
    public void RaiseJack()
    {
        supportPlate.DOLocalMoveY(raiseHeight, 3f).OnComplete(() => {
            isRaised = true;
            CheckForBatteryContact();
        });
    }

    // 2. 판이 올라갔을 때 위에 배터리가 있는지 트리거로 확인
    private void CheckForBatteryContact()
    {
        // Raycast나 Trigger를 사용하여 상단에 배터리 메쉬가 있는지 확인
        RaycastHit hit;
        if(Physics.Raycast(supportPlate.position, Vector3.up, out hit, 0.2f))
        {
            if(hit.collider.CompareTag("BatteryPack"))
            {
                attachedBattery = hit.collider.gameObject;
                // 배터리를 잭의 자식으로 설정하여 함께 움직이게 함
                attachedBattery.transform.SetParent(supportPlate);
                Debug.Log("배터리가 안전하게 잭에 고정되었습니다.");
            }
        }
    }

    // 3. 볼트가 다 풀린 후 하강 버튼 클릭
    public void LowerJack()
    {
        if(attachedBattery != null)
        {
            // 배터리팩의 스크립트에서 "탈거 완료" 상태를 확인하는 로직 필요
            supportPlate.DOLocalMoveY(lowerHeight, 5f).SetEase(Ease.OutBounce);
            Debug.Log("배터리 하강 중...");
        }
    }
}