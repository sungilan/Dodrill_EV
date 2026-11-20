using System.Collections.Generic;
using Autohand;
using TMPro;
using UnityEngine;

// ============================================================
//  VrControllerHints.cs  (2026-11-13)
//
//  VR 전용 — 컨트롤러 버튼 설명. 도구를 잡거나 레이저로 물건을 겨누면 손 옆에
//  "트리거: 렌치 돌리기 / 그립: 놓기" 처럼 버튼 위치(트리거 = 검지, 그립 = 가운데 손가락)에 선을 이어 띄운다.
//  · 같은 상황(도구 종류)마다 처음 3번만 보여 주고(각 8초), 이후에는 손바닥 메뉴 '버튼 안내 켜기'로 항상 보이게 할 수 있다.
//  · 조작 기준: AutoHand 입력 — 그립 = 잡기/유지, 트리거 = 쓰기(Squeeze). 레이저: 그립으로 겨누고 트리거로 당겨 잡기.
//  씬 배치 불필요 — 게임 시작 시 자동 생성, VR 기기가 아니면 아무것도 하지 않는다.
// ============================================================
[DefaultExecutionOrder(250)]
public class VrControllerHints : MonoBehaviour
{
    private const int ShowTimes = 3;
    private const float ShowSeconds = 8f;
    private const string PrefAlways = "vrhint_always";
    private const string PrefSeen = "vrhint_seen_";

    /// <summary>항상 보이기(손바닥 메뉴에서 켬). 기기에 저장된다.</summary>
    public static bool AlwaysShow
    {
        get => PlayerPrefs.GetInt(PrefAlways, 0) == 1;
        set { PlayerPrefs.SetInt(PrefAlways, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    private struct Ctx
    {
        public string key;
        public string trigger;   // 검지(트리거) 쪽 설명, null = 표시 안 함
        public string grip;      // 가운데 손가락(그립) 쪽 설명
    }

    private class HandView
    {
        public Hand hand;
        public string curKey;
        public float showUntil;
        public GameObject root;
        public TextMeshPro trigText, gripText;
        public LineRenderer trigLine, gripLine;
        public Transform trigDot, gripDot;
    }

    private readonly List<HandView> _views = new List<HandView>();
    private readonly HashSet<Hand> _laserTarget = new HashSet<Hand>();
    private readonly List<HandDistanceGrabber> _grabbers = new List<HandDistanceGrabber>();
    private float _nextScan;
    private Material _lineMat;
    private TMP_FontAsset _font;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindAnyObjectByType<VrControllerHints>() != null) return;
        var go = new GameObject("[VrControllerHints]");
        DontDestroyOnLoad(go);
        go.AddComponent<VrControllerHints>();
    }

    private void OnDestroy()
    {
        foreach (var g in _grabbers) if (g != null) { g.StartTarget.RemoveListener(OnStartTarget); g.StopTarget.RemoveListener(OnStopTarget); }
        foreach (var v in _views) if (v.root != null) Destroy(v.root);
        if (_lineMat != null) Destroy(_lineMat);
    }

    private void OnStartTarget(Hand h, Grabbable g) { if (h != null) _laserTarget.Add(h); }
    private void OnStopTarget(Hand h, Grabbable g) { if (h != null) _laserTarget.Remove(h); }

    private void LateUpdate()
    {
        if (!VrProbeHandling.IsVRDevice) return;
        if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 2f; Scan(); }
        var cam = Camera.main;
        if (cam == null) return;

        foreach (var v in _views)
        {
            if (v.hand == null || !v.hand.isActiveAndEnabled) { Hide(v); continue; }
            var ctx = Context(v.hand);
            if (ctx.key != v.curKey)
            {
                v.curKey = ctx.key;
                v.showUntil = 0f;
                if (ctx.key != null && (AlwaysShow || Seen(ctx.key) < ShowTimes))
                {
                    if (!AlwaysShow) MarkSeen(ctx.key);
                    v.showUntil = Time.unscaledTime + ShowSeconds;
                }
            }
            bool show = ctx.key != null && (AlwaysShow || Time.unscaledTime < v.showUntil);
            if (!show) { Hide(v); continue; }
            Draw(v, ctx, cam.transform);
        }
    }

    // ── 상황 판단 ─────────────────────────────

    private Ctx Context(Hand h)
    {
        var held = h.GetHeld();
        if (held == null && h.holdingObj != null) held = h.holdingObj;
        if (held != null)
        {
            Transform t = held.transform;
            var sg = held.GetComponentInParent<SyncGrab>();
            if (sg != null) t = sg.transform;
            const string Drop = "놓기 (허리 옆에서 놓으면 벨트에 꽂힘)";
            if (t.GetComponentInChildren<AirImpactWrench>(true) != null) return new Ctx { key = "airimpact", trigger = "임팩 돌리기 (소켓·에어 연결 필요)", grip = "두 손으로 잡기" };
            if (t.GetComponentInChildren<ImpactWrench>(true) != null) return new Ctx { key = "wrench", trigger = "렌치 돌리기 (볼트에 대고 누르기)", grip = Drop };
            var mt = t.GetComponentInChildren<ManualTool>(true);
            if (mt != null) return mt.kind == ManualTool.Kind.Spanner
                ? new Ctx { key = "spanner", trigger = "볼트에 끼우고 손목을 돌리기", grip = "놓기" }
                : new Ctx { key = "driver", trigger = "나사에 대고 돌리기", grip = "놓기" };
            if (t.GetComponentInChildren<MultimeterMaster>(true) != null) return new Ctx { key = "meter", trigger = "빈 손 그립으로 본체를 쥐면 프로브 꺼내기", grip = Drop };
            if (t.GetComponentInChildren<SprayTool>(true) != null) return new Ctx { key = "spray", trigger = "누르고 있는 동안 분사", grip = "놓기" };
            if (t.GetComponentInChildren<GreaseTool>(true) != null) return new Ctx { key = "grease", trigger = "누르고 있는 동안 도포", grip = Drop };
            if (t.GetComponentInChildren<WorkLight>(true) != null) return new Ctx { key = "light", trigger = "켜기 / 끄기", grip = "차체에 대고 놓으면 붙음" };
            if (t.GetComponentInChildren<PourContainer>(true) != null) return new Ctx { key = "pour", trigger = null, grip = "기울이면 붓기 · 놓기" };
            if (t.GetComponentInChildren<FloorDragProp>(true) != null) return new Ctx { key = "drag", trigger = null, grip = "누른 채 끌기 · 손을 멀리 떼면 놓임" };
            if (t.GetComponentInChildren<PracticeTutorialItem>(true) != null) return new Ctx { key = "practice", trigger = null, grip = "누르고 있는 동안 잡기 · 떼면 놓기" };
            return new Ctx { key = "generic", trigger = null, grip = "누르고 있는 동안 잡기 · 떼면 놓기" };
        }
        if (_laserTarget.Contains(h))
            return new Ctx { key = "laser", trigger = "당겨서 잡기", grip = "누른 채 겨누기 (레이저)" };
        return default;
    }

    private static int Seen(string key) => PlayerPrefs.GetInt(PrefSeen + key, 0);
    private static void MarkSeen(string key) { PlayerPrefs.SetInt(PrefSeen + key, Seen(key) + 1); PlayerPrefs.Save(); }

    /// <summary>처음 보기 횟수를 지운다(연습장 다시 하기 등).</summary>
    public static void ResetSeen()
    {
        foreach (var k in new[] { "airimpact", "wrench", "spanner", "driver", "meter", "spray", "grease", "light", "pour", "drag", "practice", "generic", "laser" })
            PlayerPrefs.DeleteKey(PrefSeen + k);
        PlayerPrefs.Save();
    }

    // ── 그리기 ─────────────────────────────

    private void Draw(HandView v, Ctx ctx, Transform cam)
    {
        if (v.root == null) Build(v);
        if (!v.root.activeSelf) v.root.SetActive(true);

        Vector3 trigAnchor = FingerBase(v.hand, FingerEnum.index);
        Vector3 gripAnchor = FingerBase(v.hand, FingerEnum.middle);
        if (gripAnchor == trigAnchor) gripAnchor = (v.hand.palmTransform != null ? v.hand.palmTransform.position : v.hand.transform.position);

        // 손 바깥쪽(왼손은 왼쪽, 오른손은 오른쪽) 위로 글자를 띄운다
        Vector3 toCam = (cam.position - v.hand.transform.position).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, -toCam).normalized * (v.hand.left ? -1f : 1f);
        Vector3 basePos = v.hand.transform.position + side * 0.11f + toCam * 0.03f;
        Quaternion face = Quaternion.LookRotation(basePos - cam.position, Vector3.up);

        Place(v.trigText, v.trigLine, v.trigDot, ctx.trigger, "트리거", basePos + Vector3.up * 0.045f, trigAnchor, face, side, new Color(1f, 0.82f, 0.35f));
        Place(v.gripText, v.gripLine, v.gripDot, ctx.grip, "그립", basePos - Vector3.up * 0.01f, gripAnchor, face, side, new Color(0.45f, 0.85f, 1f));
    }

    private static void Place(TextMeshPro text, LineRenderer line, Transform dot, string desc, string button, Vector3 pos, Vector3 anchor, Quaternion face, Vector3 side, Color c)
    {
        bool on = !string.IsNullOrEmpty(desc);
        if (text.gameObject.activeSelf != on) { text.gameObject.SetActive(on); line.gameObject.SetActive(on); dot.gameObject.SetActive(on); }
        if (!on) return;
        string s = $"<color=#{ColorUtility.ToHtmlStringRGB(c)}><b>{button}</b></color>  {desc}";
        if (text.text != s) text.text = s;
        // 글자 왼쪽(손 쪽) 끝이 pos 에 오게
        text.alignment = side.x * (face * Vector3.right).x + side.z * (face * Vector3.right).z >= 0f ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;
        text.rectTransform.pivot = text.alignment == TextAlignmentOptions.MidlineLeft ? new Vector2(0f, 0.5f) : new Vector2(1f, 0.5f);
        text.transform.SetPositionAndRotation(pos, face);
        line.SetPosition(0, anchor);
        line.SetPosition(1, pos - side * 0.005f);
        line.startColor = line.endColor = new Color(c.r, c.g, c.b, 0.85f);
        dot.position = anchor;
        dot.rotation = face;
    }

    private static Vector3 FingerBase(Hand h, FingerEnum type)
    {
        if (h.fingers != null)
            foreach (var f in h.fingers)
                if (f != null && f.fingerType == type)
                    return f.tip != null ? Vector3.Lerp(f.transform.position, f.tip.position, 0.35f) : f.transform.position;
        return h.palmTransform != null ? h.palmTransform.position : h.transform.position;
    }

    private static void Hide(HandView v) { if (v.root != null && v.root.activeSelf) v.root.SetActive(false); }

    private void Build(HandView v)
    {
        if (_font == null)
        {
            var tagPrefab = Resources.Load<GameObject>("ItemNameTagLabel");
            var t = tagPrefab != null ? tagPrefab.GetComponentInChildren<TMP_Text>(true) : null;
            if (t != null) _font = t.font;
        }
        if (_lineMat == null)
        {
            _lineMat = new Material(Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default"));
            _lineMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            _lineMat.renderQueue = 4000;
        }
        v.root = new GameObject("[VrHint]");
        v.root.layer = 1;
        v.root.transform.SetParent(transform, false);
        v.trigText = MakeText(v.root.transform, "Trigger");
        v.gripText = MakeText(v.root.transform, "Grip");
        v.trigLine = MakeLine(v.root.transform);
        v.gripLine = MakeLine(v.root.transform);
        v.trigDot = MakeDot(v.root.transform, new Color(1f, 0.82f, 0.35f));
        v.gripDot = MakeDot(v.root.transform, new Color(0.45f, 0.85f, 1f));
    }

    private TextMeshPro MakeText(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.layer = 1;
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshPro>();
        if (_font != null) t.font = _font;
        t.fontSize = 0.18f;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.rectTransform.sizeDelta = new Vector2(0.3f, 0.03f);
        t.color = Color.white;
        t.outlineWidth = 0.2f;
        t.outlineColor = new Color32(0, 0, 0, 220);
        if (t.fontMaterial != null) { t.fontMaterial.renderQueue = 4001; }
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return t;
    }

    private LineRenderer MakeLine(Transform parent)
    {
        var go = new GameObject("Line");
        go.layer = 1;
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.widthMultiplier = 0.0015f;
        lr.sharedMaterial = _lineMat;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    private Transform MakeDot(Transform parent, Color c)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var col = go.GetComponent<Collider>();
        if (col != null) { col.enabled = false; Destroy(col); }
        go.name = "Dot";
        go.layer = 1;
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * 0.008f;
        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var m = new Material(_lineMat) { color = c };
        mr.sharedMaterial = m;
        return go.transform;
    }

    private void Scan()
    {
        _views.RemoveAll(v => { if (v.hand == null) { if (v.root != null) Destroy(v.root); return true; } return false; });
        foreach (var hand in FindObjectsByType<Hand>(FindObjectsSortMode.None))
        {
            if (hand == null || hand.name.StartsWith("HighlightHand")) continue;
            var nob = hand.GetComponentInParent<FishNet.Object.NetworkObject>();
            if (nob != null && nob.IsSpawned && !nob.IsOwner) continue;
            bool known = false;
            foreach (var v in _views) if (v.hand == hand) { known = true; break; }
            if (!known) _views.Add(new HandView { hand = hand });
        }
        _grabbers.RemoveAll(g => g == null);
        foreach (var g in FindObjectsByType<HandDistanceGrabber>(FindObjectsSortMode.None))
        {
            if (g == null || _grabbers.Contains(g)) continue;
            var nob = g.GetComponentInParent<FishNet.Object.NetworkObject>();
            if (nob != null && nob.IsSpawned && !nob.IsOwner) continue;
            g.StartTarget.AddListener(OnStartTarget);
            g.StopTarget.AddListener(OnStopTarget);
            _grabbers.Add(g);
        }
    }
}
