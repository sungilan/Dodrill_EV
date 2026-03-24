using UnityEngine;
using DG.Tweening; // DOTween 설치 필요

public class LiftManager : MonoBehaviour
{
    public Transform liftTransform;  // 리프트 모델
    public Transform carTransform;   // 차량 모델 (리프트의 자식으로 두거나 함께 이동)

    public float topHeight = 1.8f;   // 리프트가 올라갈 최대 높이
    public float bottomHeight = 0f;  // 기본 높이
    public float duration = 5f;      // 이동 시간 (초)

    private bool isMoving = false;

    // 위로 올리기 버튼에 연결
    public void MoveUp()
    {
        if(isMoving) return;
        isMoving = true;

        // 리프트와 차를 동시에 목표 높이로 이동
        liftTransform.DOMoveY(topHeight, duration).SetEase(Ease.InOutQuad);
        carTransform.DOMoveY(topHeight, duration).SetEase(Ease.InOutQuad).OnComplete(() => {
            isMoving = false;
            Debug.Log("리프트 상승 완료");
        });

        // [사운드] 리프트 작동 기계음 재생
    }

    // 아래로 내리기 버튼에 연결
    public void MoveDown()
    {
        if(isMoving) return;
        isMoving = true;

        liftTransform.DOMoveY(bottomHeight, duration).SetEase(Ease.InOutQuad);
        carTransform.DOMoveY(bottomHeight, duration).SetEase(Ease.InOutQuad).OnComplete(() => {
            isMoving = false;
            Debug.Log("리프트 하강 완료");
        });
    }
}