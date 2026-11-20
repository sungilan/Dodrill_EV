using System.Collections.Generic;
using Autohand;
using UnityEngine;

// ============================================================
//  WorldSfx.cs
//
//  공간(3D) 효과음. 소리가 난 위치에서 들리게 재생한다(기존 SoundManager 는 모두 2D).
//  · 떨어진 물건이 바닥·작업대에 닿을 때 '툭' (속도에 따라 크기) — 모든 플랫폼, 다른 사람이 떨어뜨린 것도
//    놓은 물건은 서버에서만 물리로 떨어지고 클라이언트는 위치만 받으므로, 충돌 이벤트 대신 '빠르게 내려오다 멈춤'으로 판단한다.
//  · 볼트가 빠질 때 짧은 금속음(코드로 합성) — BoltGroupCounter 가 부른다
//  · VR 에서 물건을 잡을 때 '착' — PC·모바일은 기존 2D 소리가 있다
//  · VR 도구 벨트에 꽂을 때 '딸깍' — VrToolBelt 가 부른다
//  씬 배치 불필요 — 게임 시작 시 자동 생성된다.
// ============================================================
public class WorldSfx : MonoBehaviour
{
    private const int PoolSize = 10;
    private const float ImpactMinSpeed = 1.0f;     // 이보다 빨리 내려오다 멈추면 착지음
    private const float TeleportSpeed = 15f;       // 이보다 빠르면 순간이동(스폰·리셋)으로 보고 무시

    private static WorldSfx s_inst;
    private readonly List<AudioSource> _pool = new List<AudioSource>();
    private int _next;
    private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
    private AudioClip _boltClip, _clickClip, _torqueClip;

    private class Track { public Vector3 lastPos; public float recentDown; public float recentUntil; public float cooldown; public bool init; public float fall; public int hits; public float hitWindow; public float muteUntil; }
    private const float MinFallDistance = 0.08f;   // 이만큼은 실제로 떨어져야 착지음(제자리 떨림·위치 보정은 무시)
    private readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();
    private readonly List<int> _dead = new List<int>();

    private readonly List<Hand> _hands = new List<Hand>();
    private float _nextHandScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (s_inst != null || FindAnyObjectByType<WorldSfx>() != null) return;
        var go = new GameObject("[WorldSfx]");
        DontDestroyOnLoad(go);
        s_inst = go.AddComponent<WorldSfx>();
    }

    private void Awake()
    {
        s_inst = this;
        for (int i = 0; i < PoolSize; i++)
        {
            var go = new GameObject("Sfx" + i);
            go.transform.SetParent(transform, false);
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Logarithmic;
            a.minDistance = 0.6f;
            a.maxDistance = 20f;
            a.dopplerLevel = 0f;
            _pool.Add(a);
        }
    }

    // ── 재생 API ─────────────────────────────

    /// <summary>Resources/Sounds/{name} 을 위치에서 재생.</summary>
    public static void PlayAt(string name, Vector3 pos, float volume = 1f, float pitch = 1f)
    {
        if (s_inst == null) return;
        PlayClipAt(s_inst.Clip(name), pos, volume, pitch);
    }

    public static void PlayClipAt(AudioClip clip, Vector3 pos, float volume = 1f, float pitch = 1f)
    {
        if (s_inst == null || clip == null) return;
        var a = s_inst._pool[s_inst._next];
        s_inst._next = (s_inst._next + 1) % s_inst._pool.Count;
        a.transform.position = pos;
        a.pitch = pitch;
        a.PlayOneShot(clip, volume);
    }

    /// <summary>볼트가 빠지는 짧은 금속음.</summary>
    public static void BoltOut(Vector3 pos)
    {
        if (s_inst == null) return;
        if (s_inst._boltClip == null) s_inst._boltClip = MakeMetalPing();
        PlayClipAt(s_inst._boltClip, pos, 0.55f, Random.Range(0.92f, 1.08f));
    }

    /// <summary>도구를 벨트에 꽂는 '딸깍'.</summary>
    public static void Click(Vector3 pos)
    {
        if (s_inst == null) return;
        if (s_inst._clickClip == null) s_inst._clickClip = MakeClick();
        PlayClipAt(s_inst._clickClip, pos, 0.7f, 1f);
    }

    /// <summary>볼트를 끝까지 조였을 때 토크렌치의 '딸깍'(짧은 금속 클릭 두 번).</summary>
    public static void TorqueClick(Vector3 pos)
    {
        if (s_inst == null) return;
        if (s_inst._torqueClip == null) s_inst._torqueClip = MakeTorqueClick();
        PlayClipAt(s_inst._torqueClip, pos, 0.85f, Random.Range(0.97f, 1.03f));
    }

    private AudioClip Clip(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_clips.TryGetValue(name, out var c)) return c;
        c = Resources.Load<AudioClip>("Sounds/" + name);
        _clips[name] = c;
        return c;
    }

    // ── 착지음: 떨어지던 물건이 멈출 때 ─────────────────

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        float now = Time.time;

        foreach (var kv in TaskItem.Registry)
        {
            var ti = kv.Value;
            if (ti == null || !ti.isActiveAndEnabled) continue;
            if (!_tracks.TryGetValue(kv.Key, out var t)) { t = new Track(); _tracks[kv.Key] = t; }
            Vector3 p = ti.transform.position;
            if (!t.init) { t.init = true; t.lastPos = p; continue; }

            Vector3 v = (p - t.lastPos) / dt;
            t.lastPos = p;
            float speed = v.magnitude;
            if (speed > TeleportSpeed) { t.recentDown = 0f; continue; }

            var sg = ti.GetComponent<SyncGrab>();
            bool inHand = sg != null && (sg.IsGrabbed || sg.IsHolstered);
            if (inHand) { t.recentDown = 0f; t.fall = 0f; continue; }

            if (v.y < -0.05f) t.fall += -v.y * dt;
            if (-v.y > t.recentDown) { t.recentDown = -v.y; t.recentUntil = now + 0.2f; }
            else if (now > t.recentUntil) { t.recentDown = 0f; t.fall = 0f; }

            if (t.recentDown >= ImpactMinSpeed && speed < 0.15f && now > t.cooldown)
            {
                float vol = Mathf.Clamp01(t.recentDown / 4f) * 0.9f + 0.1f;
                bool realFall = t.fall >= MinFallDistance;
                t.recentDown = 0f; t.fall = 0f;
                t.cooldown = now + 1.2f;
                if (!realFall || now < t.muteUntil) continue;
                // 같은 물건이 짧은 시간에 계속 '착지'하면(물리 떨림·위치 보정 반복) 한동안 소리를 끈다 — '찰칵찰칵' 반복 방지
                if (now > t.hitWindow) { t.hitWindow = now + 6f; t.hits = 0; }
                if (++t.hits >= 3) { t.muteUntil = now + 15f; continue; }
                PlayAt("Item_Drop", p, vol, Random.Range(0.9f, 1.1f));
            }
        }

        // 사라진 물건 정리
        if (_tracks.Count > TaskItem.Registry.Count + 16)
        {
            _dead.Clear();
            foreach (var id in _tracks.Keys) if (!TaskItem.Registry.ContainsKey(id)) _dead.Add(id);
            foreach (var id in _dead) _tracks.Remove(id);
        }

        // VR 잡기 소리
        if (VrProbeHandling.IsVRDevice && Time.unscaledTime >= _nextHandScan)
        {
            _nextHandScan = Time.unscaledTime + 2f;
            _hands.RemoveAll(h => h == null);
            foreach (var hand in FindObjectsByType<Hand>(FindObjectsSortMode.None))
            {
                if (hand == null || _hands.Contains(hand)) continue;
                var nob = hand.GetComponentInParent<FishNet.Object.NetworkObject>();
                if (nob != null && nob.IsSpawned && !nob.IsOwner) continue;
                hand.OnGrabbed += OnHandGrabbed;
                _hands.Add(hand);
            }
        }
    }

    private void OnDestroy()
    {
        foreach (var h in _hands) if (h != null) h.OnGrabbed -= OnHandGrabbed;
        if (s_inst == this) s_inst = null;
    }

    private static readonly Dictionary<int, float> s_grabCooldown = new Dictionary<int, float>();
    private static void OnHandGrabbed(Hand hand, Grabbable grab)
    {
        if (grab == null) return;
        // 잡기 재시도(소유권 확인 후 다시 잡기)·두 손 잡기로 같은 물건 소리가 연달아 나지 않게
        int gid = grab.GetInstanceID();
        float gnow = Time.unscaledTime;
        if (s_grabCooldown.TryGetValue(gid, out var until) && gnow < until) return;
        s_grabCooldown[gid] = gnow + 0.6f;
        PlayAt("Item_Grab", grab.transform.position, 0.6f, Random.Range(0.95f, 1.05f));
    }

    // ── 합성음 ─────────────────────────────

    private const int Rate = 44100;

    /// <summary>볼트가 빠질 때: 높은 금속 '팅' + 짧은 긁힘.</summary>
    private static AudioClip MakeMetalPing()
    {
        int len = (int)(Rate * 0.35f);
        var d = new float[len];
        var rng = new System.Random(7);
        float[] f = { 2350f, 3870f, 5420f, 7300f };
        float[] a = { 0.5f, 0.3f, 0.18f, 0.1f };
        float[] k = { 14f, 18f, 24f, 32f };
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / Rate;
            float s = 0f;
            for (int j = 0; j < f.Length; j++) s += Mathf.Sin(2f * Mathf.PI * f[j] * t) * a[j] * Mathf.Exp(-t * k[j]);
            float scrape = t < 0.03f ? ((float)rng.NextDouble() * 2f - 1f) * 0.25f * (1f - t / 0.03f) : 0f;
            d[i] = (s + scrape) * Mathf.Clamp01(t / 0.002f) * 0.7f;
        }
        var c = AudioClip.Create("BoltOut", len, 1, Rate, false);
        c.SetData(d, 0);
        return c;
    }

    /// <summary>토크렌치 '딸깍': 단단한 금속 클릭 + 바로 뒤 작은 반동 클릭, 짧은 금속 울림.</summary>
    private static AudioClip MakeTorqueClick()
    {
        int len = (int)(Rate * 0.18f);
        var d = new float[len];
        var rng = new System.Random(11);
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / Rate;
            float n = (float)rng.NextDouble() * 2f - 1f;
            float c1 = n * Mathf.Exp(-t * 900f);
            float t2 = t - 0.022f;
            float c2 = t2 > 0f ? n * Mathf.Exp(-t2 * 1100f) * 0.55f : 0f;
            float ring = (Mathf.Sin(2f * Mathf.PI * 3100f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 4700f * t) * 0.2f) * Mathf.Exp(-t * 45f);
            float body = Mathf.Sin(2f * Mathf.PI * 420f * t) * Mathf.Exp(-t * 70f) * 0.35f;
            d[i] = (c1 * 0.9f + c2 + ring + body) * 0.75f;
        }
        var c = AudioClip.Create("TorqueClick", len, 1, Rate, false);
        c.SetData(d, 0);
        return c;
    }

    /// <summary>벨트 걸이에 꽂는 '딸깍'.</summary>
    private static AudioClip MakeClick()
    {
        int len = (int)(Rate * 0.12f);
        var d = new float[len];
        var rng = new System.Random(3);
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / Rate;
            float click1 = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 400f);
            float t2 = t - 0.035f;
            float click2 = t2 > 0f ? ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t2 * 300f) * 0.8f : 0f;
            float body = Mathf.Sin(2f * Mathf.PI * 180f * t) * Mathf.Exp(-t * 40f) * 0.5f;
            d[i] = (click1 * 0.6f + click2 * 0.6f + body) * 0.8f;
        }
        var c = AudioClip.Create("HolsterClick", len, 1, Rate, false);
        c.SetData(d, 0);
        return c;
    }
}
