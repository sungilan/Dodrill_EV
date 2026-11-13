using System.Collections.Generic;
using Autohand;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
//  VrPalmMenu.cs
//
//  VR 전용 — 손바닥 메뉴. 빈손의 손바닥을 얼굴 쪽으로 펴면 손 위에 작은 메뉴가 뜨고,
//  반대 손 검지로 눌러 쓴다(2×2 + 아래 넓은 버튼 1개). 정면 UI 를 찾아 고개를 돌리거나 레이저로 겨눌 필요가 없다.
//  · 단계 목록   — 왼쪽 단계 목록 패널 열기/닫기 (VrTaskListToggle)
//  · 미니맵      — 미니맵 켜기/끄기
//  · 단계 넘김   — 정면 UI 의 '완료' 버튼과 같은 동작(누를 수 없는 단계면 회색)
//  · UI 정면으로 — 정면 UI 를 다시 시선 앞으로 (VRUIFollower 기본 따라오기로 복귀)
//  · 엑스레이   — 지금 다룰 부품을 차체 너머로 비춰 보기 (XRayMode, 평가 모드에서는 회색)
//  · 집중 모드   — 작업 대상 가까이에서 가리는 차체를 하얀 반투명으로 (FocusMode, 자동/끔)
//  · 측정 기록   — 측정 기록지 보이기/숨기기 (MeasurementLog)
//  · 버튼 안내   — 컨트롤러 버튼 설명을 항상 보이기/처음만 (VrControllerHints)
//  · 연습장     — 기본 동작 연습 튜토리얼 다시 하기/끝내기 (VrPracticeTutorial, 평가 모드에서는 회색)
//  누르면 손끝 진동 + 클릭음. 손바닥을 내리거나 물건을 잡으면 사라진다.
//  씬 배치 불필요 — 게임 시작 시 자동 생성되고, VR 기기가 아니면 아무것도 하지 않는다.
// ============================================================
public class VrPalmMenu : MonoBehaviour
{
    // 작업 중 손바닥이 잠깐 얼굴 쪽을 향할 때마다 메뉴가 떠서 오조작이 났다 → 조건을 엄격하게:
    // 손바닥을 얼굴 정면으로(약 37° 이내) 0.6초 동안 '가만히' 들고 있어야 열리고, 어느 손이든 물건을 들고 있으면 열지 않는다.
    private const float FacingDot = 0.8f;       // 손바닥이 얼굴을 향하는 정도(cos)
    private const float OpenDelay = 0.6f;
    private const float MaxPalmSpeed = 0.35f;   // 이보다 빨리 움직이는 손은 '보여 주는 손'이 아니다(m/s)
    private const float PressArmDelay = 0.4f;   // 열린 직후 이 시간 동안은 눌리지 않음
    private const float CloseDelay = 0.35f;
    private const float BtnW = 0.078f, BtnH = 0.036f, Gap = 0.008f;
    private const float PressDepth = 0.006f;    // 버튼 면을 이만큼 지나면 눌림
    private const float ReleaseDepth = 0.02f;   // 이만큼 떨어지면 다시 누를 수 있음

    private static readonly Color BgColor = new Color(0.08f, 0.1f, 0.13f, 0.88f);
    private static readonly Color HoverColor = new Color(0.16f, 0.36f, 0.62f, 0.95f);
    private static readonly Color PressColor = new Color(0.25f, 0.75f, 0.4f, 0.95f);
    private static readonly Color DisabledColor = new Color(0.2f, 0.2f, 0.2f, 0.6f);

    private class Btn
    {
        public string id;
        public Vector2 center;
        public float w = BtnW;
        public GameObject go;
        public Material mat;
        public TextMeshPro text;
        public bool enabled = true;
        public float flashUntil;
    }

    private readonly List<Hand> _hands = new List<Hand>();
    private float _nextHandScan;

    private GameObject _root;
    private readonly List<Btn> _btns = new List<Btn>();
    private static VrPalmMenu s_inst;
    /// <summary>지금 손바닥 메뉴가 떠 있는가(연습장 튜토리얼이 확인).</summary>
    public static bool IsOpen => s_inst != null && s_inst._menuHand != null && s_inst._root != null && s_inst._root.activeSelf;
    private Hand _menuHand;        // 메뉴가 붙은 손
    private float _facingSince = -1f, _notFacingSince = -1f;
    private Hand _candidate;
    private bool _pressLatched;
    private float _nextPress;
    private float _openedAt;
    private Vector3 _candLastPos;

    // 정면 UI 참조(필요할 때 찾음)
    private VRUIFollower _follower;
    private GameObject _miniMap;
    private Button _confirmBtn;
    private float _nextRefFind;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindAnyObjectByType<VrPalmMenu>() != null) return;
        var go = new GameObject("[VrPalmMenu]");
        DontDestroyOnLoad(go);
        go.AddComponent<VrPalmMenu>();
    }

    private void Awake() => s_inst = this;

    private void OnDestroy()
    {
        foreach (var b in _btns) if (b.mat != null) Destroy(b.mat);
        if (_root != null) Destroy(_root);
    }

    private void LateUpdate()
    {
        if (!VrProbeHandling.IsVRDevice) return;
        if (Time.unscaledTime >= _nextHandScan) { _nextHandScan = Time.unscaledTime + 2f; ScanHands(); }
        var cam = Camera.main;
        if (cam == null) { Close(); return; }

        // 어느 손이 손바닥을 얼굴 쪽으로 폈나
        Hand facing = null;
        bool anyHolding = false;
        foreach (var h in _hands)
            if (h != null && h.isActiveAndEnabled && (h.GetHeld() != null || h.holdingObj != null)) { anyHolding = true; break; }
        if (!anyHolding)
            foreach (var h in _hands)
                if (IsPalmFacing(h, cam.transform)) { facing = h; if (h == _menuHand) break; }

        float now = Time.unscaledTime;
        if (_menuHand == null)
        {
            if (facing != null)
            {
                Vector3 pp = facing.palmTransform.position;
                if (_candidate != facing) { _candidate = facing; _facingSince = now; }
                else
                {
                    float spd = Time.unscaledDeltaTime > 1e-4f ? (pp - _candLastPos).magnitude / Time.unscaledDeltaTime : 0f;
                    if (spd > MaxPalmSpeed) _facingSince = now;          // 움직이는 중이면 처음부터 다시
                    else if (now - _facingSince >= OpenDelay) Open(facing);
                }
                _candLastPos = pp;
            }
            else _candidate = null;
        }
        else if (anyHolding) Close();
        else
        {
            bool keep = facing == _menuHand || PokerNear();
            if (keep) _notFacingSince = -1f;
            else
            {
                if (_notFacingSince < 0f) _notFacingSince = now;
                if (now - _notFacingSince >= CloseDelay) Close();
            }
        }

        if (_menuHand == null) return;
        Place(cam.transform);
        RefreshStates();
        HandlePoke();
    }

    // ── 열고 닫기 ─────────────────────────────

    private bool IsPalmFacing(Hand h, Transform cam)
    {
        if (h == null || !h.isActiveAndEnabled || h.palmTransform == null || h.name.StartsWith("HighlightHand")) return false;
        if (h.GetHeld() != null || h.holdingObj != null) return false;
        Vector3 p = h.palmTransform.position;
        Vector3 toCam = cam.position - p;
        float dist = toCam.magnitude;
        if (dist < 0.2f || dist > 0.6f) return false;
        if (Vector3.Dot(cam.forward, -toCam / dist) < 0.5f) return false;   // 시야 안에 있을 때만
        return Vector3.Dot(h.palmTransform.forward, toCam / dist) > FacingDot;
    }

    private void Open(Hand h)
    {
        if (_root == null) Build();
        if (_root == null) return;
        _menuHand = h;
        _notFacingSince = -1f;
        _pressLatched = true;   // 열자마자 눌리지 않게(손끝이 이미 안쪽이면 한 번 빠져야 함)
        _openedAt = Time.unscaledTime;
        _root.SetActive(true);
        Haptic(h, 0.02f, 0.2f);
    }

    private void Close()
    {
        _menuHand = null;
        _candidate = null;
        if (_root != null && _root.activeSelf) _root.SetActive(false);
    }

    private void Place(Transform cam)
    {
        var palm = _menuHand.palmTransform;
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, (palm.position - cam.position).normalized).normalized;
        Vector3 pos = palm.position + palm.forward * 0.03f + up * 0.16f;   // 아래 넓은 버튼까지 손바닥 위에 오게(4줄 + 넓은 1줄)
        Vector3 look = pos - cam.position;
        if (look.sqrMagnitude < 1e-6f) look = cam.forward;
        _root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look, Vector3.up));
    }

    // ── 버튼 ─────────────────────────────

    private void Build()
    {
        TMP_FontAsset font = null;
        var tagPrefab = Resources.Load<GameObject>("ItemNameTagLabel");
        if (tagPrefab != null)
        {
            var t = tagPrefab.GetComponentInChildren<TMP_Text>(true);
            if (t != null) font = t.font;
        }

        _root = new GameObject("[VrPalmMenu_Panel]");
        _root.layer = 1;
        DontDestroyOnLoad(_root);

        float cx = (BtnW + Gap) * 0.5f, ry = BtnH + Gap;
        AddBtn("tasks", "단계 목록", new Vector2(-cx, ry * 1.5f), font);
        AddBtn("minimap", "미니맵", new Vector2(cx, ry * 1.5f), font);
        AddBtn("skip", "단계 넘김", new Vector2(-cx, ry * 0.5f), font);
        AddBtn("recenter", "UI 정면으로", new Vector2(cx, ry * 0.5f), font);
        AddBtn("xray", "엑스레이 켜기", new Vector2(-cx, -ry * 0.5f), font);
        AddBtn("focus", "집중 모드", new Vector2(cx, -ry * 0.5f), font);
        AddBtn("record", "측정 기록", new Vector2(-cx, -ry * 1.5f), font);
        AddBtn("hints", "버튼 안내", new Vector2(cx, -ry * 1.5f), font);
        AddBtn("practice", "연습장", new Vector2(0f, -ry * 2.5f), font, BtnW * 2f + Gap);
        _root.SetActive(false);
    }

    private void AddBtn(string id, string label, Vector2 center, TMP_FontAsset font, float width = BtnW)
    {
        var b = new Btn { id = id, center = center, w = width };
        var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var col = bg.GetComponent<Collider>();
        if (col != null) { col.enabled = false; Destroy(col); }
        bg.name = id;
        bg.layer = 1;
        bg.transform.SetParent(_root.transform, false);
        bg.transform.localPosition = new Vector3(center.x, center.y, 0f);
        bg.transform.localScale = new Vector3(width, BtnH, 1f);
        var mr = bg.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        b.mat = new Material(Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default"));
        b.mat.renderQueue = 3990;
        b.mat.color = BgColor;
        mr.sharedMaterial = b.mat;
        b.go = bg;

        var tgo = new GameObject(id + "_Text");
        tgo.layer = 1;
        tgo.transform.SetParent(_root.transform, false);
        tgo.transform.localPosition = new Vector3(center.x, center.y, -0.001f);
        var tmp = tgo.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.rectTransform.sizeDelta = new Vector2(Mathf.Min(width, BtnW) * 0.9f, BtnH * 0.8f);
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.01f;
        tmp.fontSizeMax = 0.2f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.text = label;
        var tr = tgo.GetComponent<MeshRenderer>();
        if (tr != null) { tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        if (tmp.fontMaterial != null) tmp.fontMaterial.renderQueue = 4000;
        b.text = tmp;
        _btns.Add(b);
    }

    private void RefreshStates()
    {
        FindRefs();
        float now = Time.unscaledTime;
        foreach (var b in _btns)
        {
            switch (b.id)
            {
                case "minimap":
                    b.enabled = _miniMap != null;
                    SetText(b, _miniMap != null && _miniMap.activeSelf ? "미니맵 끄기" : "미니맵 켜기");
                    break;
                case "skip":
                    b.enabled = _confirmBtn != null && _confirmBtn.gameObject.activeInHierarchy && _confirmBtn.interactable;
                    break;
                case "tasks":
                    b.enabled = FindAnyObjectByType<VrTaskListToggle>() != null;
                    break;
                case "recenter":
                    b.enabled = _follower != null;
                    break;
                case "xray":
                    b.enabled = XRayMode.Available;
                    SetText(b, XRayMode.Active ? "엑스레이 끄기" : "엑스레이 켜기");
                    break;
                case "focus":
                    SetText(b, FocusMode.Enabled ? "집중 모드 끄기" : "집중 모드 자동");
                    break;
                case "record":
                    SetText(b, MeasurementLog.Visible ? "측정 기록 닫기" : "측정 기록 보기");
                    break;
                case "hints":
                    SetText(b, VrControllerHints.AlwaysShow ? "버튼 안내 끄기" : "버튼 안내 켜기");
                    break;
                case "practice":
                    b.enabled = !EvaluationMode.ClientIsEvaluation;
                    SetText(b, VrPracticeTutorial.Running ? "연습장 끝내기" : "연습장 (기본 동작 연습)");
                    break;
            }
            if (now < b.flashUntil) continue;
            b.mat.color = b.enabled ? BgColor : DisabledColor;
            b.text.color = b.enabled ? Color.white : new Color(1f, 1f, 1f, 0.45f);
        }
    }

    private static void SetText(Btn b, string s) { if (b.text.text != s) b.text.text = s; }

    // ── 누르기 ─────────────────────────────

    private Transform PokerTip()
    {
        foreach (var h in _hands)
        {
            // 반대쪽 손만 누를 수 있다 — 같은 쪽 손(손 뼈와 손 투영 'HighlightHand')이 둘이라 메뉴 든 손 검지가 버튼을 계속 눌렀다
            if (h == null || _menuHand == null || h.left == _menuHand.left || !h.isActiveAndEnabled || h.fingers == null) continue;
            if (h.name.StartsWith("HighlightHand")) continue;
            foreach (var f in h.fingers)
                if (f != null && f.fingerType == FingerEnum.index && f.tip != null) return f.tip;
            if (h.fingers.Length > 1 && h.fingers[1] != null && h.fingers[1].tip != null) return h.fingers[1].tip;
        }
        return null;
    }

    private Hand PokerHand()
    {
        foreach (var h in _hands) if (h != null && _menuHand != null && h.left != _menuHand.left && h.isActiveAndEnabled && !h.name.StartsWith("HighlightHand")) return h;
        return null;
    }

    private bool PokerNear()
    {
        if (_root == null || !_root.activeSelf) return false;
        var tip = PokerTip();
        return tip != null && Vector3.Distance(tip.position, _root.transform.position) < 0.12f;
    }

    private void HandlePoke()
    {
        var tip = PokerTip();
        if (tip == null) return;
        Vector3 lp = _root.transform.InverseTransformPoint(tip.position);   // 버튼 면 z=0, 보는 쪽이 -z

        Btn over = null;
        foreach (var b in _btns)
            if (Mathf.Abs(lp.x - b.center.x) <= b.w * 0.5f && Mathf.Abs(lp.y - b.center.y) <= BtnH * 0.5f) { over = b; break; }

        float now = Time.unscaledTime;
        if (lp.z < -ReleaseDepth || over == null) _pressLatched = false;

        foreach (var b in _btns)
            if (now >= b.flashUntil && b.enabled) b.mat.color = b == over && lp.z > -0.03f ? HoverColor : BgColor;

        if (over == null || _pressLatched || now < _nextPress || now - _openedAt < PressArmDelay) return;
        if (lp.z < -PressDepth || lp.z > 0.03f) return;

        _pressLatched = true;
        _nextPress = now + 0.35f;
        var hand = PokerHand();
        if (!over.enabled) { Haptic(hand, 0.02f, 0.15f); WorldSfx.PlayAt("Beep_Error", tip.position, 0.35f); return; }

        over.mat.color = PressColor;
        over.flashUntil = now + 0.15f;
        Haptic(hand, 0.03f, 0.5f);
        WorldSfx.PlayAt("UI_Click", tip.position, 0.6f);
        Run(over.id);
    }

    private void Run(string id)
    {
        switch (id)
        {
            case "tasks":
                FindAnyObjectByType<VrTaskListToggle>()?.Toggle();
                break;
            case "minimap":
                if (_miniMap != null) _miniMap.SetActive(!_miniMap.activeSelf);
                break;
            case "skip":
                if (_confirmBtn != null && _confirmBtn.interactable) _confirmBtn.onClick.Invoke();
                break;
            case "recenter":
                if (_follower != null) _follower.ResetToDefaultFollow();
                break;
            case "xray":
                XRayMode.Toggle();
                break;
            case "focus":
                FocusMode.Toggle();
                break;
            case "record":
                MeasurementLog.Toggle();
                break;
            case "hints":
                VrControllerHints.AlwaysShow = !VrControllerHints.AlwaysShow;
                WarningPopup.Show("vrhints", VrControllerHints.AlwaysShow ? "버튼 안내 항상 표시" : "버튼 안내 끔",
                    VrControllerHints.AlwaysShow ? "도구를 잡으면 손 옆에 버튼 설명이 나옵니다" : "", new Color(0.2f, 0.24f, 0.3f, 0.92f), 1.6f);
                break;
            case "practice":
                if (VrPracticeTutorial.Running) VrPracticeTutorial.Stop();
                else { Close(); VrPracticeTutorial.Restart(); }
                break;
        }
    }

    // ── 정면 UI 찾기 ─────────────────────────────

    private void FindRefs()
    {
        if (_follower != null && _confirmBtn != null && _miniMap != null) return;
        if (Time.unscaledTime < _nextRefFind) return;
        _nextRefFind = Time.unscaledTime + 1.5f;

        if (_follower == null) _follower = FindAnyObjectByType<VRUIFollower>();
        if (_follower == null) return;
        foreach (var t in _follower.GetComponentsInChildren<Transform>(true))
        {
            if (_miniMap == null && t.name == "CanvasMiniMap_PC_01") _miniMap = t.gameObject;
            if (_confirmBtn == null && t.name == "ConfirmButton_01") _confirmBtn = t.GetComponent<Button>();
        }
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

    private static void Haptic(Hand h, float duration, float amp)
    {
        if (h == null) return;
        try { h.PlayHapticVibration(duration, amp); } catch { }
    }
}
