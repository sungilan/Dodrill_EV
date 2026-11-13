using UnityEngine;

// ============================================================
//  AngleGrinder.cs
//
//  핸드 그라인더. 누르고 있는 동안(VR 트리거 / PC F / 모바일 작업 버튼) 디스크가 고속 회전한다.
//  디스크 가장자리가 금속·물체 면에 닿으면 불꽃이 회전 방향(접선)으로 튀고, 갈리는 소리·강한 진동이 난다 — 모두에게 보임.
//  · PC·모바일: 화면 가운데로 겨눈 면(1.5m 안)에 대면 갈린다
//  · 안전 안내: 처음 켤 때 보안경·장갑, 불꽃 방향(사람·인화물 쪽 금지)
// ============================================================
public class AngleGrinder : EquipmentBase
{
    protected override string MobileActionName => "연삭";

    [Tooltip("디스크(로컬 +Y 가 회전축)")] public Transform disc;
    [Tooltip("디스크 반지름(m)")] public float discRadius = 0.0575f;

    private ParticleSystem _sparks;
    private AudioSource _motor, _grind;
    private float _spin, _rpm01;
    private bool _warned;

    private void Start()
    {
        var lib = EquipFx.Lib;
        if (lib != null && lib.spark != null)
        {
            _sparks = EquipFx.MakeParticles(transform, "[GrindSparks]", new EquipFx.PsSettings
            {
                mat = lib.spark, color = new Color(1f, 0.72f, 0.28f, 1f), lifetime = 0.55f, speed = 5.5f, size = 0.01f,
                gravity = 1.2f, rate = 0f, coneAngle = 14f, radius = 0.004f, max = 600, stretch = true
            });
            _sparks.Play();
        }
        _motor = EquipFx.LoopSource(gameObject, EquipFx.Rattle, 12f);
        _grind = EquipFx.LoopSource(gameObject, EquipFx.Hiss, 14f);
    }

    protected override void OnPressDown(Autohand.Hand h)
    {
        if (_warned) return;
        _warned = true;
        EquipFx.Notice("grinder_safety", "그라인더 안전", "보안경·장갑 착용. 불꽃이 사람·배터리·인화물 쪽으로 튀지 않게 하세요", EquipFx.Warn, 3.5f, 60f);
    }

    protected override void Update()
    {
        base.Update();
        _rpm01 = Mathf.MoveTowards(_rpm01, Using ? 1f : 0f, Time.deltaTime * (Using ? 3f : 0.8f));   // 끄면 천천히 멈춤
        _spin += _rpm01 * 4000f * Time.deltaTime;
        var d = disc != null ? disc : transform;
        if (disc != null) disc.localRotation = Quaternion.Euler(0f, _spin, 0f);

        // 디스크 가장자리가 닿는 곳
        bool contact = false;
        Vector3 point = default, normal = default, tangent = default;
        if (_rpm01 > 0.3f)
        {
            Vector3 axis = d.up;
            Vector3 aim = IsLocalHeld && !IsVR && Camera.main != null ? Camera.main.transform.forward : Vector3.ProjectOnPlane(transform.forward, axis);
            Ray ray = IsLocalHeld && !IsVR && Camera.main != null ? new Ray(Camera.main.transform.position, aim) : new Ray(d.position, aim.sqrMagnitude > 1e-4f ? aim.normalized : -d.forward);
            float range = IsLocalHeld && !IsVR ? 1.6f : discRadius + 0.02f;
            foreach (var h in Physics.RaycastAll(ray, range, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.collider.GetComponentInParent<Autohand.Hand>() != null) continue;
                contact = true; point = h.point; normal = h.normal;
                break;
            }
            if (!contact && IsVR)
            {
                // VR: 디스크 둘레 어디든 닿으면
                foreach (var c in Physics.OverlapSphere(d.position, discRadius + 0.01f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (c.transform.IsChildOf(transform) || c.GetComponentInParent<Autohand.Hand>() != null) continue;
                    point = c.ClosestPoint(d.position); normal = (d.position - point).sqrMagnitude > 1e-6f ? (d.position - point).normalized : -d.forward;
                    contact = true; break;
                }
            }
            if (contact) tangent = Vector3.Cross(d.up, (point - d.position).sqrMagnitude > 1e-6f ? (point - d.position).normalized : -normal).normalized;
        }

        if (_sparks != null)
        {
            var em = _sparks.emission;
            em.rateOverTime = contact ? 450f * _rpm01 : 0f;
            if (contact)
            {
                Vector3 dir = (tangent + normal * 0.35f).normalized;
                if (dir.sqrMagnitude < 0.1f) dir = normal;
                _sparks.transform.SetPositionAndRotation(point, Quaternion.LookRotation(dir));
            }
        }
        EquipFx.Drive(_motor, _rpm01 * 0.55f, 1.4f + _rpm01 * 0.6f);
        EquipFx.Drive(_grind, contact ? 0.8f : 0f, contact ? 0.55f : 1f);

        if (IsLocalHeld && _rpm01 > 0.05f && IsVR)
        {
            if (contact && Time.frameCount % 2 == 0) HapticAll(0.03f, 0.95f);       // 갈릴 때 강한 떨림
            else if (Time.frameCount % 4 == 0) HapticAll(0.02f, 0.25f * _rpm01);     // 공회전 잔떨림
        }
    }
}
