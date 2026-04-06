using UnityEngine;
using DG.Tweening;
using FishNet.Object;

public class MultimeterProbeSync : MonoBehaviour
{
    private SyncGrab _syncGrab;
    private MultimeterProbe _probe;

    // 복귀를 위한 초기 로컬 위치 저장 변수
    private Vector3 _originLocalPos;
    private Quaternion _originLocalRot;

    void Awake()
    {
        _syncGrab = GetComponent<SyncGrab>();
        _probe = GetComponent<MultimeterProbe>();

        // 씬 시작 시 본체 소켓에 배치된 현재의 로컬 좌표를 기억합니다.
        _originLocalPos = transform.localPosition;
        _originLocalRot = transform.localRotation;
    }

    /// <summary>
    /// PC에서 단자를 클릭했을 때 호출. 
    /// SyncGrab의 소유권을 획득한 뒤 해당 좌표로 날아갑니다.
    /// </summary>
    public void FlyToTerminal(MeasurementPoint targetPoint, Vector3 hitPos, Vector3 hitNormal)
    {
        // 서버 소유권 요청 후 이동 실행
        _syncGrab.RequestGrab(() => {
            PerformFly(targetPoint, hitPos, hitNormal);
        });
    }

    private void PerformFly(MeasurementPoint targetPoint, Vector3 hitPos, Vector3 hitNormal)
    {
        DOTween.Kill(transform);

        // 단자에 꽂히는 회전값 계산 (단자의 Normal 반대 방향 + 오프셋 결합)
        Quaternion targetRot = Quaternion.LookRotation(-hitNormal) * Quaternion.Euler(_syncGrab.holdRotationOffset);
        Vector3 finalPos = hitPos + (targetRot * _syncGrab.holdPositionOffset);

        // 아크 이동 연출
        Vector3 mid = (transform.position + finalPos) * 0.5f + Vector3.up * 0.2f;
        transform.DOPath(new[] { mid, finalPos }, 0.4f, PathType.CatmullRom)
            .SetEase(Ease.OutCubic);

        transform.DORotateQuaternion(targetRot, 0.4f)
            .SetEase(Ease.OutCubic)
            .OnComplete(() => {
                // 도달 시 실제 측정 로직에 단자 ID 주입 및 LCD 갱신
                _probe.currentTerminalId = targetPoint.terminalId;
                _probe.master.EvaluateConnection();

                // LNT를 통해 최종 위치 강제 동기화
                var lnt = GetComponent<LocalNetworkTransform>();
                lnt?.SetTargetPosition(finalPos, targetRot);
            });
    }

    /// <summary>
    /// 매개변수 없이 호출 시 초기 저장된 로컬 위치로 복구합니다.
    /// </summary>
    public void ReturnToSocket()
    {
        // 소유권 확인 후 이동
        _syncGrab.RequestGrab(() => {
            // 측정 상태 초기화
            _probe.currentTerminalId = "";
            _probe.master.EvaluateConnection();

            // 본체의 자식 상태이므로 DOLocalMove 사용
            transform.DOLocalMove(_originLocalPos, 0.3f);
            transform.DOLocalRotateQuaternion(_originLocalRot, 0.3f).OnComplete(() => {
                // 복귀 완료 후 소유권 해제 및 Kinematic 복구
                _syncGrab.RequestRelease();
            });
        });
    }
}