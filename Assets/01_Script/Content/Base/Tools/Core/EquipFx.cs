using System.Collections.Generic;
using Autohand;
using UnityEngine;

// ============================================================
//  EquipFx.cs
//
//  정비 장비 공통 효과 도구 모음. (볼트 관련은 Disassembly/Bolt/BoltWork·BoltHint, 내 손 목록은 Vr/Interaction/LocalHands 로 나눴다 — 11/20)
//  · EquipFx   — 파티클(안개·액체 줄기·불꽃·공기) 만들기, 합성 효과음(쉬익·드르륵·깨짐·콸콸), 안내 카드
// ============================================================
public static class EquipFx
{
    private static EquipmentFxLibrary s_lib;
    public static EquipmentFxLibrary Lib
    {
        get
        {
            if (s_lib == null) s_lib = Resources.Load<EquipmentFxLibrary>("Equipment/EquipmentFxLibrary");
            return s_lib;
        }
    }

    // ── 파티클 ─────────────────────────────

    public struct PsSettings
    {
        public Material mat;
        public Color color;
        public float lifetime, speed, size, sizeEnd, gravity, rate, coneAngle, radius;
        public int max;
        public bool stretch;
        public float drag;
    }

    /// <summary>부모 아래에 꺼진 상태의 반복 파티클을 만든다(Play/Stop 또는 emission rate 로 조절).</summary>
    public static ParticleSystem MakeParticles(Transform parent, string name, PsSettings s)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(s.lifetime * 0.7f, s.lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(s.speed * 0.8f, s.speed);
        main.startSize = new ParticleSystem.MinMaxCurve(s.size * 0.7f, s.size);
        main.startColor = s.color;
        main.gravityModifier = s.gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = s.max > 0 ? s.max : 300;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var em = ps.emission;
        em.rateOverTime = s.rate;

        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = s.coneAngle;
        sh.radius = Mathf.Max(0.001f, s.radius);

        if (s.sizeEnd > 0f && Mathf.Abs(s.sizeEnd - 1f) > 0.01f)
        {
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, s.sizeEnd));
        }

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        if (s.drag > 0f)
        {
            var lim = ps.limitVelocityOverLifetime;
            lim.enabled = true;
            lim.drag = s.drag;
            lim.multiplyDragByParticleSize = false;
            lim.multiplyDragByParticleVelocity = true;
        }

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = s.mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (s.stretch)
        {
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.08f;
            r.lengthScale = 1.2f;
        }
        return ps;
    }

    /// <summary>한 번 터지고 사라지는 불꽃(부품 파손·소켓 미끄러짐).</summary>
    public static void SparkBurst(Vector3 pos, Vector3 normal, int count = 40)
    {
        var lib = Lib;
        if (lib == null || lib.spark == null) return;
        var go = new GameObject("[SparkBurst]");
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(normal.sqrMagnitude > 1e-6f ? normal : Vector3.up);
        var ps = MakeParticles(go.transform, "Sparks", new PsSettings
        {
            mat = lib.spark, color = new Color(1f, 0.75f, 0.3f, 1f), lifetime = 0.5f, speed = 3.5f, size = 0.012f,
            gravity = 1f, rate = 0f, coneAngle = 55f, radius = 0.01f, max = 120, stretch = true
        });
        var em = ps.emission;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var main = ps.main; main.loop = false;
        ps.Play();
        Object.Destroy(go, 1.5f);
    }

    // ── 합성 효과음 ─────────────────────────────

    private const int Rate = 44100;
    private static AudioClip s_hiss, s_rattle, s_crack, s_pour, s_ratchet, s_slip, s_click;

    /// <summary>에어·스프레이 '쉬익' (반복용).</summary>
    public static AudioClip Hiss => s_hiss != null ? s_hiss : (s_hiss = Make("EquipHiss", 1f, (t, rnd) =>
    {
        float n = (float)rnd.NextDouble() * 2f - 1f;
        return n * 0.35f;
    }, lowpass: 0.55f));

    /// <summary>에어 임팩렌치 '드르르륵' (반복용) — 초당 약 22번 타격.</summary>
    public static AudioClip Rattle => s_rattle != null ? s_rattle : (s_rattle = Make("EquipRattle", 1f, (t, rnd) =>
    {
        float hit = (t * 22f) % 1f;
        float env = Mathf.Exp(-hit * 9f);
        float n = (float)rnd.NextDouble() * 2f - 1f;
        float motor = Mathf.Sin(2f * Mathf.PI * 180f * t) * 0.15f + Mathf.Sin(2f * Mathf.PI * 360f * t) * 0.08f;
        return n * env * 0.7f + motor + n * 0.08f;
    }, lowpass: 0.35f));

    /// <summary>금속이 깨지는 '쾅·쨍' (한 번).</summary>
    public static AudioClip Crack => s_crack != null ? s_crack : (s_crack = Make("EquipCrack", 0.6f, (t, rnd) =>
    {
        float n = (float)rnd.NextDouble() * 2f - 1f;
        float ring = Mathf.Sin(2f * Mathf.PI * 2600f * t) * 0.3f + Mathf.Sin(2f * Mathf.PI * 4100f * t) * 0.2f;
        return (n * Mathf.Exp(-t * 18f) * 0.9f + ring * Mathf.Exp(-t * 9f)) * 0.9f;
    }, lowpass: 0.9f, loop: false));

    /// <summary>액체가 흘러내리는 '콸콸' (반복용).</summary>
    public static AudioClip Pour => s_pour != null ? s_pour : (s_pour = Make("EquipPour", 1.5f, (t, rnd) =>
    {
        float n = (float)rnd.NextDouble() * 2f - 1f;
        float bubble = Mathf.Sin(2f * Mathf.PI * (300f + 120f * Mathf.Sin(t * 13f)) * t) * Mathf.Max(0f, Mathf.Sin(t * 37f)) * 0.15f;
        return n * 0.25f + bubble;
    }, lowpass: 0.2f));

    /// <summary>스패너 '딱' (라쳇 한 칸).</summary>
    public static AudioClip Ratchet => s_ratchet != null ? s_ratchet : (s_ratchet = Make("EquipRatchet", 0.08f, (t, rnd) =>
    {
        float n = (float)rnd.NextDouble() * 2f - 1f;
        return n * Mathf.Exp(-t * 300f) + Mathf.Sin(2f * Mathf.PI * 3400f * t) * Mathf.Exp(-t * 80f) * 0.3f;
    }, lowpass: 1f, loop: false));

    /// <summary>소켓이 헛도는 '끼긱'.</summary>
    public static AudioClip Slip => s_slip != null ? s_slip : (s_slip = Make("EquipSlip", 0.35f, (t, rnd) =>
    {
        float saw = ((t * 900f) % 1f) * 2f - 1f;
        return saw * 0.3f * Mathf.Exp(-t * 6f) * (0.6f + 0.4f * Mathf.Sin(t * 120f));
    }, lowpass: 0.7f, loop: false));

    /// <summary>스위치·커플러 '딸깍'.</summary>
    public static AudioClip Click => s_click != null ? s_click : (s_click = Make("EquipClick", 0.1f, (t, rnd) =>
    {
        float n = (float)rnd.NextDouble() * 2f - 1f;
        return n * Mathf.Exp(-t * 250f) * 0.8f + Mathf.Sin(2f * Mathf.PI * 1800f * t) * Mathf.Exp(-t * 60f) * 0.2f;
    }, lowpass: 1f, loop: false));

    private static AudioClip Make(string name, float seconds, System.Func<float, System.Random, float> f, float lowpass, bool loop = true)
    {
        int len = (int)(Rate * seconds);
        var d = new float[len];
        var rnd = new System.Random(name.GetHashCode());
        float y = 0f;
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / Rate;
            float x = f(t, rnd);
            y += (x - y) * lowpass;   // 간단한 저역 통과
            d[i] = y;
        }
        if (loop)
        {
            // 이음매 클릭 방지: 앞뒤 20ms 교차 감쇠
            int fade = Mathf.Min(len / 4, Rate / 50);
            for (int i = 0; i < fade; i++)
            {
                float a = (float)i / fade;
                d[i] = d[i] * a + d[len - fade + i] * (1f - a);
            }
            System.Array.Resize(ref d, len - fade);
            len -= fade;
        }
        var c = AudioClip.Create(name, len, 1, Rate, false);
        c.SetData(d, 0);
        return c;
    }

    /// <summary>물체에 붙은 3D 반복 음원(없으면 만든다).</summary>
    public static AudioSource LoopSource(GameObject go, AudioClip clip, float maxDistance = 12f)
    {
        var a = go.AddComponent<AudioSource>();
        a.clip = clip;
        a.loop = true;
        a.playOnAwake = false;
        a.spatialBlend = 1f;
        a.rolloffMode = AudioRolloffMode.Logarithmic;
        a.minDistance = 0.5f;
        a.maxDistance = maxDistance;
        a.dopplerLevel = 0f;
        a.volume = 0f;
        return a;
    }

    /// <summary>반복 음원을 부드럽게 켜고 끈다(볼륨 목표값으로 이동).</summary>
    public static void Drive(AudioSource a, float targetVolume, float pitch = 1f)
    {
        if (a == null) return;
        a.volume = Mathf.MoveTowards(a.volume, targetVolume, Time.deltaTime * 4f);
        a.pitch = pitch;
        if (a.volume > 0.001f) { if (!a.isPlaying) a.Play(); }
        else if (a.isPlaying) a.Stop();
    }

    // ── 안내 카드 (같은 키는 seconds 동안 한 번만) ─────────────────────────────

    private static readonly Dictionary<string, float> s_noticeCooldown = new Dictionary<string, float>();
    public static readonly Color Info = new Color(0.08f, 0.22f, 0.36f, 0.92f);
    public static readonly Color Warn = new Color(0.55f, 0.3f, 0.02f, 0.92f);
    public static readonly Color Danger = new Color(0.6f, 0.03f, 0.03f, 0.95f);
    /// <summary>낙하·파손 위험(보라) — 감전(빨강 Danger)·일반 경고(주황 Warn)와 구분</summary>
    public static readonly Color Hazard = new Color(0.34f, 0.12f, 0.52f, 0.95f);

    /// <summary>PC: 이번 프레임 마우스 휠을 굴렸는가(UI 위에서 굴린 것 제외). 든 도구의 '다음 소켓/다음 색' 입력.
    /// 예전엔 Q 였는데 Q 가 높이 올리기와 겹쳐, 이 도구를 든 동안엔 높이를 올릴 수 없었다.</summary>
    public static bool PcWheelStep()
    {
        if (Mathf.Abs(Input.mouseScrollDelta.y) < 0.01f) return false;
        var es = UnityEngine.EventSystems.EventSystem.current;
        return es == null || !es.IsPointerOverGameObject();
    }

    public static void Notice(string key, string title, string message, Color tint, float show = 2.5f, float cooldown = 4f)
    {
        // VR 손바닥 메뉴 '설명창' 끔 → 사용법·매뉴얼(파랑 Info) 카드만 숨긴다. 막힘·녹슨 볼트·위험(주황·빨강)은 작업 피드백이라 계속 띄운다
        if (!InfoCardPolicy.Allowed && tint == Info) return;
        float now = Time.unscaledTime;
        if (s_noticeCooldown.TryGetValue(key, out var until) && now < until) return;
        s_noticeCooldown[key] = now + cooldown;
        WarningPopup.Show("equip", title, message, tint, show);
    }

    public static void Haptic(Hand h, float duration, float amp)
    {
        if (h == null) return;
        try { h.PlayHapticVibration(duration, amp); } catch { }
    }

    /// <summary>bounds 기준 물체의 '위쪽' 로컬 축(모델 축이 제각각이라 처음 놓인 자세에서 세계 위쪽에 가장 가까운 축).</summary>
    public static Vector3 UpAxisLocal(Transform t)
    {
        Vector3 best = Vector3.up; float bd = -2f;
        foreach (var ax in new[] { Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
        {
            float d = Vector3.Dot(t.TransformDirection(ax), Vector3.up);
            if (d > bd) { bd = d; best = ax; }
        }
        return best;
    }

    public static bool LocalBounds(Transform root, out Bounds b)
    {
        b = default; bool any = false;
        var inv = root.worldToLocalMatrix;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            var wb = r.bounds;
            Vector3 c = wb.center, e = wb.extents;
            for (int i = 0; i < 8; i++)
            {
                var p = inv.MultiplyPoint3x4(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        }
        return any;
    }
}
