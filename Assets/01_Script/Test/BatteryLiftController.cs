
using DG.Tweening;
using DoDrill;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using System.Collections;
using UnityEngine;

public class BatteryLiftController : LiftControllerBase
{
    [Header("── 리프트 상판 설정 ──")]
    public Transform liftPlate;
    public float maxHeight = 0.5f;

    private Vector3 _plateOrigin;

    protected override float GetMaxHeight() => maxHeight;

    public override void OnStartClient()
    {
        base.OnStartClient();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        // 실행 중인 코루틴이 있다면 안전하게 종료
        StopAllCoroutines();
    }

    protected override void OnLiftStart()
    {
        // 리프트 모드 설정 및 초기 위치 저장
        if(liftMode == LiftMode.TransformBased)
        {
            if(liftAnimator != null) liftAnimator.enabled = false;
            if(liftPlate != null) _plateOrigin = liftPlate.localPosition;
        }

        Debug.Log("<color=cyan>[BatteryLift]</color> 리프트 초기화 완료. (배터리 자동 부착 기능 비활성화)");
    }

    protected override void ApplyHeightTransform(float h)
    {
        // 리프트 상판 이동 (로컬 Y축)
        if(liftPlate != null)
        {
            liftPlate.localPosition = _plateOrigin + Vector3.up * h;
        }
    }

    protected override void SyncPayloadLateUpdate()
    {
        // 추가 동기화 필요 시 작성
    }

    protected override void OnHeightChanged(float newHeight, float prevHeight)
    {
        // 높이 변화에 따른 추가 로직이 필요 없다면 비워둡니다.
    }

    protected override void OnReachedBottom()
    {
        // 리프트가 바닥에 도달했을 때의 로직
        Debug.Log("<color=orange>[BatteryLift]</color> 리프트가 최하단에 도달했습니다.");
    }

    protected override void OnReachedTop()
    {
        // 리프트가 최고점에 도달했을 때의 로직
        Debug.Log("<color=orange>[BatteryLift]</color> 리프트가 최고점에 도달했습니다.");
    }
}