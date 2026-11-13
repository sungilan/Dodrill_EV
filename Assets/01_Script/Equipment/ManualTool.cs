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
//  · 토크렌치: 조임 전용. 볼트 밖에서 누르면 설정 토크가 바뀐다(TorqueSpec.Presets).
//              설정값이 볼트의 매뉴얼 규정 범위(TorqueSpec) 안이어야 조여지고, 도달하면 '딸깍' + 강한 진동.
//              모자라면 일찍 '딸깍'(덜 조임), 넘치면 과조임 경고. 푸는 데 쓰면 안내(공구 손상).
//  끝 위치는 모델의 가장 긴 축 양 끝 중 볼트에 가까운 쪽.
// ============================================================
public class ManualTool : EquipmentBase
{
    public enum Kind { Spanner, Screwdriver, TorqueWrench }
    public Kind kind = Kind.Spanner;
    [Tooltip("절연 공구(1000V) — 고전압 부품 볼트에는 절연 공구만 쓸 수 있다")] public bool insulated;
    [Tooltip("한 바퀴 돌릴 때 진행도(볼트 timeToComplete 와 무관한 고정값) — 720° 에 완료")] public float degreesToComplete = 720f;

    private Vector3 _axisLocal;
    private Vector3 _endALocal, _endBLocal;
    private Bolt _bolt;
    private Vector3 _prevDir;
    private bool _hasPrev;
    private float _ratchetAcc;
    private int _setIdx = 3;          // 토크렌치 설정(로컬) — 기본 2.7 kgf·m
    private bool _prevIn, _wasHeld;
    private float _clickCd;
    public float SetTorque => TorqueSpec.Presets[Mathf.Clamp(_setIdx, 0, TorqueSpec.Presets.Length - 1)];

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
        if (!IsLocalHeld) { _bolt = null; _hasPrev = false; _wasHeld = false; _prevIn = false; return; }
        bool pressed = LocalInput && !_prevIn;
        _prevIn = LocalInput;
        if (kind == Kind.TorqueWrench && !_wasHeld)
            EquipFx.Notice("torque_set", $"토크렌치 설정 {SetTorque:0.0#} kgf·m", "볼트에 대지 않고 누르면 설정이 바뀝니다. 볼트마다 매뉴얼 규정 토크에 맞추세요", EquipFx.Info, 3f, 0f);
        _wasHeld = true;
        _clickCd -= Time.deltaTime;

        Vector3 a = transform.TransformPoint(_endALocal), bpt = transform.TransformPoint(_endBLocal);
        var ba = BoltWork.Nearest(a, 0.07f);
        var bb = BoltWork.Nearest(bpt, 0.07f);
        Bolt bolt = ba;
        if (bolt == null || (bb != null && (bb.transform.position - bpt).sqrMagnitude < (ba.transform.position - a).sqrMagnitude)) bolt = bb;

        if (bolt != _bolt) { _bolt = bolt; _hasPrev = false; _ratchetAcc = 0f; }
        if (kind == Kind.TorqueWrench && _bolt == null && pressed)
        {
            _setIdx = (_setIdx + 1) % TorqueSpec.Presets.Length;
            AudioSource.PlayClipAtPoint(EquipFx.Click, transform.position, 0.6f);
            HapticAll(0.02f, 0.3f);
            EquipFx.Notice("torque_set", $"토크렌치 설정 {SetTorque:0.0#} kgf·m", "단계: 0.5 · 0.9 · 1.2 · 2.7 · 5.0 · 12 kgf·m", EquipFx.Info, 2f, 0f);
        }
        if (_bolt == null || _bolt.isloosened) return;

        if (kind == Kind.Screwdriver)
        {
            EquipFx.Notice("screwdriver_bolt", "볼트에는 스패너를 쓰세요", "드라이버는 나사(+/-)용입니다. 육각 볼트는 스패너나 임팩렌치로 푸세요", EquipFx.Warn, 2.5f, 6f);
            return;
        }

        if (kind == Kind.TorqueWrench && !_bolt.isAssembleMode)
        {
            EquipFx.Notice("torque_loosen", "토크렌치는 조임 전용입니다", "풀 때 쓰면 토크렌치 눈금이 틀어집니다 — 스패너·임팩렌치로 푸세요", EquipFx.Warn, 2.5f, 6f);
            return;
        }

        if (!insulated && IsHighVoltage(_bolt))
        {
            EquipFx.Notice("hv_tool", "절연 공구를 쓰세요!", "고전압 배터리·커넥터 주변 볼트입니다. 일반 공구는 감전·단락 위험 — 빨강/노랑 손잡이의 1000V 절연 공구로 작업하세요", EquipFx.Danger, 3f, 5f);
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

        TorqueSpec.Spec spec = default;
        bool hasSpec = kind == Kind.TorqueWrench && TorqueSpec.TryGet(_bolt, out spec);
        if (hasSpec && SetTorque < spec.min - 1e-3f)
        {
            // 설정이 낮으면 볼트가 덜 조인 상태에서 먼저 '딸깍'
            if (_clickCd <= 0f) { _clickCd = 0.6f; AudioSource.PlayClipAtPoint(EquipFx.Click, _bolt.transform.position, 0.8f); HapticAll(0.08f, 0.8f); }
            EquipFx.Notice("torque_under", $"토크 부족 — 설정 {SetTorque:0.0#} kgf·m", $"{spec.part} 규정 {spec.Range} ({spec.source}). 설정을 올려서 다시 조이세요", EquipFx.Warn, 3f, 4f);
            return;
        }
        if (hasSpec && SetTorque > spec.max + 1e-3f)
        {
            EquipFx.Notice("torque_over", $"과조임 위험 — 설정 {SetTorque:0.0#} kgf·m", $"{spec.part} 규정 {spec.Range}. 넘게 조이면 나사산·커버·단자가 손상됩니다 — 설정을 낮추세요", EquipFx.Danger, 3f, 4f);
            return;
        }

        if (BoltWork.IsSeized(_bolt) && !BoltWork.IsLubed(_bolt))
            EquipFx.Notice("seized", "녹슬어 뻑뻑한 볼트", "윤활 방청제를 뿌리면 잘 풀립니다", EquipFx.Warn, 3f, 10f);

        bool wasDone = _bolt.isloosened;
        _bolt.InteractWithTool(deg / degreesToComplete * BoltWork.SpeedFactor(_bolt), transform.TransformDirection(_axisLocal));
        if (kind == Kind.TorqueWrench && !wasDone && _bolt.isloosened)
        {
            HapticAll(0.12f, 1f);   // 규정 토크 도달 — 손목이 '꺾이는' 느낌
            AudioSource.PlayClipAtPoint(EquipFx.Click, _bolt.transform.position, 0.9f);
            EquipFx.Notice("torque_ok", $"딸깍 — {SetTorque:0.0#} kgf·m 체결 완료", hasSpec ? $"{spec.part} 규정 {spec.Range} ({spec.source})" : "규정 토크로 조였습니다", EquipFx.Info, 2.5f, 1f);
        }

        _ratchetAcc += deg;
        if (_ratchetAcc >= 30f)
        {
            _ratchetAcc = 0f;
            AudioSource.PlayClipAtPoint(EquipFx.Ratchet, _bolt.transform.position, 0.7f);
            HapticAll(0.02f, 0.4f);
        }
    }

    /// <summary>고전압 부품 볼트인가 — 시나리오 볼트(배터리 팩·커넥터·PRA 등) 중 12V 계통이 아닌 것. 휠 너트 같은 자유 실습 볼트는 아니다.</summary>
    public static bool IsHighVoltage(Bolt b)
    {
        if (b == null || b.GetComponent<FreeWorkBolt>() != null) return false;
        var g = b.GetComponentInParent<BoltGroupCounter>();
        if (g == null) return false;
        string id = ((g.taskId ?? "") + " " + g.name).ToUpperInvariant();
        return !id.Contains("12V");
    }
}
