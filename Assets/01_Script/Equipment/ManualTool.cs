using UnityEngine;
using XRAirpotrSecurity;

// ============================================================
//  ManualTool.cs
//
//  수공구 — 스패너(렌치)·드라이버.
//  · 스패너: 끝을 지금 단계 볼트에 대고 손목을 돌리면(볼트 축 둘레 회전량) 볼트가 풀린다/조인다. 30°마다 라쳇 '딱'.
//            에어 공구보다 느리지만 공기·전원 없이 쓸 수 있다. 녹슨 볼트는 더 느리고, 윤활제를 뿌리면 빨라진다.
//            PC·모바일: 끝을 볼트 가까이 두고 F(작업 버튼)를 누르고 있으면 돌린다.
//  · 드라이버: 볼트에 대면 "볼트에는 스패너를 쓰세요" 안내만(나사 전용).
//  끝 위치는 모델의 가장 긴 축 양 끝 중 볼트에 가까운 쪽.
// ============================================================
public class ManualTool : EquipmentBase
{
    public enum Kind { Spanner, Screwdriver }
    public Kind kind = Kind.Spanner;
    [Tooltip("한 바퀴 돌릴 때 진행도(볼트 timeToComplete 와 무관한 고정값) — 720° 에 완료")] public float degreesToComplete = 720f;

    private Vector3 _axisLocal;
    private Vector3 _endALocal, _endBLocal;
    private Bolt _bolt;
    private Vector3 _prevDir;
    private bool _hasPrev;
    private float _ratchetAcc;

    private void Start()
    {
        if (EquipFx.LocalBounds(transform, out var b))
        {
            Vector3 e = b.extents;
            _axisLocal = e.x >= e.y && e.x >= e.z ? Vector3.right : (e.y >= e.z ? Vector3.up : Vector3.forward);
            float half = Vector3.Dot(e, _axisLocal);
            _endALocal = b.center + _axisLocal * half * 0.9f;
            _endBLocal = b.center - _axisLocal * half * 0.9f;
        }
        else { _axisLocal = Vector3.forward; _endALocal = Vector3.forward * 0.1f; _endBLocal = -_endALocal; }
    }

    protected override bool WantUsing(bool input) => false;   // 다른 사람에게 보낼 '쓰는 중' 상태는 없다(손 움직임이 곧 동작)

    protected override void Update()
    {
        base.Update();
        if (!IsLocalHeld) { _bolt = null; _hasPrev = false; return; }

        Vector3 a = transform.TransformPoint(_endALocal), bpt = transform.TransformPoint(_endBLocal);
        var ba = BoltWork.Nearest(a, 0.07f);
        var bb = BoltWork.Nearest(bpt, 0.07f);
        Bolt bolt = ba;
        if (bolt == null || (bb != null && (bb.transform.position - bpt).sqrMagnitude < (ba.transform.position - a).sqrMagnitude)) bolt = bb;

        if (bolt != _bolt) { _bolt = bolt; _hasPrev = false; _ratchetAcc = 0f; }
        if (_bolt == null || _bolt.isloosened) return;

        if (kind == Kind.Screwdriver)
        {
            EquipFx.Notice("screwdriver_bolt", "볼트에는 스패너를 쓰세요", "드라이버는 나사(+/-)용입니다. 육각 볼트는 스패너나 임팩렌치로 푸세요", EquipFx.Warn, 2.5f, 6f);
            return;
        }

        if (!BoltWork.IsCurrentStep(_bolt))
        {
            EquipFx.Notice("spanner_step", "지금 단계의 볼트가 아닙니다", "안내에 표시된 볼트를 작업하세요", EquipFx.Info, 2f, 6f);
            return;
        }

        float deg;
        if (Platform == PlatformType.VR)
        {
            // 볼트 축 둘레로 공구 축이 돈 각도
            Vector3 n = _bolt.transform.up;
            Vector3 dir = Vector3.ProjectOnPlane(transform.TransformDirection(_axisLocal), n);
            if (dir.sqrMagnitude < 1e-4f) { _hasPrev = false; return; }
            dir.Normalize();
            deg = _hasPrev ? Mathf.Abs(Vector3.SignedAngle(_prevDir, dir, n)) : 0f;
            _prevDir = dir; _hasPrev = true;
            if (deg > 45f) deg = 0f;   // 순간 튐 무시
        }
        else
        {
            deg = LocalInput ? 160f * Time.deltaTime : 0f;
        }
        if (deg <= 0f) return;

        if (BoltWork.IsSeized(_bolt) && !BoltWork.IsLubed(_bolt))
            EquipFx.Notice("seized", "녹슬어 뻑뻑한 볼트", "윤활 방청제를 뿌리면 잘 풀립니다", EquipFx.Warn, 3f, 10f);

        _bolt.InteractWithTool(deg / degreesToComplete * BoltWork.SpeedFactor(_bolt), transform.TransformDirection(_axisLocal));

        _ratchetAcc += deg;
        if (_ratchetAcc >= 30f)
        {
            _ratchetAcc = 0f;
            AudioSource.PlayClipAtPoint(EquipFx.Ratchet, _bolt.transform.position, 0.7f);
            HapticAll(0.02f, 0.4f);
        }
    }
}
