using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  SprayTool.cs
//
//  잡은 채로 트리거(PC: F, 모바일: 작업 버튼)를 누르고 있으면 분사되는 스프레이류.
//  · Lubricant  : 윤활 방청제(WD 계열) — 녹슬어 뻑뻑한 볼트에 뿌리면 잘 풀린다(BoltWork.Lubricate → 풀림 속도 1.6배)
//  · Cleaner    : 접점 세정제 — 분사만(커넥터·단자 청소 연출)
//  · Extinguisher: 소화기 — 넓고 멀리 분말 분사, 앞쪽의 불(이름에 fire/flame 이 든 파티클)을 끈다
//  · 분사 방향은 누르는 순간 '손이 가리키는 쪽'에 가장 가까운 캔의 옆 축으로 정해 모두에게 보낸다(모델마다 노즐 축이 달라서)
//  · 내용물이 다 떨어지면 더 나오지 않는다
// ============================================================
public class SprayTool : EquipmentBase
{
    protected override string MobileActionName => "분사";

    public enum Kind { Lubricant, Cleaner, Extinguisher }

    public Kind kind = Kind.Lubricant;
    [Tooltip("최대 분사 시간(초)")] public float contentSeconds = 40f;

    private Vector3 _upLocal;
    private Vector3 _topLocal;
    private float _radius;
    private ParticleSystem _mist;
    private AudioSource _audio;
    private float _left;
    private float _nextHit;
    private float _sprayTime;
    private readonly List<Bolt> _hits = new List<Bolt>();

    private bool IsExt => kind == Kind.Extinguisher;
    private float Range => IsExt ? 3.5f : 0.9f;

    protected override void Awake()
    {
        base.Awake();
        _left = contentSeconds;
    }

    private void Start()
    {
        _upLocal = EquipFx.UpAxisLocal(transform);
        if (EquipFx.LocalBounds(transform, out var b))
        {
            Vector3 e = b.extents;
            Vector3 au = new Vector3(Mathf.Abs(_upLocal.x), Mathf.Abs(_upLocal.y), Mathf.Abs(_upLocal.z));
            float along = Vector3.Dot(e, au);
            _topLocal = b.center + _upLocal * along * (IsExt ? 0.85f : 0.95f);
            Vector3 side = e - Vector3.Scale(au, e);
            _radius = Mathf.Max(side.x, side.y, side.z) * 0.8f;
        }

        var lib = EquipFx.Lib;
        if (lib != null && lib.mist != null)
        {
            Color c = kind == Kind.Lubricant ? new Color(0.85f, 0.8f, 0.55f, 0.85f)    // 분사가 잘 안 보여 진하게
                    : kind == Kind.Cleaner ? new Color(0.8f, 0.9f, 1f, 0.8f)
                    : new Color(0.97f, 0.97f, 0.95f, 0.9f);
            _mist = EquipFx.MakeParticles(transform, "[Spray]", new EquipFx.PsSettings
            {
                mat = lib.mist, color = c,
                lifetime = IsExt ? 1.2f : 0.65f, speed = IsExt ? 6f : 3.2f,
                size = IsExt ? 0.25f : 0.08f, sizeEnd = IsExt ? 4f : 5f,
                gravity = IsExt ? 0.15f : 0.02f, rate = 0f, coneAngle = IsExt ? 12f : 9f,
                radius = 0.004f, max = IsExt ? 500 : 250, drag = IsExt ? 1.2f : 2.5f
            });
            _mist.Play();
        }
        _audio = EquipFx.LoopSource(gameObject, EquipFx.Hiss, IsExt ? 15f : 8f);
    }

    protected override Vector3 ComputeUseDirLocal()
    {
        // 위쪽 축은 빼고, 손이 가리키는 쪽에 가장 가까운 옆 축
        return SnapToLocalAxis(PointingDir(), _upLocal);
    }

    protected override bool WantUsing(bool input) => input && _left > 0f;

    protected override void Update()
    {
        base.Update();
        if (_mist == null) return;

        bool on = Using && _left > 0f;
        Vector3 dirW = transform.TransformDirection(UseDirLocal);
        Vector3 origin = transform.TransformPoint(_topLocal + UseDirLocal * _radius);
        _mist.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(dirW));
        var em = _mist.emission;
        em.rateOverTime = on ? (IsExt ? 260f : 240f) : 0f;
        EquipFx.Drive(_audio, on ? (IsExt ? 0.7f : 0.45f) : 0f, IsExt ? 0.8f : 1.15f);

        if (!on) { _sprayTime = 0f; return; }
        _left -= Time.deltaTime;
        _sprayTime += Time.deltaTime;

        if (!IsLocalHeld) return;   // 효과 판정은 들고 있는 사람만(중복 안내 방지)
        if (IsVR && Time.frameCount % 6 == 0) HapticAll(0.05f, IsExt ? 0.35f : 0.15f);
        if (_left <= 0f) { EquipFx.Notice("spray_empty", "내용물이 다 떨어졌습니다", "새 캔을 쓰세요", EquipFx.Info, 2f, 6f); return; }

        if (Time.time < _nextHit) return;
        _nextHit = Time.time + 0.1f;

        if (kind == Kind.Lubricant) HitBolts(origin, dirW);
        else if (IsExt) Extinguish(origin, dirW);
    }

    private void HitBolts(Vector3 origin, Vector3 dir)
    {
        BoltWork.InCone(origin, dir, Range, 15f, _hits);
        bool anySeized = false, anyNew = false;
        foreach (var b in _hits)
        {
            if (b.isloosened || b.isAssembleMode) continue;
            if (BoltWork.Lubricate(b)) anyNew = true;
            if (BoltWork.IsSeized(b)) anySeized = true;
        }
        if (!anyNew) return;
        if (anySeized)
            EquipFx.Notice("lube_seized", "윤활 방청제 침투", "녹슨 볼트가 부드러워졌습니다 — 이제 잘 풀립니다", EquipFx.Info, 2.5f, 6f);
        else
            EquipFx.Notice("lube", "윤활 방청제 분사", "볼트 나사산에 윤활제가 스며들었습니다", EquipFx.Info, 2f, 8f);
    }

    private static readonly List<ParticleSystem> s_fires = new List<ParticleSystem>();
    private static float s_nextFireScan;

    private void Extinguish(Vector3 origin, Vector3 dir)
    {
        if (Time.unscaledTime >= s_nextFireScan)
        {
            s_nextFireScan = Time.unscaledTime + 2f;
            s_fires.Clear();
            foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                string n = ps.name.ToLowerInvariant();
                if (n.Contains("fire") || n.Contains("flame") || n.Contains("불")) s_fires.Add(ps);
            }
        }
        if (_sprayTime < 1.5f) return;   // 1.5초 이상 뿌려야 꺼진다
        float cos = Mathf.Cos(25f * Mathf.Deg2Rad);
        foreach (var f in s_fires)
        {
            if (f == null || !f.isPlaying) continue;
            Vector3 v = f.transform.position - origin;
            float d = v.magnitude;
            if (d > Range || d < 1e-3f || Vector3.Dot(v / d, dir) < cos) continue;
            f.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            EquipFx.Notice("fire_out", "불이 꺼졌습니다", "소화 완료 — 재발화에 주의하세요", EquipFx.Info, 2.5f, 5f);
        }
    }
}
