using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UMesh = UnityEngine.Mesh;

// ============================================================
//  AccidentFx.cs
//  기획서 '사고 시스템'의 연출을 코드로 만든다(에셋 없이 — 파티클·소리·메시 조각을 런타임에 생성).
//
//   · Fire      — 배터리 열폭주·화재: 흰 연기 → 가스 폭발음 → 제트 불꽃 + 검은 연기 + 불씨 + 열 왜곡 판
//   · Shatter   — 대형 부품 낙하: 부품 메시를 여러 조각(중앙값 분할)으로 쪼개 흩어지게(Rigidbody) 한다
//   · Pool      — 냉각수 누출: 바닥에 퍼지는 액체 웅덩이(클릭해서 닦기) + 화면 가장자리 얼룩 + 쏟아지는 소리
//   · SmallSpark— 비절연 공구를 고전압 근처에서 쓸 때: 스파크 + 경고음
//  모든 함수는 '이 기기에서 보이는 연출'만 만든다(네트워크 동기화는 부르는 쪽 RPC 가 맡는다).
// ============================================================
public static class AccidentFx
{
    // ── 코루틴 실행기 ──
    private class Runner : MonoBehaviour { }
    private static Runner s_runner;
    private static Runner R
    {
        get
        {
            if (s_runner == null)
            {
                var go = new GameObject("[AccidentFx]");
                UnityEngine.Object.DontDestroyOnLoad(go);
                s_runner = go.AddComponent<Runner>();
            }
            return s_runner;
        }
    }
    private static void Run(IEnumerator e) { R.StartCoroutine(e); }

    private static bool IsVR => XRAirpotrSecurity.Utils.GetPlatformType() == XRAirpotrSecurity.PlatformType.VR;

    // ══════════════════════════════════════════
    //  재질 · 텍스처
    // ══════════════════════════════════════════
    private static Texture2D s_soft, s_disc;
    private static Material s_particleMat, s_poolMat;

    private static Texture2D SoftTex()
    {
        if (s_soft != null) return s_soft;
        const int n = 64;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "AccidentSoft" };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                float a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply();
        return s_soft = t;
    }

    // 웅덩이용 — 가장자리가 불규칙하고 비교적 또렷한 원반
    private static Texture2D DiscTex()
    {
        if (s_disc != null) return s_disc;
        const int n = 128;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "AccidentDisc" };
        var rnd = new System.Random(11);
        float[] lobe = new float[7];
        for (int i = 0; i < lobe.Length; i++) lobe[i] = (float)rnd.NextDouble() * 6.28f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Vector2 p = new Vector2(x - n * 0.5f, y - n * 0.5f) / (n * 0.5f);
                float ang = Mathf.Atan2(p.y, p.x);
                float edge = 0.82f + 0.07f * Mathf.Sin(ang * 3f + lobe[0]) + 0.05f * Mathf.Sin(ang * 5f + lobe[1]) + 0.03f * Mathf.Sin(ang * 9f + lobe[2]);
                float d = p.magnitude;
                float a = Mathf.Clamp01((edge - d) / 0.08f);
                float inner = 0.85f + 0.15f * Mathf.Clamp01(1f - d);   // 가운데가 조금 더 진하게
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a * inner));
            }
        t.Apply();
        return s_disc = t;
    }

    private static Material ParticleMat()
    {
        if (s_particleMat != null) return s_particleMat;
        var sd = Shader.Find("Sprites/Default");
        if (sd == null) return null;
        s_particleMat = new Material(sd) { name = "AccidentParticle" };
        s_particleMat.mainTexture = SoftTex();
        return s_particleMat;
    }

    private static Material PoolMat()
    {
        if (s_poolMat != null) return s_poolMat;
        var sd = Shader.Find("Sprites/Default");
        if (sd == null) return null;
        s_poolMat = new Material(sd) { name = "AccidentPool" };
        s_poolMat.mainTexture = DiscTex();
        return s_poolMat;
    }

    // ══════════════════════════════════════════
    //  소리(절차 생성)
    // ══════════════════════════════════════════
    private static readonly Dictionary<string, AudioClip> s_clips = new Dictionary<string, AudioClip>();

    private static AudioClip Clip(string name, float seconds, Func<float, float, float> gen, int sr = 22050)
    {
        if (s_clips.TryGetValue(name, out var c) && c != null) return c;
        int n = Mathf.Max(1, (int)(seconds * sr));
        var d = new float[n];
        for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(gen(i / (float)sr, seconds), -1f, 1f);
        c = AudioClip.Create(name, n, 1, sr, false);
        c.SetData(d, 0);
        s_clips[name] = c;
        return c;
    }

    private static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);

    /// <summary>가스 폭발음 — 낮은 쿵 + 쏴 하는 잡음</summary>
    public static AudioClip Boom()
    {
        var r = new System.Random(3);
        return Clip("AccBoom", 1.8f, (t, L) =>
            Noise(r) * Mathf.Exp(-t * 4.5f) * 0.85f + Mathf.Sin(6.2832f * (62f - 20f * t) * t) * Mathf.Exp(-t * 3.2f) * 0.9f);
    }

    /// <summary>금속·셀이 부서지는 소리 — 시간차를 둔 여러 번의 잡음 파열</summary>
    public static AudioClip Crash()
    {
        var r = new System.Random(5);
        float[] at = { 0f, 0.07f, 0.16f, 0.29f, 0.45f, 0.62f };
        float prev = 0f;
        return Clip("AccCrash", 1.5f, (t, L) =>
        {
            float v = 0f;
            for (int i = 0; i < at.Length; i++)
                if (t >= at[i]) v += Noise(r) * Mathf.Exp(-(t - at[i]) * (9f + i * 3f)) * (0.8f - i * 0.1f);
            float hp = v - prev * 0.6f; prev = v;   // 조금 날카롭게
            return hp + Mathf.Sin(6.2832f * 90f * t) * Mathf.Exp(-t * 14f) * 0.5f;
        });
    }

    /// <summary>가스가 새는 쉬익 소리(반복용)</summary>
    public static AudioClip Hiss()
    {
        var r = new System.Random(9);
        float prev = 0f;
        return Clip("AccHiss", 2f, (t, L) => { float v = Noise(r); float o = v - prev * 0.8f; prev = v; return o * 0.22f; });
    }

    /// <summary>불이 타닥거리는 소리(반복용)</summary>
    public static AudioClip Crackle()
    {
        var r = new System.Random(13);
        float env = 0f;
        return Clip("AccCrackle", 2.5f, (t, L) =>
        {
            if (r.NextDouble() < 0.0035) env = 0.9f;
            env *= 0.985f;
            return Noise(r) * env * 0.6f + Noise(r) * 0.04f;
        });
    }

    /// <summary>액체가 쏟아지는 소리 — 꾸르륵(사인 스윕) + 쏴</summary>
    public static AudioClip Glug()
    {
        var r = new System.Random(17);
        float prev = 0f;
        return Clip("AccGlug", 2.2f, (t, L) =>
        {
            float env = Mathf.Clamp01(t * 6f) * Mathf.Clamp01((L - t) * 2f);
            float bub = Mathf.Sin(6.2832f * (180f + 140f * Mathf.Sin(t * 11f) + 90f * Mathf.Sin(t * 4.3f)) * t) * 0.28f;
            float v = Noise(r); float hiss = (v - prev * 0.7f) * 0.2f; prev = v;
            return (bub + hiss) * env;
        });
    }

    /// <summary>스파크 + 경고 신호음</summary>
    public static AudioClip Zap()
    {
        var r = new System.Random(21);
        return Clip("AccZap", 0.9f, (t, L) =>
        {
            float zap = Noise(r) * Mathf.Exp(-t * 18f) * 0.9f;
            float beep = (t > 0.18f && Mathf.Repeat(t - 0.18f, 0.2f) < 0.1f) ? Mathf.Sin(6.2832f * 980f * t) * 0.35f : 0f;
            return zap + beep;
        });
    }

    private static void PlayAt(AudioClip c, Vector3 pos, float vol = 1f)
    {
        if (c != null) AudioSource.PlayClipAtPoint(c, pos, vol);
    }

    // ══════════════════════════════════════════
    //  파티클 도우미
    // ══════════════════════════════════════════
    private static ParticleSystem NewPS(string name, Transform parent, Vector3 lp, bool stretch = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false; main.loop = true; main.duration = 5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        var m = ParticleMat();
        if (m != null) r.sharedMaterial = m;
        return ps;
    }

    private static Gradient Grad(Color[] c, float[] ct, float[] a, float[] at)
    {
        var g = new Gradient();
        var ck = new GradientColorKey[c.Length]; for (int i = 0; i < c.Length; i++) ck[i] = new GradientColorKey(c[i], ct[i]);
        var ak = new GradientAlphaKey[a.Length]; for (int i = 0; i < a.Length; i++) ak[i] = new GradientAlphaKey(a[i], at[i]);
        g.SetKeys(ck, ak);
        return g;
    }

    // ══════════════════════════════════════════
    //  1) 화재 — 열폭주
    // ══════════════════════════════════════════
    /// <summary>
    /// 배터리 열폭주 연출. 0~boomAt 초: 흰 연기가 피어오른다 → 가스 폭발음 + 제트 불꽃·불길·검은 연기·불씨·열 왜곡 → seconds 초 뒤 사그라든다.
    /// </summary>
    public static GameObject Fire(Vector3 pos, float seconds = 40f, float boomAt = 2.5f, float scale = 1f)
    {
        var root = new GameObject("[AccidentFire]");
        root.transform.position = pos;
        Run(FireRoutine(root, seconds, boomAt, scale));
        return root;
    }

    private static IEnumerator FireRoutine(GameObject root, float seconds, float boomAt, float s)
    {
        var tr = root.transform;
        Vector3 pos = tr.position;

        // 연기 — 처음엔 하얗고, 폭발 뒤 검게(시작 색을 시간에 따라 바꾼다)
        var smoke = NewPS("Smoke", tr, Vector3.up * 0.1f * s);
        {
            var m = smoke.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5.5f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.5f * s, 1.3f * s);
            m.startSize = new ParticleSystem.MinMaxCurve(0.5f * s, 1.1f * s);
            m.startColor = new Color(0.95f, 0.95f, 0.95f, 0.85f);
            m.gravityModifier = -0.03f; m.maxParticles = 320;
            var e = smoke.emission; e.rateOverTime = 16f;
            var sh = smoke.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 20f; sh.radius = 0.22f * s; sh.rotation = new Vector3(-90f, 0f, 0f);
            var col = smoke.colorOverLifetime; col.enabled = true;
            col.color = Grad(new[] { Color.white, Color.white }, new[] { 0f, 1f }, new[] { 0f, 0.8f, 0f }, new[] { 0f, 0.12f, 1f });
            var sz = smoke.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.35f, 1f, 1.7f));
        }
        smoke.Play();

        // 불길
        var flame = NewPS("Flame", tr, Vector3.up * 0.05f * s);
        {
            var m = flame.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.95f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.9f * s, 2.2f * s);
            m.startSize = new ParticleSystem.MinMaxCurve(0.28f * s, 0.55f * s);
            m.startColor = new Color(1f, 0.75f, 0.25f, 0.95f);
            m.gravityModifier = -0.12f; m.maxParticles = 260;
            var e = flame.emission; e.rateOverTime = 0f;
            var sh = flame.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 14f; sh.radius = 0.3f * s; sh.rotation = new Vector3(-90f, 0f, 0f);
            var col = flame.colorOverLifetime; col.enabled = true;
            col.color = Grad(new[] { new Color(1f, 0.95f, 0.6f), new Color(1f, 0.55f, 0.1f), new Color(0.8f, 0.12f, 0.02f) }, new[] { 0f, 0.4f, 1f },
                             new[] { 0f, 0.95f, 0f }, new[] { 0f, 0.15f, 1f });
            var sz = flame.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f));
        }
        flame.Play();

        // 제트 불꽃 — 셀 가스가 뿜어 나오는 가는 불줄기(펄스)
        var jet = NewPS("Jet", tr, Vector3.up * 0.1f * s, true);
        {
            var m = jet.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.4f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(6f * s, 10f * s);
            m.startSize = new ParticleSystem.MinMaxCurve(0.12f * s, 0.2f * s);
            m.startColor = new Color(1f, 0.85f, 0.4f, 1f);
            m.gravityModifier = 0f; m.maxParticles = 200;
            var e = jet.emission; e.rateOverTime = 0f;
            var sh = jet.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 5f; sh.radius = 0.04f * s; sh.rotation = new Vector3(-90f, 0f, 0f);
            var col = jet.colorOverLifetime; col.enabled = true;
            col.color = Grad(new[] { Color.white, new Color(1f, 0.5f, 0.1f) }, new[] { 0f, 1f }, new[] { 1f, 0f }, new[] { 0f, 1f });
            var r = jet.GetComponent<ParticleSystemRenderer>(); r.lengthScale = 4f; r.velocityScale = 0.12f;
        }
        jet.Play();

        // 불씨
        var ember = NewPS("Embers", tr, Vector3.up * 0.1f * s, true);
        {
            var m = ember.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * s, 4.5f * s);
            m.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
            m.startColor = new Color(1f, 0.7f, 0.25f, 1f);
            m.gravityModifier = 0.35f; m.maxParticles = 160;
            var e = ember.emission; e.rateOverTime = 0f;
            var sh = ember.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 0.15f * s; sh.rotation = new Vector3(-90f, 0f, 0f);
            var col = ember.colorOverLifetime; col.enabled = true;
            col.color = Grad(new[] { Color.white, new Color(1f, 0.4f, 0.1f) }, new[] { 0f, 1f }, new[] { 1f, 0f }, new[] { 0f, 1f });
            var r = ember.GetComponent<ParticleSystemRenderer>(); r.lengthScale = 2f; r.velocityScale = 0.06f;
        }
        ember.Play();

        // 불빛 · 소리 · 열 왜곡
        var lgo = new GameObject("Light"); lgo.transform.SetParent(tr, false); lgo.transform.localPosition = Vector3.up * 0.4f * s;
        var light = lgo.AddComponent<Light>();
        light.type = LightType.Point; light.color = new Color(1f, 0.55f, 0.2f); light.range = 7f * s; light.intensity = 0f; light.shadows = LightShadows.None;

        var asrc = root.AddComponent<AudioSource>();
        asrc.clip = Hiss(); asrc.loop = true; asrc.spatialBlend = 1f; asrc.maxDistance = 25f; asrc.rolloffMode = AudioRolloffMode.Linear; asrc.volume = 0.5f;
        asrc.Play();

        GameObject heat = null;

        float t = 0f; bool boomed = false;
        while (t < seconds)
        {
            if (root == null) yield break;
            t += Time.deltaTime;

            // 연기 색: 흰색 → 검정(폭발 직후 2초 동안)
            float dark = Mathf.Clamp01((t - boomAt) / 2.5f);
            var sm = smoke.main;
            sm.startColor = Color.Lerp(new Color(0.95f, 0.95f, 0.95f, 0.85f), new Color(0.07f, 0.07f, 0.07f, 0.95f), dark);

            if (!boomed && t >= boomAt)
            {
                boomed = true;
                PlayAt(Boom(), pos, 1f);
                var se = smoke.emission; se.rateOverTime = 34f;
                var fe = flame.emission; fe.rateOverTime = 70f;
                var ee = ember.emission; ee.rateOverTime = 28f;
                asrc.clip = Crackle(); asrc.volume = 0.7f; asrc.Play();
                heat = MakeHeat(tr, s);
            }
            if (boomed)
            {
                // 제트: 0.6초 뿜고 0.7~1.4초 쉬기를 반복
                float cyc = Mathf.Repeat(t * 1.1f, 1.6f);
                var je = jet.emission; je.rateOverTime = cyc < 0.6f ? 120f : 0f;
                light.intensity = (2.5f + Mathf.PerlinNoise(t * 9f, 0.3f) * 3f) * s;
            }
            yield return null;
        }

        // 사그라듦
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        float f = 0f;
        while (f < 4f)
        {
            if (root == null) yield break;
            f += Time.deltaTime;
            light.intensity = Mathf.Lerp(light.intensity, 0f, Time.deltaTime * 2f);
            asrc.volume = Mathf.Lerp(asrc.volume, 0f, Time.deltaTime * 1.5f);
            if (heat != null && f > 1f) heat.SetActive(false);
            yield return null;
        }
        if (root != null) UnityEngine.Object.Destroy(root, 4f);
    }

    // 열 왜곡 판 — URP 의 Opaque Texture 를 켤 수 있을 때만 만든다
    private static GameObject MakeHeat(Transform parent, float s)
    {
        var sh = Resources.Load<Shader>("Shaders/AccidentHeat");
        if (sh == null || !sh.isSupported) return null;
        var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (urp == null) return null;
        if (!urp.supportsCameraOpaqueTexture) urp.supportsCameraOpaqueTexture = true;   // 에셋 값을 바꾸므로 플레이 중에만 유효(에디터는 에셋에 저장될 수 있음)
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var col = q.GetComponent<Collider>(); if (col != null) UnityEngine.Object.Destroy(col);
        q.name = "HeatDistortion";
        q.transform.SetParent(parent, false);
        q.transform.localPosition = Vector3.up * 1.0f * s;
        q.transform.localScale = new Vector3(1.6f * s, 2.2f * s, 1f);
        q.GetComponent<MeshRenderer>().sharedMaterial = new Material(sh);
        q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var bb = q.AddComponent<Billboard>();
        return q;
    }

    private class Billboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            var c = Camera.main;
            if (c == null) return;
            Vector3 f = transform.position - c.transform.position; f.y = 0f;
            if (f.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
        }
    }

    // ══════════════════════════════════════════
    //  2) 파쇄 — 메시를 여러 조각으로
    // ══════════════════════════════════════════
    /// <summary>
    /// root 아래 MeshFilter 들의 메시를 pieces 개 안팎의 조각으로 쪼개 월드에 흩뿌린다(각 조각 = Rigidbody + 상자 콜라이더).
    /// 원본 root 는 지운다. center 에서 바깥으로 power(m/s) 정도로 튄다. 조각은 life 초 뒤 줄어들며 사라진다.
    /// </summary>
    public static List<GameObject> Shatter(Transform root, Vector3 center, float power = 3.5f, int pieces = 30, float life = 45f)
    {
        var made = new List<GameObject>();
        if (root == null) return made;
        var mfs = new List<MeshFilter>(root.GetComponentsInChildren<MeshFilter>());
        mfs.RemoveAll(m => m == null || m.sharedMesh == null);
        if (mfs.Count == 0) { UnityEngine.Object.Destroy(root.gameObject); return made; }

        long totalTris = 0;
        foreach (var m in mfs) totalTris += m.sharedMesh.triangles.Length / 3;
        var rnd = new System.Random(Environment.TickCount);
        var holder = new GameObject("[Debris]");

        foreach (var mf in mfs)
        {
            var src = mf.sharedMesh;
            var mr = mf.GetComponent<MeshRenderer>();
            var mats = mr != null ? mr.sharedMaterials : null;
            int tris = src.triangles.Length / 3;
            int k = Mathf.Clamp(Mathf.RoundToInt(pieces * (float)tris / Mathf.Max(1, totalTris)), 1, 14);
            List<UMesh> parts = src.isReadable && k > 1 ? SplitMesh(src, k, rnd) : null;
            if (parts == null || parts.Count == 0) parts = new List<UMesh> { src };   // 읽을 수 없는 메시 — 통째로 한 덩어리

            foreach (var pm in parts)
            {
                var go = new GameObject(mf.name + "_shard");
                go.transform.SetParent(holder.transform, false);
                go.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                go.transform.localScale = mf.transform.lossyScale;
                go.layer = mf.gameObject.layer;
                go.AddComponent<MeshFilter>().sharedMesh = pm;
                var rend = go.AddComponent<MeshRenderer>();
                if (mats != null) rend.sharedMaterials = mats;

                var b = pm.bounds;
                var box = go.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = new Vector3(Mathf.Max(0.02f, b.size.x * 0.9f), Mathf.Max(0.02f, b.size.y * 0.9f), Mathf.Max(0.02f, b.size.z * 0.9f));

                var rb = go.AddComponent<Rigidbody>();
                Vector3 sz = Vector3.Scale(b.size, go.transform.localScale);
                rb.mass = Mathf.Clamp(Mathf.Abs(sz.x * sz.y * sz.z) * 600f, 0.3f, 25f);
                rb.linearDamping = 0.1f; rb.angularDamping = 0.2f;
                Vector3 wc = go.transform.TransformPoint(b.center);
                Vector3 dir = (wc - center); dir.y = Mathf.Max(dir.y, 0f) + 0.25f;
                dir = (dir.normalized + new Vector3(Noise(rnd), Noise(rnd) * 0.3f, Noise(rnd)) * 0.5f).normalized;
                rb.linearVelocity = dir * power * (0.5f + (float)rnd.NextDouble() * 0.9f);
                rb.angularVelocity = new Vector3(Noise(rnd), Noise(rnd), Noise(rnd)) * power * 2f;
                made.Add(go);
            }
        }

        UnityEngine.Object.Destroy(root.gameObject);
        Run(DebrisLife(holder, made, life));
        return made;
    }

    private static IEnumerator DebrisLife(GameObject holder, List<GameObject> shards, float life)
    {
        yield return new WaitForSeconds(life);
        float t = 0f;
        var start = new List<Vector3>();
        foreach (var g in shards) start.Add(g != null ? g.transform.localScale : Vector3.one);
        while (t < 3f)
        {
            t += Time.deltaTime;
            for (int i = 0; i < shards.Count; i++) if (shards[i] != null) shards[i].transform.localScale = start[i] * Mathf.Clamp01(1f - t / 3f);
            yield return null;
        }
        if (holder != null) UnityEngine.Object.Destroy(holder);
    }

    /// <summary>삼각형 중심을 가장 긴 축 기준 중앙값 근처에서 반복해 나눠 k 개 조각 메시를 만든다.</summary>
    public static List<UMesh> SplitMesh(UMesh src, int k, System.Random rnd)
    {
        var verts = src.vertices; var norms = src.normals; var uvs = src.uv;
        bool hasN = norms != null && norms.Length == verts.Length;
        bool hasUv = uvs != null && uvs.Length == verts.Length;
        int subs = src.subMeshCount;
        var tri = new List<(int sub, int a, int b, int c, Vector3 cen)>();
        for (int s = 0; s < subs; s++)
        {
            var idx = src.GetTriangles(s);
            for (int i = 0; i + 2 < idx.Length; i += 3)
                tri.Add((s, idx[i], idx[i + 1], idx[i + 2], (verts[idx[i]] + verts[idx[i + 1]] + verts[idx[i + 2]]) / 3f));
        }
        if (tri.Count < 2) return null;

        var clusters = new List<List<int>> { new List<int>() };
        for (int i = 0; i < tri.Count; i++) clusters[0].Add(i);
        int guard = 0;
        while (clusters.Count < k && guard++ < 200)
        {
            int bi = 0;
            for (int i = 1; i < clusters.Count; i++) if (clusters[i].Count > clusters[bi].Count) bi = i;
            var big = clusters[bi];
            if (big.Count < 4) break;
            var bb = new Bounds(tri[big[0]].cen, Vector3.zero);
            foreach (int ti in big) bb.Encapsulate(tri[ti].cen);
            int axis = bb.size.x >= bb.size.y && bb.size.x >= bb.size.z ? 0 : (bb.size.y >= bb.size.z ? 1 : 2);
            big.Sort((x, y) => tri[x].cen[axis].CompareTo(tri[y].cen[axis]));
            int cut = Mathf.Clamp((int)(big.Count * (0.38f + (float)rnd.NextDouble() * 0.24f)), 1, big.Count - 1);
            var a = big.GetRange(0, cut); var b = big.GetRange(cut, big.Count - cut);
            clusters[bi] = a; clusters.Add(b);
        }

        var result = new List<UMesh>();
        foreach (var cl in clusters)
        {
            var map = new Dictionary<int, int>();
            var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pu = new List<Vector2>();
            var sub = new List<int>[subs]; for (int s = 0; s < subs; s++) sub[s] = new List<int>();
            foreach (int ti in cl)
            {
                var t = tri[ti];
                foreach (int vi in new[] { t.a, t.b, t.c })
                {
                    if (!map.TryGetValue(vi, out int ni))
                    {
                        ni = pv.Count; map[vi] = ni;
                        pv.Add(verts[vi]); if (hasN) pn.Add(norms[vi]); if (hasUv) pu.Add(uvs[vi]);
                    }
                    sub[t.sub].Add(ni);
                }
            }
            var m = new UMesh { name = src.name + "_shard", indexFormat = pv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(pv);
            if (hasN) m.SetNormals(pn); if (hasUv) m.SetUVs(0, pu);
            m.subMeshCount = subs;
            for (int s = 0; s < subs; s++) m.SetTriangles(sub[s], s, false);
            m.RecalculateBounds();
            if (!hasN) m.RecalculateNormals();
            result.Add(m);
        }
        return result;
    }

    // ══════════════════════════════════════════
    //  3) 냉각수 웅덩이 · 화면 얼룩
    // ══════════════════════════════════════════
    /// <summary>
    /// 바닥에 퍼지는 액체 웅덩이. 클릭하면 닦이고(wipes 번) 다 닦으면 onClean 호출. 반환: 웅덩이 오브젝트.
    /// </summary>
    public static GameObject Pool(Vector3 near, float radius, Color color, string label, int wipes, Action onClean)
    {
        float y = near.y;
        if (Physics.Raycast(near + Vector3.up * 0.6f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)) y = hit.point.y;
        var pos = new Vector3(near.x, y + 0.012f, near.z);

        // 바닥 위에 누운 판(눈에 보이는 웅덩이)은 자식, 클릭 상자는 뿌리(회전 없음 — 얇은 축이 위아래)
        var go = new GameObject("[CoolantPool]");
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * 0.05f;
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var qc = q.GetComponent<Collider>(); if (qc != null) UnityEngine.Object.Destroy(qc);
        q.transform.SetParent(go.transform, false);
        q.transform.localRotation = Quaternion.Euler(90f, UnityEngine.Random.value * 360f, 0f);
        var mr = q.GetComponent<MeshRenderer>();
        if (PoolMat() != null) mr.sharedMaterial = new Material(PoolMat()) { color = color };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        go.layer = 15;
        var box = go.AddComponent<BoxCollider>(); box.center = new Vector3(0f, 0.015f, 0f); box.size = new Vector3(1f, 0.03f, 1f);
        var proxy = go.AddComponent<ClickProxy>();
        int left = Mathf.Max(1, wipes);
        proxy.displayName = label; proxy.vrPokeable = true;
        proxy.hover = touch => $"눌러서 닦아냅니다 ({left}번 남음)";
        proxy.onClick = () =>
        {
            left--;
            PlayAt(Glug(), go.transform.position, 0.25f);
            if (left <= 0) { if (go != null) UnityEngine.Object.Destroy(go); onClean?.Invoke(); return; }
            Run(ShrinkTo(go.transform, (float)left / wipes));
        };

        Run(PoolGrow(go.transform, radius * 2f));
        PlayAt(Glug(), pos, 1f);
        Splash(pos, color);
        return go;
    }

    private static IEnumerator PoolGrow(Transform t, float diameter)
    {
        float k = 0f;
        while (k < 1f && t != null)
        {
            k += Time.deltaTime / 2.5f;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(k), 3f);
            t.localScale = Vector3.one * Mathf.Max(0.05f, diameter * e);
            yield return null;
        }
    }

    private static IEnumerator ShrinkTo(Transform t, float frac)
    {
        if (t == null) yield break;
        Vector3 a = t.localScale; Vector3 b = a * 0.72f;
        float k = 0f;
        while (k < 1f && t != null) { k += Time.deltaTime / 0.35f; t.localScale = Vector3.Lerp(a, b, k); yield return null; }
    }

    private static void Splash(Vector3 pos, Color color)
    {
        var go = new GameObject("[Splash]"); go.transform.position = pos + Vector3.up * 0.1f;
        var ps = NewPS("Drops", go.transform, Vector3.zero);
        var m = ps.main; m.loop = false; m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f); m.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.6f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f); m.startColor = new Color(color.r, color.g, color.b, 0.9f); m.gravityModifier = 1.2f;
        var e = ps.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 40f; sh.radius = 0.08f; sh.rotation = new Vector3(-90f, 0f, 0f);
        ps.Play();
        UnityEngine.Object.Destroy(go, 2f);
    }

    // 화면 가장자리 얼룩 (PC·모바일 — VR 은 화면 UI 가 없어 건너뛴다)
    private static GameObject s_stain;
    private static float s_stainEnd;

    public static void ScreenStain(Color color, float seconds)
    {
        if (IsVR || Application.isBatchMode) return;
        s_stainEnd = Mathf.Max(s_stainEnd, Time.unscaledTime + seconds);
        if (s_stain != null) return;
        Run(StainRoutine(color));
    }

    private static IEnumerator StainRoutine(Color color)
    {
        s_stain = new GameObject("[AccidentStain]");
        UnityEngine.Object.DontDestroyOnLoad(s_stain);
        var cv = s_stain.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 60;
        var sc = s_stain.AddComponent<UnityEngine.UI.CanvasScaler>();
        sc.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920f, 1080f);
        var imgs = new List<UnityEngine.UI.Image>();
        // 가장자리 얼룩 덩어리 — 모서리·변마다 크기·위치가 다른 번짐
        Vector2[] anchor = { new Vector2(0.03f, 0.1f), new Vector2(0.97f, 0.12f), new Vector2(0.08f, 0.93f), new Vector2(0.94f, 0.9f), new Vector2(0.5f, 0.02f), new Vector2(0.02f, 0.5f), new Vector2(0.98f, 0.55f) };
        float[] size = { 520f, 460f, 420f, 560f, 700f, 380f, 340f };
        var spr = Sprite.Create(SoftTex(), new Rect(0, 0, SoftTex().width, SoftTex().height), new Vector2(0.5f, 0.5f));
        for (int i = 0; i < anchor.Length; i++)
        {
            var go = new GameObject("Blob" + i, typeof(RectTransform)); go.transform.SetParent(s_stain.transform, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = anchor[i]; rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(size[i], size[i] * 0.8f);
            var im = go.AddComponent<UnityEngine.UI.Image>(); im.sprite = spr; im.raycastTarget = false; im.color = new Color(color.r, color.g, color.b, 0f);
            imgs.Add(im);
        }
        float a = 0f;
        while (Time.unscaledTime < s_stainEnd || a > 0.01f)
        {
            float target = Time.unscaledTime < s_stainEnd ? Mathf.Clamp01((s_stainEnd - Time.unscaledTime) / 4f) * 0.6f + 0.2f : 0f;
            a = Mathf.MoveTowards(a, target, Time.unscaledDeltaTime * 0.5f);
            foreach (var im in imgs) im.color = new Color(color.r, color.g, color.b, a);
            yield return null;
        }
        UnityEngine.Object.Destroy(s_stain); s_stain = null;
    }

    // ══════════════════════════════════════════
    //  4) 짧은 스파크 + 경고음
    // ══════════════════════════════════════════
    public static void SmallSpark(Vector3 pos)
    {
        var go = new GameObject("[WarnSpark]"); go.transform.position = pos;
        var ps = NewPS("Sparks", go.transform, Vector3.zero, true);
        var m = ps.main; m.loop = false; m.duration = 0.4f; m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 4f); m.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.025f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.92f, 0.55f), new Color(0.55f, 0.8f, 1f)); m.gravityModifier = 0.7f;
        var e = ps.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.02f;
        var r = ps.GetComponent<ParticleSystemRenderer>(); r.lengthScale = 2.5f; r.velocityScale = 0.08f;
        ps.Play();
        UnityEngine.Object.Destroy(go, 2f);
        PlayAt(Zap(), pos, 0.9f);
    }

    /// <summary>부품이 부서지는 소리</summary>
    public static void PlayCrash(Vector3 pos) => PlayAt(Crash(), pos, 1f);
}
