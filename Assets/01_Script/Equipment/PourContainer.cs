using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  PourContainer.cs
//
//  액체 통(엔진오일 병·냉각수/워셔액 통) — 기울여 내리면 주둥이에서 액체가 흘러내린다.
//  · 기울기가 70° 를 넘으면 흐르기 시작하고, 거꾸로 들수록 많이 흐른다. 다 쏟으면 멈춘다.
//  · 줄기가 떨어지는 곳: 오일 받이(FloorDragProp.catchLiquid) 위면 받은 양이 늘고, 바닥이면 웅덩이가 생겨 점점 커진다
//  · 위치·자세는 SyncGrab 으로 모두에게 같으므로 흐름은 각 화면에서 따로 계산한다(네트워크 메시지 없음)
// ============================================================
public class PourContainer : MonoBehaviour
{
    [Tooltip("액체 색")] public Color liquidColor = new Color(0.55f, 0.36f, 0.05f, 0.95f);
    [Tooltip("처음 들어 있는 양(L)")] public float capacity = 1f;
    [Tooltip("최대로 기울였을 때 초당 흐르는 양(L/s)")] public float maxFlow = 0.12f;
    [Tooltip("흐르기 시작하는 기울기(도)")] public float pourAngle = 70f;
    [Tooltip("표시 이름(웅덩이 안내용)")] public string liquidName = "오일";

    private float _amount;
    private Vector3 _upLocal;
    private Vector3 _spoutCenterLocal;
    private float _spoutRadius;
    private ParticleSystem _stream;
    private AudioSource _audio;
    private float _nextCatchCheck;
    private Collider[] _myColliders;

    private static Transform s_puddleRoot;
    private static readonly List<Transform> s_puddles = new List<Transform>();
    private static readonly List<float> s_puddleVol = new List<float>();
    private const int MaxPuddles = 6;

    private void Start()
    {
        _amount = capacity;
        _upLocal = EquipFx.UpAxisLocal(transform);
        if (EquipFx.LocalBounds(transform, out var b))
        {
            // 주둥이: 위쪽 축 방향 끝면 중앙. 반지름은 몸통 폭의 1/4 정도(주둥이는 몸통보다 좁다)
            Vector3 ext = b.extents;
            float along = Mathf.Abs(Vector3.Dot(ext, new Vector3(Mathf.Abs(_upLocal.x), Mathf.Abs(_upLocal.y), Mathf.Abs(_upLocal.z))));
            _spoutCenterLocal = b.center + _upLocal * along;
            Vector3 side = ext - new Vector3(Mathf.Abs(_upLocal.x) * ext.x, Mathf.Abs(_upLocal.y) * ext.y, Mathf.Abs(_upLocal.z) * ext.z);
            _spoutRadius = Mathf.Clamp(side.magnitude * 0.3f, 0.01f, 0.05f);
        }
        _myColliders = GetComponentsInChildren<Collider>();

        var lib = EquipFx.Lib;
        if (lib != null && lib.drop != null)
        {
            _stream = EquipFx.MakeParticles(transform, "[Pour]", new EquipFx.PsSettings
            {
                mat = lib.drop, color = liquidColor, lifetime = 0.9f, speed = 0.5f, size = 0.012f, sizeEnd = 0.8f,
                gravity = 1f, rate = 0f, coneAngle = 6f, radius = 0.004f, max = 260, stretch = true
            });
            _stream.Play();
        }
        _audio = EquipFx.LoopSource(gameObject, EquipFx.Pour, 8f);
    }

    private void Update()
    {
        if (_stream == null) return;
        Vector3 up = transform.TransformDirection(_upLocal);
        float tilt = Vector3.Angle(up, Vector3.up);
        bool pouring = tilt > pourAngle && _amount > 0f;
        float flow = pouring ? Mathf.InverseLerp(pourAngle, 160f, tilt) * 0.8f + 0.2f : 0f;

        // 아래쪽 입술(주둥이 가장자리 중 가장 낮은 곳)에서 흘러나온다
        Vector3 spout = transform.TransformPoint(_spoutCenterLocal);
        Vector3 down = Vector3.ProjectOnPlane(Vector3.down, up);
        Vector3 lip = spout + (down.sqrMagnitude > 1e-6f ? down.normalized * _spoutRadius : Vector3.zero);
        Vector3 outDir = (up * 0.6f + (down.sqrMagnitude > 1e-6f ? down.normalized : Vector3.zero) * 0.4f).normalized;
        _stream.transform.SetPositionAndRotation(lip, Quaternion.LookRotation(outDir.sqrMagnitude > 1e-6f ? outDir : Vector3.down));

        var em = _stream.emission;
        em.rateOverTime = 160f * flow;
        var main = _stream.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f + 0.5f * flow, 0.4f + 0.7f * flow);

        EquipFx.Drive(_audio, pouring ? 0.25f + 0.35f * flow : 0f, 0.9f + 0.3f * flow);

        if (!pouring) return;
        float liters = maxFlow * flow * Time.deltaTime;
        _amount = Mathf.Max(0f, _amount - liters);
        _pending += liters;

        if (Time.time >= _nextCatchCheck)
        {
            _nextCatchCheck = Time.time + 0.2f;
            Land(lip, _pending);
            _pending = 0f;
        }
    }

    private float _pending;

    /// <summary>줄기가 떨어지는 곳 처리.</summary>
    private void Land(Vector3 from, float liters)
    {
        var hits = Physics.RaycastAll(from + Vector3.down * 0.02f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue; RaycastHit hit = default; bool any = false;
        foreach (var h in hits)
        {
            if (IsMine(h.collider)) continue;
            if (h.collider.GetComponentInParent<Autohand.Hand>() != null) continue;
            if (h.distance < best) { best = h.distance; hit = h; any = true; }
        }
        if (!any) return;

        var catcher = hit.collider.GetComponentInParent<FloorDragProp>();
        if (catcher != null && catcher.AddLiquid(liters)) return;

        if (hit.normal.y > 0.6f) GrowPuddle(hit.point, liters);
    }

    private bool IsMine(Collider c)
    {
        if (_myColliders == null) return false;
        foreach (var m in _myColliders) if (m == c) return true;
        return false;
    }

    // ── 웅덩이 ─────────────────────────────

    private void GrowPuddle(Vector3 p, float liters)
    {
        int idx = -1;
        for (int i = 0; i < s_puddles.Count; i++)
        {
            if (s_puddles[i] == null) continue;
            Vector3 d = s_puddles[i].position - p; d.y = 0f;
            if (d.magnitude < s_puddles[i].localScale.x * 0.5f + 0.08f) { idx = i; break; }
        }
        if (idx < 0)
        {
            var lib = EquipFx.Lib;
            if (lib == null || lib.puddle == null) return;
            if (s_puddleRoot == null) s_puddleRoot = new GameObject("[Puddles]").transform;
            if (s_puddles.Count >= MaxPuddles)
            {
                if (s_puddles[0] != null) Destroy(s_puddles[0].gameObject);
                s_puddles.RemoveAt(0); s_puddleVol.RemoveAt(0);
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Puddle";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(s_puddleRoot, false);
            go.transform.position = p + Vector3.up * 0.003f;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = lib.puddle;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock();
            Color c = liquidColor * 0.45f; c.a = 1f;
            mpb.SetColor("_BaseColor", c);
            r.SetPropertyBlock(mpb);
            s_puddles.Add(go.transform); s_puddleVol.Add(0f);
            idx = s_puddles.Count - 1;
            EquipFx.Notice("spill", "바닥에 " + liquidName + "이(가) 흘렀습니다", "오일 받이(드레이너)를 아래에 대고 부으세요", EquipFx.Warn, 2.5f, 15f);
        }
        s_puddleVol[idx] += liters;
        // 넓이 ∝ 부피 (2mm 두께로 퍼진다고 보고, 최대 지름 1.2m)
        float area = s_puddleVol[idx] / 1000f / 0.002f;
        float diameter = Mathf.Clamp(Mathf.Sqrt(area / Mathf.PI) * 2f, 0.05f, 1.2f);
        s_puddles[idx].localScale = new Vector3(diameter, 0.0015f, diameter);
    }
}
