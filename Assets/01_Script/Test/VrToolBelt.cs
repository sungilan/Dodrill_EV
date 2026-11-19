using System.Collections.Generic;
using Autohand;
using TMPro;
using UnityEngine;

// ============================================================
//  VrToolBelt.cs
//
//  VR 전용 — 허리 도구 벨트. 도구(임팩트 렌치·멀티미터·메거·그리스)를 허리 양옆 슬롯 근처에서 놓으면
//  바닥에 떨어지지 않고 그 자리에 꽂힌다. 손을 다시 대고 그립하면 꺼내진다.
//  차 밑에서 작업하다 도구를 놓쳐 찾으러 다니던 불편을 줄인다.
//
//  · 도구를 들고 있을 때만 허리에 슬롯 원이 보이고, 손이 가까우면 초록색으로 바뀐다.
//  · 꽂아도 서버에는 '놓기'를 보내지 않는다(내 소유·잡힘 상태 유지) → 다른 사람이 가져가지 못하고, 다른 화면에도 내 허리에 붙어 보인다.
//    (놓을 때 서버가 소유권을 가져가 중력으로 떨어뜨리는 SyncGrab 흐름을 SyncGrab.VrReleaseInterceptors 로 가로챈다)
//  · 몸 방향은 머리 방향을 천천히 따라간다(고개만 돌릴 때 벨트가 같이 휙 돌지 않게).
//  (11/19) 찾기 쉽게: 도구를 든 동안 슬롯에 ① 반투명 원판 ② 위로 솟는 빛기둥 ③ '공구 걸이 · 여기에 놓기' 글자가 뜨고,
//  꽂은 적이 없으면 도구를 2초 이상 들고 있을 때 안내 카드 + 선임 대사(tool_belt)를 60초마다 최대 3번 알려 준다(고개를 숙이면 허리 양옆).
//  씬 배치 불필요 — 게임 시작 시 자동 생성되고, VR 기기가 아니면 아무것도 하지 않는다.
// ============================================================
[DefaultExecutionOrder(300)]
public class VrToolBelt : MonoBehaviour
{
    private const float HipDrop = 0.48f;        // 목(머리 회전 중심)에서 허리 벨트까지(m) — 0.62 는 무릎처럼 느껴졌다
    private const float HipSide = 0.24f;        // 몸 중심에서 좌우(m)
    private const float HipForward = 0.1f;
    private const float HolsterRadius = 0.2f;   // 슬롯에서 이 거리 안에서 놓으면 꽂힌다
    private const float UnlockRadius = 0.3f;    // 손이 이만큼 가까우면 잡을 수 있게 물리를 푼다
    private static readonly Color IdleColor = new Color(1f, 1f, 1f, 0.35f);
    private static readonly Color ReadyColor = new Color(0.3f, 1f, 0.45f, 0.95f);

    private class Slot
    {
        public float side;              // -1 왼쪽, +1 오른쪽
        public SyncGrab item;
        public Rigidbody rb;
        public Grabbable grab;
        public Vector3 centerLocal;     // 도구 중심(도구 로컬)
        public Quaternion relRot;       // 몸 방향 기준 도구 회전
        public LineRenderer ring;
        public GameObject disc;
        public Material discMat;
        public LineRenderer pillar;
        public TextMeshPro label;
        public Vector3 pos;
    }

    private readonly Slot[] _slots = { new Slot { side = -1f }, new Slot { side = 1f } };
    private readonly List<Hand> _hands = new List<Hand>();
    private float _nextHandScan;
    private float _bodyYaw;
    private bool _yawInit;
    private Material _mat;
    private static VrToolBelt s_inst;
    private Quaternion _lastYaw = Quaternion.identity;
    private bool _framed;
    private bool _everHolstered;
    private float _toolHeldSince = -1f, _nextHint;
    private int _hintCount;
    private static TMP_FontAsset s_font;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindAnyObjectByType<VrToolBelt>() != null) return;
        var go = new GameObject("[VrToolBelt]");
        DontDestroyOnLoad(go);
        go.AddComponent<VrToolBelt>();
    }

    private void OnEnable() { s_inst = this; if (!SyncGrab.VrReleaseInterceptors.Contains(TryHolster)) SyncGrab.VrReleaseInterceptors.Add(TryHolster); }

    // ── 연습장(VrPracticeTutorial) 등 네트워크 없는 물건용: 슬롯 위치만 알려 준다(꽂아 두기는 부르는 쪽이 한다) ──

    /// <summary>슬롯 i(0 왼쪽, 1 오른쪽)의 이번 프레임 위치·몸 방향.</summary>
    public static bool TryGetSlot(int i, out Vector3 pos, out Quaternion yaw)
    {
        pos = default; yaw = Quaternion.identity;
        if (s_inst == null || !s_inst._framed || i < 0 || i >= s_inst._slots.Length) return false;
        pos = s_inst._slots[i].pos; yaw = s_inst._lastYaw;
        return true;
    }

    /// <summary>점 p 에서 radius 안의 빈 슬롯.</summary>
    public static bool TryGetFreeSlotNear(Vector3 p, float radius, out int index, out Vector3 pos, out Quaternion yaw)
    {
        index = -1; pos = default; yaw = Quaternion.identity;
        if (s_inst == null || !s_inst._framed) return false;
        float best = radius;
        for (int i = 0; i < s_inst._slots.Length; i++)
        {
            var s = s_inst._slots[i];
            if (s.item != null) continue;
            float d = Vector3.Distance(p, s.pos);
            if (d < best) { best = d; index = i; pos = s.pos; }
        }
        yaw = s_inst._lastYaw;
        return index >= 0;
    }

    private void OnDisable()
    {
        SyncGrab.VrReleaseInterceptors.Remove(TryHolster);
        foreach (var s in _slots) Free(s, false);
    }

    // ── 몸 기준 위치 ─────────────────────────────

    private bool BodyFrame(out Vector3 hip, out Quaternion yawRot)
    {
        hip = default; yawRot = Quaternion.identity;
        var cam = Camera.main;
        if (cam == null) return false;
        var h = cam.transform;
        Vector3 f = h.forward; f.y = 0f;
        if (f.sqrMagnitude < 1e-4f) f = h.up; f.y = 0f;
        float headYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        if (!_yawInit) { _bodyYaw = headYaw; _yawInit = true; }
        // 고개를 크게 돌리면 몸도 따라 돈다
        float diff = Mathf.DeltaAngle(_bodyYaw, headYaw);
        float rate = Mathf.Abs(diff) > 45f ? 240f : 60f;
        _bodyYaw = Mathf.MoveTowardsAngle(_bodyYaw, headYaw, rate * Time.deltaTime);
        yawRot = Quaternion.Euler(0f, _bodyYaw, 0f);
        // 고개를 숙이면 헤드셋이 앞·아래로 내려가 벨트도 따라 내려갔다 → 머리 회전 중심(목) 기준으로 잡는다
        Vector3 neck = h.position - h.rotation * new Vector3(0f, 0.08f, 0.09f) + Vector3.up * 0.08f;
        hip = neck + Vector3.down * HipDrop;
        return true;
    }

    private Vector3 SlotPos(Slot s, Vector3 hip, Quaternion yawRot)
        => hip + yawRot * new Vector3(HipSide * s.side, 0f, HipForward);

    // ── 매 프레임 ─────────────────────────────

    private void LateUpdate()
    {
        if (!VrProbeHandling.IsVRDevice) return;
        if (Time.unscaledTime >= _nextHandScan) { _nextHandScan = Time.unscaledTime + 2f; ScanHands(); }
        if (!BodyFrame(out var hip, out var yawRot)) return;
        _lastYaw = yawRot;
        _framed = true;

        bool holdingTool = false;
        foreach (var h in _hands)
        {
            var held = h != null ? h.GetHeld() : null;
            if (held == null) continue;
            var pti = held.GetComponentInParent<PracticeTutorialItem>();
            if (IsTool(held.GetComponentInParent<SyncGrab>()) || (pti != null && pti.holsterable)) { holdingTool = true; break; }
        }

        TrackHint(holdingTool);

        foreach (var s in _slots)
        {
            s.pos = SlotPos(s, hip, yawRot);

            if (s.item != null)
            {
                // 꺼냈거나(손에 잡힘) 더 이상 내 것이 아니면 슬롯을 비운다
                if (s.item == null || !s.item.isActiveAndEnabled || !s.item.IsOwner || !s.item.IsGrabbed) { Free(s, false); }
                else if (s.grab != null && s.grab.IsHeld()) { Free(s, true); }
                else Hold(s, yawRot);
            }

            DrawRing(s, holdingTool && s.item == null, yawRot);
        }
    }

    private void Hold(Slot s, Quaternion yawRot)
    {
        var t = s.item.transform;
        Quaternion rot = yawRot * s.relRot;
        Vector3 offset = rot * Vector3.Scale(s.centerLocal, t.lossyScale);
        t.SetPositionAndRotation(s.pos - offset, rot);

        if (s.rb != null)
        {
            // AutoHand 는 kinematic 물체를 잡지 않는다 → 손이 가까울 때만 물리를 풀어 둔다(중력은 끈 채)
            bool handNear = false;
            foreach (var h in _hands)
            {
                if (h == null || !h.isActiveAndEnabled) continue;
                Vector3 palm = h.palmTransform != null ? h.palmTransform.position : h.transform.position;
                if ((palm - s.pos).sqrMagnitude < UnlockRadius * UnlockRadius) { handNear = true; break; }
            }
            if (s.rb.isKinematic == handNear) s.rb.isKinematic = !handNear;
            s.rb.useGravity = false;
            if (!s.rb.isKinematic) { s.rb.linearVelocity = Vector3.zero; s.rb.angularVelocity = Vector3.zero; }
        }
    }

    /// <summary>SyncGrab.OnVRRelease 에서 불린다. 꽂았으면 true(서버에 놓기를 보내지 않음).</summary>
    private bool TryHolster(SyncGrab sg, Hand hand)
    {
        if (!isActiveAndEnabled || !VrProbeHandling.IsVRDevice || !IsTool(sg)) return false;
        if (!BodyFrame(out var hip, out var yawRot)) return false;

        Vector3 c = Center(sg.transform);
        Slot best = null;
        float bestD = HolsterRadius;
        foreach (var s in _slots)
        {
            if (s.item != null) continue;
            float d = Vector3.Distance(c, SlotPos(s, hip, yawRot));
            if (d < bestD) { bestD = d; best = s; }
        }
        if (best == null) return false;

        _everHolstered = true;
        WarningPopup.Hide("vrbelt");
        best.item = sg;
        best.grab = sg.GetComponentInChildren<Grabbable>();
        best.rb = sg.GetComponent<Rigidbody>();
        best.centerLocal = sg.transform.InverseTransformPoint(c);
        best.relRot = Quaternion.Inverse(yawRot) * sg.transform.rotation;
        best.pos = SlotPos(best, hip, yawRot);
        sg.IsHolstered = true;
        sg.SetBeltedNet(true);
        if (best.rb != null) { best.rb.isKinematic = true; best.rb.useGravity = false; }

        WorldSfx.Click(best.pos);
        try { hand.PlayHapticVibration(0.05f, 0.5f); } catch { }
        Debug.Log($"[VrToolBelt] {sg.name} → {(best.side < 0 ? "왼쪽" : "오른쪽")} 허리에 꽂음");
        return true;
    }

    private void Free(Slot s, bool taken)
    {
        if (s.item != null)
        {
            s.item.IsHolstered = false;
            s.item.SetBeltedNet(false);
            // 손에 꺼낸 게 아니면(소유권을 잃음 등) 손이 가까울 때 풀어 둔 물리를 다시 고정 — 중력 없이 떠다니지 않게
            if (!taken && s.rb != null && !s.rb.isKinematic)
            {
                s.rb.linearVelocity = Vector3.zero; s.rb.angularVelocity = Vector3.zero;
                s.rb.isKinematic = true;
            }
            if (taken) Debug.Log($"[VrToolBelt] {s.item.name} 허리에서 꺼냄");
        }
        s.item = null; s.rb = null; s.grab = null;
    }

    // ── 도구 판정 ─────────────────────────────

    private static bool IsTool(SyncGrab sg)
    {
        if (sg == null) return false;
        var t = sg.transform;
        return t.GetComponentInChildren<ImpactWrench>(true) != null
            || t.GetComponentInChildren<MultimeterMaster>(true) != null
            || t.GetComponentInChildren<GreaseTool>(true) != null;
    }

    private static Vector3 Center(Transform root)
    {
        bool any = false; Bounds b = default;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (r == null || !r.enabled || r is LineRenderer || r is ParticleSystemRenderer || r is SkinnedMeshRenderer) continue;
            if (r.GetComponentInParent<Canvas>() != null) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any)
        {
            var col = root.GetComponent<Collider>();
            return col != null ? col.bounds.center : root.position;
        }
        return b.center;
    }

    private void ScanHands()
    {
        _hands.RemoveAll(h => h == null);
        foreach (var hand in FindObjectsByType<Hand>(FindObjectsSortMode.None))
        {
            if (hand == null || _hands.Contains(hand)) continue;
            var nob = hand.GetComponentInParent<FishNet.Object.NetworkObject>();
            if (nob != null && nob.IsSpawned && !nob.IsOwner) continue;   // 다른 사람 손 제외
            _hands.Add(hand);
        }
    }

    // ── 처음 쓰는 사람 안내 ─────────────────────────────

    private void TrackHint(bool holdingTool)
    {
        float now = Time.unscaledTime;
        if (!holdingTool) { _toolHeldSince = -1f; return; }
        if (_toolHeldSince < 0f) _toolHeldSince = now;
        if (_everHolstered || _hintCount >= 3 || now < _nextHint || now - _toolHeldSince < 2f) return;
        _hintCount++; _nextHint = now + 60f;
        WarningPopup.Show("vrbelt", "허리 공구 걸이", "도구를 든 채 허리 양옆(고개를 숙이면 보이는 초록 원)에 놓으면 꽂혀요. 꺼낼 땐 손을 대고 그립", new Color(0.08f, 0.26f, 0.2f, 0.92f), 8f);
        MentorDialogue.Say("tool_belt", 120f);
    }

    private static TMP_FontAsset BeltFont()
    {
        if (s_font != null) return s_font;
        var tagPrefab = Resources.Load<GameObject>("ItemNameTagLabel");
        if (tagPrefab != null)
        {
            var t = tagPrefab.GetComponentInChildren<TMP_Text>(true);
            if (t != null) s_font = t.font;
        }
        return s_font;
    }

    /// <summary>슬롯 위의 원판·빛기둥·글자 — 허리를 보지 않아도 가슴 높이까지 솟은 빛기둥이 시야 아래쪽에 걸리게 한다.</summary>
    private void DrawGuide(Slot s, bool show, bool near, Quaternion yawRot)
    {
        if (!show)
        {
            if (s.disc != null && s.disc.activeSelf) s.disc.SetActive(false);
            if (s.pillar != null && s.pillar.gameObject.activeSelf) s.pillar.gameObject.SetActive(false);
            if (s.label != null && s.label.gameObject.activeSelf) s.label.gameObject.SetActive(false);
            return;
        }
        if (s.disc == null) MakeGuide(s);
        if (!s.disc.activeSelf) { s.disc.SetActive(true); s.pillar.gameObject.SetActive(true); s.label.gameObject.SetActive(true); }

        Color c = near ? ReadyColor : new Color(0.3f, 1f, 0.45f, 0.8f);
        float pulse = near ? 1f : 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
        s.disc.transform.SetPositionAndRotation(s.pos, Quaternion.Euler(90f, yawRot.eulerAngles.y, 0f));
        s.discMat.color = new Color(c.r, c.g, c.b, near ? 0.45f : Mathf.Lerp(0.15f, 0.32f, pulse));

        Vector3 top = s.pos + Vector3.up * 0.34f;
        s.pillar.SetPosition(0, s.pos);
        s.pillar.SetPosition(1, top);
        s.pillar.startColor = new Color(c.r, c.g, c.b, 0.55f);
        s.pillar.endColor = new Color(c.r, c.g, c.b, 0f);

        s.label.transform.position = top + Vector3.up * 0.035f;
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 look = s.label.transform.position - cam.transform.position;
            if (look.sqrMagnitude > 1e-6f) s.label.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
        }
        string t = near ? "<b>놓으세요!</b>" : "<b>공구 걸이</b>\n<size=70%>여기에 도구 놓기</size>";
        if (s.label.text != t) s.label.text = t;
        s.label.color = near ? ReadyColor : new Color(0.75f, 1f, 0.8f);
    }

    private void MakeGuide(Slot s)
    {
        var sh = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
        s.discMat = new Material(sh) { mainTexture = VrPalmMenu.DiscTex() };
        s.discMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
        s.discMat.renderQueue = 3990;

        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var col = q.GetComponent<Collider>(); if (col != null) Destroy(col);
        q.name = "[VrToolBeltDisc]"; q.layer = 1;
        q.transform.SetParent(transform, false);
        q.transform.localScale = new Vector3(0.15f, 0.15f, 1f);
        var mr = q.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        mr.sharedMaterial = s.discMat;
        s.disc = q;

        var pg = new GameObject("[VrToolBeltPillar]"); pg.layer = 1;
        pg.transform.SetParent(transform, false);
        var lr = pg.AddComponent<LineRenderer>();
        lr.useWorldSpace = true; lr.positionCount = 2;
        lr.widthMultiplier = 0.012f;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
        if (_mat == null) _mat = MakeRingMat();
        lr.sharedMaterial = _mat;
        s.pillar = lr;

        var tg = new GameObject("[VrToolBeltLabel]"); tg.layer = 1;
        tg.transform.SetParent(transform, false);
        var tmp = tg.AddComponent<TextMeshPro>();
        var f = BeltFont(); if (f != null) tmp.font = f;
        tmp.rectTransform.sizeDelta = new Vector2(0.3f, 0.09f);
        tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.02f; tmp.fontSizeMax = 0.06f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.richText = true;
        if (tmp.fontMaterial != null) tmp.fontMaterial.renderQueue = 4000;
        var tr = tg.GetComponent<MeshRenderer>(); if (tr != null) tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        s.label = tmp;
    }

    // ── 슬롯 표시 ─────────────────────────────

    private void DrawRing(Slot s, bool show, Quaternion yawRot)
    {
        if (!show)
        {
            if (s.ring != null && s.ring.gameObject.activeSelf) s.ring.gameObject.SetActive(false);
            DrawGuide(s, false, false, yawRot);
            return;
        }
        if (s.ring == null) s.ring = MakeRing();
        if (!s.ring.gameObject.activeSelf) s.ring.gameObject.SetActive(true);

        bool near = false;
        foreach (var h in _hands)
        {
            if (h == null || h.GetHeld() == null) continue;
            Vector3 c = Center(h.GetHeld().transform);
            if ((c - s.pos).sqrMagnitude < HolsterRadius * HolsterRadius) { near = true; break; }
        }
        float pulse = near ? 1f : 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
        var col = near ? ReadyColor : IdleColor;
        col.a *= near ? 1f : Mathf.Lerp(0.6f, 1f, pulse);
        s.ring.startColor = s.ring.endColor = col;
        s.ring.widthMultiplier = near ? 0.01f : 0.007f;
        DrawGuide(s, true, near, yawRot);

        // 허리 높이의 수평 원
        const float r = 0.07f;
        int n = s.ring.positionCount;
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.PI * 2f * i / n;
            s.ring.SetPosition(i, s.pos + yawRot * new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
        }
    }

    private static Material MakeRingMat()
    {
        var sh = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
        var m = new Material(sh);
        m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
        m.renderQueue = 4000;
        return m;
    }

    private LineRenderer MakeRing()
    {
        if (_mat == null) _mat = MakeRingMat();
        var go = new GameObject("[VrToolBeltSlot]");
        go.layer = 1;   // TransparentFX — VR 카메라가 그리는 레이어
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.positionCount = 32;
        lr.numCapVertices = 0;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sharedMaterial = _mat;
        return lr;
    }
}
