using System.Collections.Generic;
using Autohand;
using TMPro;
using UnityEngine;

// ============================================================
//  VrPracticeTutorial.cs  (2026-11-13)
//
//  VR 전용 — 연습장 튜토리얼(첫 실행, 3~5분). 시나리오 전에 작업대 하나로 기본 동작을 하나씩 해 본다.
//    ① 잡기 → ② 레이저로 당기기 → ③ 벨트에 꽂기 → ④ 손바닥 메뉴 → ⑤ 연습 볼트 풀고 조이기 → ⑥ 프로브로 단자 재기
//  하나씩 성공해야 다음으로 넘어간다. 책상 앞 오른쪽 모서리의 '건너뛰기'를 검지로 누르면 언제든 끝낼 수 있다.
//
//  · 첫 실행(이 기기에서 완료·건너뛰기 기록이 없을 때) 훈련 모드에서 자동 시작. 평가 모드에서는 시작하지 않는다.
//    손바닥 메뉴 '연습장'으로 언제든 다시 할 수 있다.
//  · 작업대와 연습 물건(블록·스패너·볼트·멀티미터)은 내 화면에만 만든다(네트워크·서버·시나리오 무관).
//    시나리오 진행·다른 사람에게 영향이 없다. 끝나면 모두 지운다.
//  · 작업대는 내 앞쪽 빈 바닥을 찾아 놓는다(차·벽과 겹치지 않는 방향).
// ============================================================
public class VrPracticeTutorial : MonoBehaviour
{
    private const string PrefDone = "practice_tutorial_done";
    private const float TableH = 0.82f, TableW = 0.9f, TableD = 0.5f;
    private const float BoltDegrees = 540f;   // 풀기·조이기 각각(손목 1.5바퀴)
    private const float ProbeDwell = 0.5f;

    private enum Step { Grab, Laser, Belt, PalmMenu, BoltLoosen, BoltTighten, Probe, Done }

    private static readonly string[] Titles =
    {
        "물건 잡기", "레이저로 당기기", "벨트에 꽂기", "손바닥 메뉴", "볼트 풀기", "볼트 조이기", "프로브로 단자 재기", "완료"
    };

    private static readonly string[] Bodies =
    {
        "책상 위 <color=#6fc3ff>파란 블록</color>에 손을 대고 <b>그립</b>(가운데 손가락 버튼)을 누르고 있으세요.\n버튼을 떼면 놓습니다.",
        "블록을 멀리 옮겨 두었습니다.\n<b>오른손 그립</b>을 누른 채 블록을 겨누면 레이저가 나옵니다. 그 상태로 <b>트리거</b>(검지)를 당겨 끌어오세요.",
        "책상 위 <color=#ffd27a>스패너</color>를 잡고 허리 옆 <color=#5be37d>초록 원</color> 가까이에서 놓으세요.\n작업 중 도구를 잃어버리지 않게 꽂아 두는 곳입니다.",
        "양손을 비우고, 한 손 <b>손바닥을 얼굴 쪽으로 펴서</b> 1초쯤 가만히 들고 있으세요.\n메뉴가 뜨면 반대 손 검지로 버튼을 누릅니다(지금은 보기만 하면 됩니다).",
        "허리의 스패너를 꺼내 <color=#ffd27a>연습 볼트</color> 머리에 끝을 대고, 손목을 돌려 푸세요.\n라쳇 '딱' 소리가 나면 돌아가는 중입니다.",
        "잘했습니다. 이제 같은 방법으로 볼트를 <b>다시 조이세요</b>.",
        "책상 위 멀티미터의 <color=#ff6a5a>빨간 프로브</color>를 잡아, <color=#ffd27a>노란 연습 단자</color>에 끝을 대고 잠깐 유지하세요.\n값이 측정되면 성공입니다.",
        "연습 완료! 실제 작업도 같은 방법으로 하면 됩니다.\n손바닥 메뉴 '연습장'에서 언제든 다시 할 수 있습니다.",
    };

    public static bool Running => s_inst != null && s_inst._running;
    private static VrPracticeTutorial s_inst;

    private bool _running;
    private Step _step;
    private float _stepStarted;
    private float _doneAt;
    private bool _autoChecked;
    private float _bootTime;

    // 작업대
    private GameObject _station;
    private Vector3 _tablePos;
    private Quaternion _tableRot;
    private int _grabLayer;
    private TMP_FontAsset _font;
    private readonly List<Material> _mats = new List<Material>();

    // 안내판
    private TextMeshPro _boardText;
    private Transform _skipBtn;
    private Material _skipMat;
    private bool _skipLatched;
    private TextMeshPro _arrow;

    // 물건
    private Grabbable _block;
    private Vector3 _blockRestPos;
    private bool _pulled;
    private Grabbable _spanner;
    private Vector3 _spannerAxisLocal, _spannerEndA, _spannerEndB;
    private bool _spannerHolstered;
    private int _holsterSlot;
    private Quaternion _holsterRelRot;
    private Transform _bolt;
    private float _boltProgress;
    private Vector3 _boltRestLocal;
    private Vector3 _prevSpannerDir;
    private bool _hasPrevDir;
    private float _ratchetAcc;
    private Renderer _boltRenderer;
    private Grabbable _probe;
    private Transform _terminal;
    private TextMeshPro _lcd;
    private LineRenderer _probeCable;
    private Transform _meterJack;
    private float _probeDwell;
    private float _palmOpenFor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (s_inst != null || FindAnyObjectByType<VrPracticeTutorial>() != null) return;
        var go = new GameObject("[VrPracticeTutorial]");
        DontDestroyOnLoad(go);
        s_inst = go.AddComponent<VrPracticeTutorial>();
    }

    private void Awake() { s_inst = this; _bootTime = Time.unscaledTime; }

    private void OnDestroy()
    {
        Cleanup();
        if (s_inst == this) s_inst = null;
    }

    /// <summary>손바닥 메뉴 '연습장' — 처음부터 다시.</summary>
    public static void Restart()
    {
        if (s_inst == null) return;
        if (EvaluationMode.ClientIsEvaluation)
        {
            WarningPopup.Show("practice", "연습장", "평가 모드에서는 쓸 수 없습니다", new Color(0.25f, 0.25f, 0.28f, 0.92f), 1.6f);
            return;
        }
        s_inst.Begin();
    }

    /// <summary>연습 중이면 끝낸다.</summary>
    public static void Stop() { if (s_inst != null && s_inst._running) s_inst.Finish(true); }

    // ── 시작·끝 ─────────────────────────────

    private void Update()
    {
        if (!VrProbeHandling.IsVRDevice) return;

        if (!_autoChecked && !_running)
        {
            // 게임 씬에 내 VR 손이 준비되고 몇 초 뒤, 첫 실행이면 자동 시작
            if (Time.unscaledTime - _bootTime < 4f) return;
            if (ScenarioStateReceiver.Instance == null || Camera.main == null || LocalHands.All.Count < 2) return;
            _autoChecked = true;
            if (PlayerPrefs.GetInt(PrefDone, 0) == 0 && !EvaluationMode.ClientIsEvaluation) Begin();
        }
        if (!_running) return;
        if (EvaluationMode.ClientIsEvaluation) { Finish(true); return; }

        Tick();
    }

    private void Begin()
    {
        Cleanup();
        var cam = Camera.main;
        if (cam == null) return;
        if (!BuildStation(cam.transform)) { Debug.LogWarning("[Practice] 작업대 놓을 자리를 못 찾음"); return; }
        _running = true;
        VrControllerHints.ResetSeen();   // 연습하는 동안 버튼 안내가 다시 보이게
        SetStep(Step.Grab);
        Debug.Log("[Practice] 연습장 시작");
    }

    private void Finish(bool skipped)
    {
        PlayerPrefs.SetInt(PrefDone, 1);
        PlayerPrefs.Save();
        Debug.Log($"[Practice] 연습장 {(skipped ? "건너뜀" : "완료")} ({Time.unscaledTime - _stepStarted:F0}s)");
        Cleanup();
        if (skipped) WarningPopup.Show("practice", "연습장 종료", "손바닥 메뉴 '연습장'에서 다시 할 수 있습니다", new Color(0.2f, 0.24f, 0.3f, 0.92f), 2f);
    }

    private void Cleanup()
    {
        _running = false;
        // 손에 든 연습 물건은 먼저 놓게 한다(손이 지워진 물건을 쥔 채 남지 않게)
        foreach (var h in LocalHands.All)
            if (h != null && h.holdingObj != null && h.holdingObj.GetComponent<PracticeTutorialItem>() != null) h.ForceReleaseGrab();
        if (_station != null) Destroy(_station);
        _station = null;
        foreach (var m in _mats) if (m != null) Destroy(m);
        _mats.Clear();
        _block = _spanner = _probe = null;
        _bolt = _terminal = _skipBtn = _meterJack = null;
        _spannerHolstered = false;
    }

    private void SetStep(Step s)
    {
        _step = s;
        _stepStarted = Time.unscaledTime;
        _hasPrevDir = false;
        _probeDwell = 0f;
        _palmOpenFor = 0f;
        if (s == Step.Laser) MoveBlockFar();
        if (s == Step.Done) _doneAt = Time.unscaledTime;
        RefreshBoard();
        if (s != Step.Grab)
        {
            var cam = Camera.main;
            if (cam != null) WorldSfx.PlayAt("UI_Click", cam.transform.position + cam.transform.forward * 0.5f, 0.8f);
            foreach (var h in LocalHands.All) Haptic(h, 0.08f, 0.6f);
        }
    }

    // ── 매 프레임 ─────────────────────────────

    private void Tick()
    {
        if (_station == null) { _running = false; return; }
        HandleSkipPoke();
        UpdateHolster();
        UpdateCable();
        UpdateArrow();

        switch (_step)
        {
            case Step.Grab:
                if (_block != null && _block.IsHeld()) SetStep(Step.Laser);
                break;

            case Step.Laser:
                if (_block == null) break;
                if (!_block.IsHeld()) _blockRestPos = _block.transform.position;
                else
                {
                    // 레이저로 당겼거나(OnPull), 손이 닿지 않는 거리에서 손으로 들어왔다
                    bool far = false;
                    foreach (var h in _block.GetHeldBy()) if (h != null && (h.transform.position - _blockRestPos).sqrMagnitude > 0.9f * 0.9f) far = true;
                    if (_pulled || far) SetStep(Step.Belt);
                    else if (Time.unscaledTime - _stepStarted > 2f)
                    {
                        // 걸어가서 직접 집었다 → 다시 멀리 두고 레이저로 해 보게
                        foreach (var h in LocalHands.All) if (h != null && h.holdingObj == _block) h.ForceReleaseGrab();
                        MoveBlockFar();
                        WarningPopup.Show("practice_laser", "레이저로 당겨 보세요", "손이 닿지 않는 곳에서 그립으로 겨누고 트리거를 당깁니다", new Color(0.2f, 0.3f, 0.45f, 0.92f), 2.5f);
                    }
                }
                break;

            case Step.Belt:
                if (_spannerHolstered) SetStep(Step.PalmMenu);
                break;

            case Step.PalmMenu:
                _palmOpenFor = VrPalmMenu.IsOpen ? _palmOpenFor + Time.unscaledDeltaTime : 0f;
                if (_palmOpenFor > 1.2f) SetStep(Step.BoltLoosen);
                break;

            case Step.BoltLoosen:
            case Step.BoltTighten:
                TurnBolt();
                break;

            case Step.Probe:
                CheckProbe();
                break;

            case Step.Done:
                if (Time.unscaledTime - _doneAt > 7f) Finish(false);
                break;
        }
    }

    // ── 작업대 만들기 ─────────────────────────────

    private bool BuildStation(Transform cam)
    {
        _grabLayer = LayerMask.NameToLayer(Hand.grabbableLayerNameDefault);
        if (_grabLayer < 0) _grabLayer = 0;
        if (_font == null)
        {
            var tagPrefab = Resources.Load<GameObject>("ItemNameTagLabel");
            var t = tagPrefab != null ? tagPrefab.GetComponentInChildren<TMP_Text>(true) : null;
            if (t != null) _font = t.font;
        }

        // 내 앞쪽부터 돌아가며 빈 바닥 찾기(내 몸·손 콜라이더는 무시)
        int mask = ~(1 << _grabLayer);
        foreach (var ln in new[] { "Hand", "HandHolding", "HandReleasing", "HandPlayer", "Player" })
        { int l = LayerMask.NameToLayer(ln); if (l >= 0) mask &= ~(1 << l); }
        Vector3 f = cam.forward; f.y = 0f;
        if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
        f.Normalize();
        float[] yaws = { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 150f, -150f, 180f };
        bool found = false;
        foreach (float y in yaws)
        {
            Vector3 dir = Quaternion.Euler(0f, y, 0f) * f;
            Vector3 p = cam.position + dir * 1.05f;
            if (!Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out var hit, 3f, mask, QueryTriggerInteraction.Ignore)) continue;
            if (hit.point.y > cam.position.y - 1.0f) continue;   // 바닥이 아니라 무언가 위(차·책상)
            Quaternion rot = Quaternion.LookRotation(-dir, Vector3.up);   // 책상 앞면이 나를 보게
            Vector3 center = hit.point + Vector3.up * (TableH * 0.5f + 0.02f);
            if (Physics.CheckBox(center, new Vector3(TableW * 0.5f + 0.1f, TableH * 0.5f, TableD * 0.5f + 0.1f), rot, mask, QueryTriggerInteraction.Ignore))
                continue;
            if (Physics.Raycast(cam.position, dir, 1.4f, mask, QueryTriggerInteraction.Ignore)) continue;   // 가는 길이 막힘
            _tablePos = hit.point;
            _tableRot = rot;
            found = true;
            break;
        }
        if (!found) return false;

        _station = new GameObject("[PracticeStation]");
        _station.transform.SetPositionAndRotation(_tablePos, _tableRot);

        // 책상(상판 + 다리 대신 몸통)
        var table = Box("Table", _station.transform, new Vector3(0f, TableH * 0.5f, 0f), new Vector3(TableW, TableH, TableD), new Color(0.32f, 0.34f, 0.38f), true);
        table.layer = 0;
        Vector3 top = new Vector3(0f, TableH, 0f);

        // 연습 블록
        _block = MakeGrabbable("PracticeBlock", _station.transform.TransformPoint(top + new Vector3(-0.3f, 0.05f, 0.05f)), _tableRot,
            new Vector3(0.08f, 0.08f, 0.08f), new Color(0.25f, 0.6f, 1f), 0.4f);
        var dg = _block.gameObject.AddComponent<DistanceGrabbable>();
        dg.instantPull = true;
        dg.ignoreHighlights = true;
        if (dg.OnPull == null) dg.OnPull = new UnityHandGrabEvent();
        dg.OnPull.AddListener((h, g) => _pulled = true);
        if (dg.StartTargeting == null) dg.StartTargeting = new UnityHandGrabEvent();
        if (dg.StopTargeting == null) dg.StopTargeting = new UnityHandGrabEvent();
        if (dg.StartSelecting == null) dg.StartSelecting = new UnityHandGrabEvent();
        AllowLaserOn(_grabLayer);

        // 연습 스패너(맵의 실제 스패너 모양을 빌려 온다, 없으면 막대)
        _spanner = MakeSpanner(_station.transform.TransformPoint(top + new Vector3(0f, 0.03f, 0.1f)), _tableRot * Quaternion.Euler(0f, 90f, 0f));
        _spanner.onGrab.AddListener((h, g) => { if (_spannerHolstered) { _spannerHolstered = false; Unlock(_spanner, true); } });
        _spanner.onRelease.AddListener((h, g) => TryHolsterSpanner());

        // 연습 볼트(받침판 위)
        Box("BoltPlate", _station.transform, top + new Vector3(0.12f, 0.01f, -0.08f), new Vector3(0.16f, 0.02f, 0.16f), new Color(0.5f, 0.52f, 0.55f), true);
        var bolt = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bolt.name = "PracticeBolt";
        bolt.transform.SetParent(_station.transform, false);
        bolt.transform.localPosition = top + new Vector3(0.12f, 0.035f, -0.08f);
        bolt.transform.localScale = new Vector3(0.035f, 0.015f, 0.035f);
        Destroy(bolt.GetComponent<Collider>());
        _boltRenderer = bolt.GetComponent<Renderer>();
        Tint(_boltRenderer, new Color(1f, 0.82f, 0.35f));
        _bolt = bolt.transform;
        _boltRestLocal = bolt.transform.localPosition;
        Label(_station.transform, "연습 볼트", top + new Vector3(0.12f, 0.12f, -0.08f), 0.3f);

        // 연습 멀티미터 + 단자
        var meter = Box("PracticeMeter", _station.transform, top + new Vector3(0.33f, 0.04f, 0.08f), new Vector3(0.09f, 0.08f, 0.04f), new Color(0.95f, 0.75f, 0.15f), true);
        meter.transform.localRotation = Quaternion.Euler(-20f, 180f, 0f);
        _lcd = Label(meter.transform, "- - -", new Vector3(0f, 0.1f, -0.6f), 0.5f);
        _lcd.transform.localScale = new Vector3(1f / 0.09f, 1f / 0.08f, 1f / 0.04f) * 0.1f;
        _meterJack = new GameObject("Jack").transform;
        _meterJack.SetParent(meter.transform, false);
        _meterJack.localPosition = new Vector3(0.3f, -0.5f, 0f);
        _probe = MakeGrabbable("PracticeProbe", _station.transform.TransformPoint(top + new Vector3(0.3f, 0.015f, -0.06f)), _tableRot * Quaternion.Euler(0f, 0f, 90f),
            new Vector3(0.015f, 0.065f, 0.015f), new Color(0.9f, 0.15f, 0.12f), 0.08f, PrimitiveType.Cylinder);
        _probeCable = new GameObject("Cable").AddComponent<LineRenderer>();
        _probeCable.transform.SetParent(_station.transform, false);
        _probeCable.positionCount = 12;
        _probeCable.widthMultiplier = 0.006f;
        _probeCable.useWorldSpace = true;
        _probeCable.sharedMaterial = NewUnlit(new Color(0.85f, 0.12f, 0.1f));
        _probeCable.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var term = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        term.name = "PracticeTerminal";
        term.transform.SetParent(_station.transform, false);
        term.transform.localPosition = top + new Vector3(0.33f, 0.025f, -0.12f);
        term.transform.localScale = new Vector3(0.025f, 0.025f, 0.025f);
        Destroy(term.GetComponent<Collider>());
        Tint(term.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.2f));
        _terminal = term.transform;
        Label(_station.transform, "연습 단자", top + new Vector3(0.33f, 0.1f, -0.12f), 0.3f);

        // 안내판(책상 뒤쪽 위)
        var board = new GameObject("Board");
        board.layer = 1;
        board.transform.SetParent(_station.transform, false);
        board.transform.localPosition = new Vector3(0f, TableH + 0.5f, -(TableD * 0.5f + 0.05f));   // 책상 먼 쪽(로컬 +Z 가 나를 향함)
        board.transform.localRotation = Quaternion.Euler(10f, 180f, 0f);   // 앞쪽(나)을 향해 살짝 숙임
        var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(bg.GetComponent<Collider>());
        bg.layer = 1;
        bg.transform.SetParent(board.transform, false);
        bg.transform.localPosition = new Vector3(0f, 0f, 0.003f);
        bg.transform.localScale = new Vector3(0.78f, 0.46f, 1f);
        bg.GetComponent<Renderer>().sharedMaterial = NewUnlit(new Color(0.13f, 0.17f, 0.23f, 0.95f), 2980);   // 글자(3000)보다 먼저 그려 글자를 덮지 않게
        _boardText = Label(board.transform, "", new Vector3(0f, 0.02f, 0f), 1f);
        _boardText.rectTransform.sizeDelta = new Vector2(0.72f, 0.38f);
        _boardText.alignment = TextAlignmentOptions.TopLeft;
        _boardText.enableAutoSizing = true;
        _boardText.fontSizeMin = 0.08f;
        _boardText.fontSizeMax = 0.3f;

        // 건너뛰기 버튼 — 안내판은 책상 너머라 손이 닿지 않으므로, 책상 앞쪽 오른편 모서리 위에 비스듬히 세운다
        var pad = new GameObject("SkipPad");
        pad.layer = 1;
        pad.transform.SetParent(_station.transform, false);
        pad.transform.localPosition = new Vector3(-(TableW * 0.5f - 0.1f), TableH + 0.16f, TableD * 0.5f - 0.03f);
        pad.transform.localRotation = Quaternion.Euler(35f, 180f, 0f);   // 나를 향하고 위로 젖힘
        var skip = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(skip.GetComponent<Collider>());
        skip.name = "Skip";
        skip.layer = 1;
        skip.transform.SetParent(pad.transform, false);
        skip.transform.localScale = new Vector3(0.17f, 0.065f, 1f);
        _skipMat = NewUnlit(new Color(0.75f, 0.32f, 0.14f, 0.95f), 2990);
        skip.GetComponent<Renderer>().sharedMaterial = _skipMat;
        _skipBtn = skip.transform;
        var st = Label(pad.transform, "건너뛰기", new Vector3(0f, 0f, -0.002f), 0.4f);
        st.alignment = TextAlignmentOptions.Center;

        _arrow = Label(_station.transform, "▼", Vector3.zero, 1.6f);
        _arrow.color = new Color(1f, 0.85f, 0.25f);
        return true;
    }

    private void RefreshBoard()
    {
        if (_boardText == null) return;
        var sb = new System.Text.StringBuilder();
        sb.Append("<b>연습장</b>  <size=70%><color=#9fb3c8>기본 동작을 하나씩 해 봅니다</color></size>\n");
        Step[] list = { Step.Grab, Step.Laser, Step.Belt, Step.PalmMenu, Step.BoltLoosen, Step.Probe };
        sb.Append("<size=70%>");
        for (int i = 0; i < list.Length; i++)
        {
            var s = list[i];
            bool done = (int)_step > (int)s && !(s == Step.BoltLoosen && _step == Step.BoltTighten);
            bool cur = _step == s || (s == Step.BoltLoosen && _step == Step.BoltTighten);
            string name = s == Step.BoltLoosen ? "볼트 풀고 조이기" : Titles[(int)s];
            sb.Append(done ? "<color=#5be37d>✔ " : cur ? "<color=#ffd27a>▶ " : "<color=#7a8696>· ");
            sb.Append(name).Append("</color>");
            sb.Append(i < list.Length - 1 ? "   " : "\n");
            if (i == 2) sb.Append('\n');
        }
        sb.Append("</size>\n<b>").Append(Titles[(int)_step]).Append("</b>\n");
        sb.Append("<size=80%>").Append(Bodies[(int)_step]).Append("</size>");
        _boardText.text = sb.ToString();
    }

    // ── 단계별 ─────────────────────────────

    private void MoveBlockFar()
    {
        if (_block == null) return;
        foreach (var h in LocalHands.All) if (h != null && h.holdingObj == _block) h.ForceReleaseGrab();
        _pulled = false;
        // 책상 반대편 바닥 2m 쯤(내 쪽에서 책상 너머)
        Vector3 p = _station.transform.TransformPoint(new Vector3(-0.2f, 0f, -1.3f));
        if (Physics.Raycast(p + Vector3.up * 1f, Vector3.down, out var hit, 3f, ~(1 << _grabLayer), QueryTriggerInteraction.Ignore)) p = hit.point;
        p += Vector3.up * 0.06f;
        var rb = _block.GetComponent<Rigidbody>();
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.position = p; }
        _block.transform.position = p;
        _blockRestPos = p;
    }

    private void TryHolsterSpanner()
    {
        if (_spanner == null || _spannerHolstered) return;
        if (!VrToolBelt.TryGetFreeSlotNear(Center(_spanner.transform), 0.2f, out _holsterSlot, out var pos, out var yaw)) return;
        _spannerHolstered = true;
        _holsterRelRot = Quaternion.Inverse(yaw) * _spanner.transform.rotation;
        var rb = _spanner.GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
        WorldSfx.Click(pos);
    }

    private void UpdateHolster()
    {
        if (!_spannerHolstered || _spanner == null) return;
        if (!VrToolBelt.TryGetSlot(_holsterSlot, out var pos, out var yaw)) return;
        var t = _spanner.transform;
        Quaternion rot = yaw * _holsterRelRot;
        Vector3 off = rot * Vector3.Scale(t.InverseTransformPoint(Center(t)), t.lossyScale);
        t.SetPositionAndRotation(pos - off, rot);
        // AutoHand 는 kinematic 물체를 잡지 않는다 → 손이 가까울 때만 물리를 풀어 둔다(중력은 끈 채)
        bool near = false;
        foreach (var h in LocalHands.All)
        {
            if (h == null) continue;
            Vector3 palm = h.palmTransform != null ? h.palmTransform.position : h.transform.position;
            if ((palm - pos).sqrMagnitude < 0.3f * 0.3f) { near = true; break; }
        }
        var rb = _spanner.GetComponent<Rigidbody>();
        if (rb != null)
        {
            if (rb.isKinematic == near) rb.isKinematic = !near;
            rb.useGravity = false;
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }
    }

    private void Unlock(Grabbable g, bool gravity)
    {
        var rb = g != null ? g.GetComponent<Rigidbody>() : null;
        if (rb == null) return;
        rb.isKinematic = false;
        rb.useGravity = gravity;
    }

    private void TurnBolt()
    {
        if (_spanner == null || _bolt == null || !_spanner.IsHeld()) { _hasPrevDir = false; return; }
        Vector3 a = _spanner.transform.TransformPoint(_spannerEndA), b = _spanner.transform.TransformPoint(_spannerEndB);
        Vector3 bp = _bolt.position;
        float near = Mathf.Min((a - bp).sqrMagnitude, (b - bp).sqrMagnitude);
        if (near > 0.08f * 0.08f) { _hasPrevDir = false; return; }

        Vector3 n = _bolt.up;
        Vector3 dir = Vector3.ProjectOnPlane(_spanner.transform.TransformDirection(_spannerAxisLocal), n);
        if (dir.sqrMagnitude < 1e-4f) { _hasPrevDir = false; return; }
        dir.Normalize();
        float deg = _hasPrevDir ? Mathf.Abs(Vector3.SignedAngle(_prevSpannerDir, dir, n)) : 0f;
        _prevSpannerDir = dir; _hasPrevDir = true;
        if (deg > 45f || deg <= 0f) return;

        _boltProgress = Mathf.Clamp01(_boltProgress + deg / BoltDegrees);
        float lift = _step == Step.BoltLoosen ? _boltProgress : 1f - _boltProgress;
        _bolt.localPosition = _boltRestLocal + Vector3.up * 0.018f * lift;
        _bolt.Rotate(n, deg * (_step == Step.BoltLoosen ? -1f : 1f), Space.World);

        _ratchetAcc += deg;
        if (_ratchetAcc >= 30f)
        {
            _ratchetAcc = 0f;
            AudioSource.PlayClipAtPoint(EquipFx.Ratchet, bp, 0.7f);
            foreach (var h in _spanner.GetHeldBy()) Haptic(h, 0.02f, 0.4f);
        }

        if (_boltProgress >= 1f)
        {
            _boltProgress = 0f;
            if (_step == Step.BoltLoosen) SetStep(Step.BoltTighten);
            else
            {
                Tint(_boltRenderer, new Color(0.3f, 0.95f, 0.4f));
                WorldSfx.TorqueClick(bp);
                SetStep(Step.Probe);
            }
        }
    }

    private void CheckProbe()
    {
        if (_probe == null || _terminal == null) return;
        if (!_probe.IsHeld()) { _probeDwell = 0f; return; }
        var t = _probe.transform;
        Vector3 tipA = t.TransformPoint(Vector3.up), tipB = t.TransformPoint(Vector3.down);   // 원기둥 양 끝(로컬 ±1)
        float d = Mathf.Min((tipA - _terminal.position).magnitude, (tipB - _terminal.position).magnitude);
        if (d > 0.035f) { _probeDwell = 0f; if (_lcd != null) _lcd.text = "- - -"; return; }
        _probeDwell += Time.unscaledDeltaTime;
        if (_lcd != null) _lcd.text = _probeDwell < ProbeDwell ? "..." : "12.6 V";
        if (_probeDwell >= ProbeDwell)
        {
            foreach (var h in _probe.GetHeldBy()) Haptic(h, 0.1f, 0.7f);
            WorldSfx.PlayAt("UI_Click", _terminal.position, 0.8f);
            SetStep(Step.Done);
        }
    }

    // ── 안내 표시 ─────────────────────────────

    private void UpdateArrow()
    {
        if (_arrow == null) return;
        Transform target = null;
        switch (_step)
        {
            case Step.Grab: case Step.Laser: target = _block != null ? _block.transform : null; break;
            case Step.Belt: target = _spanner != null && !_spanner.IsHeld() ? _spanner.transform : null; break;
            case Step.BoltLoosen: case Step.BoltTighten: target = _spanner != null && _spanner.IsHeld() ? _bolt : (_spanner != null ? _spanner.transform : null); break;
            case Step.Probe: target = _probe != null && _probe.IsHeld() ? _terminal : (_probe != null ? _probe.transform : null); break;
        }
        bool on = target != null && !(target == (_block != null ? _block.transform : null) && _block.IsHeld());
        if (_arrow.gameObject.activeSelf != on) _arrow.gameObject.SetActive(on);
        if (!on) return;
        var cam = Camera.main;
        Vector3 p = target.position + Vector3.up * (0.12f + 0.025f * Mathf.Sin(Time.unscaledTime * 5f));
        _arrow.transform.position = p;
        if (cam != null) _arrow.transform.rotation = Quaternion.LookRotation(p - cam.transform.position, Vector3.up);
    }

    private void UpdateCable()
    {
        if (_probeCable == null || _probe == null || _meterJack == null) return;
        Vector3 a = _meterJack.position, b = _probe.transform.TransformPoint(Vector3.down * 0.9f);
        float sag = Mathf.Clamp(0.25f - Vector3.Distance(a, b) * 0.2f, 0.02f, 0.2f);
        Vector3 mid = (a + b) * 0.5f + Vector3.down * sag;
        int n = _probeCable.positionCount;
        for (int i = 0; i < n; i++)
        {
            float k = (float)i / (n - 1);
            _probeCable.SetPosition(i, (1 - k) * (1 - k) * a + 2 * (1 - k) * k * mid + k * k * b);
        }
        // 케이블이 1.2m 이상 늘어나면 손에서 빠진다
        if (_probe.IsHeld() && Vector3.Distance(a, _probe.transform.position) > 1.2f)
            foreach (var h in LocalHands.All) if (h != null && h.holdingObj == _probe) h.ForceReleaseGrab();
    }

    private void HandleSkipPoke()
    {
        if (_skipBtn == null) return;
        bool over = false;
        foreach (var h in LocalHands.All)
        {
            var tip = LocalHands.IndexTip(h);
            if (tip == null) continue;
            Vector3 lp = _skipBtn.InverseTransformPoint(tip.position);   // 버튼 크기 1×1(스케일 적용 전 좌표)
            if (Mathf.Abs(lp.x) <= 0.6f && Mathf.Abs(lp.y) <= 0.7f && lp.z > -0.025f && lp.z < 0.06f) { over = true; break; }
        }
        _skipMat.color = over ? new Color(1f, 0.55f, 0.2f, 1f) : new Color(0.75f, 0.32f, 0.14f, 0.95f);
        if (over && !_skipLatched && Time.unscaledTime - _stepStarted > 0.5f)
        {
            _skipLatched = true;
            Finish(_step != Step.Done);
            return;
        }
        if (!over) _skipLatched = false;
    }

    // ── 만들기 도우미 ─────────────────────────────

    private Grabbable MakeGrabbable(string name, Vector3 pos, Quaternion rot, Vector3 size, Color color, float mass, PrimitiveType shape = PrimitiveType.Cube)
    {
        var go = GameObject.CreatePrimitive(shape);
        go.name = name;
        go.layer = _grabLayer;
        go.transform.SetParent(_station.transform, true);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = size;
        if (shape == PrimitiveType.Cylinder)
        {
            // 원기둥 기본 콜라이더(캡슐)는 잡기 어렵다 → 박스
            Destroy(go.GetComponent<Collider>());
            go.AddComponent<BoxCollider>();
        }
        Tint(go.GetComponent<Renderer>(), color);
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        go.AddComponent<PracticeTutorialItem>();
        var g = go.AddComponent<Grabbable>();
        return g;
    }

    private Grabbable MakeSpanner(Vector3 pos, Quaternion rot)
    {
        // 맵의 장비 자리에 지정된 스패너 프리팹 모양(메시만) 복사
        GameObject src = null;
        foreach (var slot in EquipmentSlot.All)
        {
            if (slot == null || slot.prefab == null) continue;
            var mt = slot.prefab.GetComponentInChildren<ManualTool>(true);
            if (mt != null && mt.kind == ManualTool.Kind.Spanner) { src = slot.prefab.gameObject; break; }
        }

        var go = new GameObject("PracticeSpanner");
        go.layer = _grabLayer;
        go.transform.SetParent(_station.transform, true);
        go.transform.SetPositionAndRotation(pos, rot);
        bool copied = false;
        if (src != null)
        {
            foreach (var mf in src.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null) continue;
                var c = new GameObject(mf.name);
                c.layer = _grabLayer;
                c.transform.SetParent(go.transform, false);
                // 프리팹 루트 기준 상대 배치
                Matrix4x4 m = src.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                c.transform.localPosition = m.GetColumn(3);
                c.transform.localRotation = m.rotation;
                c.transform.localScale = m.lossyScale;
                c.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                c.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
                copied = true;
            }
        }
        if (!copied)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(bar.GetComponent<Collider>());
            bar.layer = _grabLayer;
            bar.transform.SetParent(go.transform, false);
            bar.transform.localScale = new Vector3(0.025f, 0.012f, 0.22f);
            Tint(bar.GetComponent<Renderer>(), new Color(0.75f, 0.77f, 0.8f));
        }

        // 콜라이더 = 모양 경계 상자, 긴 축 = 끝 방향
        Bounds b = default; bool any = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var lb = TransformBounds(go.transform, r.bounds);
            if (!any) { b = lb; any = true; } else b.Encapsulate(lb);
        }
        if (!any) b = new Bounds(Vector3.zero, new Vector3(0.03f, 0.015f, 0.22f));
        var box = go.AddComponent<BoxCollider>();
        box.center = b.center;
        box.size = Vector3.Max(b.size, Vector3.one * 0.012f);
        Vector3 s = b.size;
        _spannerAxisLocal = s.x >= s.y && s.x >= s.z ? Vector3.right : s.y >= s.z ? Vector3.up : Vector3.forward;
        float half = Vector3.Dot(s, _spannerAxisLocal) * 0.5f;
        _spannerEndA = b.center + _spannerAxisLocal * half;
        _spannerEndB = b.center - _spannerAxisLocal * half;

        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        go.AddComponent<PracticeTutorialItem>().holsterable = true;
        return go.AddComponent<Grabbable>();
    }

    private static Bounds TransformBounds(Transform t, Bounds world)
    {
        Vector3 c = world.center, e = world.extents;
        var lb = new Bounds(t.InverseTransformPoint(c), Vector3.zero);
        for (int i = 0; i < 8; i++)
            lb.Encapsulate(t.InverseTransformPoint(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
        return lb;
    }

    private GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Color color, bool collider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        if (!collider) Destroy(go.GetComponent<Collider>());
        Tint(go.GetComponent<Renderer>(), color);
        return go;
    }

    private TextMeshPro Label(Transform parent, string text, Vector3 localPos, float size)
    {
        var go = new GameObject("Label");
        go.layer = 1;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        if (parent == _station.transform) go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // 책상 앞(나)을 향해
        var t = go.AddComponent<TextMeshPro>();
        var kf = MobileWorkButton.KoreanFont();
        if (kf != null) t.font = kf; else if (_font != null) t.font = _font;
        t.text = text;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.rectTransform.sizeDelta = new Vector2(0.4f, 0.1f);
        t.color = Color.white;
        t.richText = true;
        t.outlineWidth = 0.08f;
        t.outlineColor = new Color32(0, 0, 0, 200);
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return t;
    }

    private static void Tint(Renderer r, Color c)
    {
        if (r == null) return;
        // 기본 도형 재질(파이프라인 기본 Lit)을 그대로 쓰고 색만 바꾼다 — 런타임 셰이더 변형이 빌드에서 빠지는 문제를 피한다
        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", c);
        mpb.SetColor("_Color", c);
        r.SetPropertyBlock(mpb);
    }

    private Material NewUnlit(Color c, int queue = 3000)
    {
        var m = new Material(Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default")) { color = c };
        m.renderQueue = queue;
        _mats.Add(m);
        return m;
    }

    private static Vector3 Center(Transform root)
    {
        bool any = false; Bounds b = default;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (r == null || !r.enabled || r is LineRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any ? b.center : root.position;
    }

    private static void AllowLaserOn(int layer)
    {
        if (layer < 0 || layer >= 32) return;
        foreach (var g in FindObjectsByType<HandDistanceGrabber>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (g != null && (g.layers.value & (1 << layer)) == 0) g.layers = g.layers.value | (1 << layer);
    }

    private static void Haptic(Hand h, float duration, float amp)
    {
        if (h == null) return;
        try { h.PlayHapticVibration(duration, amp); } catch { }
    }
}

/// <summary>연습장에서 만든 물건 표시(네트워크 없음). holsterable = 허리 벨트에 꽂을 수 있음.</summary>
public class PracticeTutorialItem : MonoBehaviour
{
    public bool holsterable;
}
