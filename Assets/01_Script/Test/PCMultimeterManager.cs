using UnityEngine;
using FishNet.Object;
using Autohand; // Autohand 참조 필수

public class PCMultimeterManager : MonoBehaviour
{
    [Header("필수 할당 (Inspector)")]
    public SyncGrab bodySyncGrab;
    public MultimeterProbeSync blackProbeSync;
    public MultimeterProbeSync redProbeSync;
    public Grabbable multimeterGrabbable; // Grabbable 컴포넌트 참조 추가

    [Header("레이어 설정")]
    public LayerMask terminalLayer;

    private int _clickCount = 0;

    // ── 외부 이벤트용 (인자 없는 버전) ──────────────────

    /// <summary>
    /// AutoHand의 인자 없는 이벤트에 연결할 수 있습니다.
    /// 현재 잡고 있는 손 중 첫 번째 손의 방향으로 발사합니다.
    /// </summary>
    public void FireProbeFromCurrentHand()
    {
        // 1. 상태 체크
        if(multimeterGrabbable == null || !multimeterGrabbable.IsHeld()) return;
        if(bodySyncGrab == null || !bodySyncGrab.IsOwner) return;

        // 2. 현재 잡고 있는 손 리스트 가져오기
        var heldByHands = multimeterGrabbable.GetHeldBy();
        if(heldByHands.Count > 0)
        {
            // 보통 한 손으로 잡으므로 첫 번째 손(0번)을 사용
            Hand currentHand = heldByHands[0];

            if(currentHand != null)
            {
                // 손바닥 정면 방향으로 레이 생성
                Ray ray = new Ray(currentHand.palmTransform.position, currentHand.palmTransform.forward);
                ExecuteProbeLogic(ray);
            }
        }
    }

    public void RequestResetProbes() => ResetProbes();

    // ── 내부 핵심 로직 ──────────────────────────────────────────

    private void ExecuteProbeLogic(Ray ray)
    {
        if(Physics.Raycast(ray, out RaycastHit hit, 10f, terminalLayer))
        {
            var point = hit.collider.GetComponent<MeasurementPoint>();
            if(point == null) return;

            if(_clickCount == 0)
            {
                blackProbeSync.FlyToTerminal(point, hit.point, hit.normal);
                _clickCount = 1;
            }
            else if(_clickCount == 1)
            {
                redProbeSync.FlyToTerminal(point, hit.point, hit.normal);
                _clickCount = 2;
            }
        }
    }

    private void ResetProbes()
    {
        if(_clickCount == 0) return;
        _clickCount = 0;
        if(blackProbeSync != null) blackProbeSync.ReturnToSocket();
        if(redProbeSync != null) redProbeSync.ReturnToSocket();
    }
}