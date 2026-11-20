using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XRAirpotrSecurity;

// ============================================================
//  CarPartListPanel.cs
//
//  PC·모바일 화면 왼쪽 위에 '부위 목록' 버튼(키 L)을 두고, 누르면 자유 실습 부위의 지금 상태를 세 탭으로 보여 준다.
//   · 풀 수 있음 — 지금 바로 볼트를 풀 수 있는 부위(어셈블리면 세부 부위 수 표시)
//   · 막힘      — 아직 못 푸는 부위와 그 이유(먼저 떼어야 하는 부위·휠·어셈블리)
//   · 떼어냄    — 떼어냈거나 끼우는 중인 부위(다시 끼울 수 있는지)
//  자유 탈거 모드에서 '지금 뭘 더 분해할 수 있지?'를 바로 알 수 있게 한다. VR 은 쓰지 않는다. 씬에 둘 필요 없이 시작할 때 스스로 붙는다.
// ============================================================
public class CarPartListPanel : MonoBehaviour
{
    private GameObject _canvas;
    private GameObject _panel;
    private TMP_Text _body;
    private TMP_Text[] _tabLabels;
    private ScrollRect _scroll;
    private bool _open;
    private int _tab;
    private float _nextRefresh, _nextCheck;
    private readonly int[] _count = new int[3];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindAnyObjectByType<CarPartListPanel>() != null) return;
        var go = new GameObject("[CarPartListPanel]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<CarPartListPanel>();
    }

    private void Update()
    {
        if (Time.unscaledTime >= _nextCheck)
        {
            _nextCheck = Time.unscaledTime + 1f;
            var svc = CarPartService.Instance;
            bool want = svc != null && svc.Ready && Utils.GetPlatformType() != PlatformType.VR;
            if (want && _canvas == null) Build();
            else if (!want && _canvas != null) { Destroy(_canvas); _canvas = null; _panel = null; _open = false; }
        }
        if (_canvas == null) return;
        if (Input.GetKeyDown(KeyCode.L)) SetOpen(!_open);
        CheckHeaderClick();
        if (_open && Time.unscaledTime >= _nextRefresh) { _nextRefresh = Time.unscaledTime + 0.5f; Refresh(); }
    }

    private void OnDestroy() { if (_canvas != null) Destroy(_canvas); }

    /// <summary>오른쪽 위 메뉴의 '부위 목록' 버튼(PcHudLayout)이 부른다. L 키와 같다.</summary>
    public static void Toggle()
    {
        var p = FindAnyObjectByType<CarPartListPanel>();
        if (p != null && p._canvas != null) p.SetOpen(!p._open);
    }

    public static bool IsOpen
    {
        get { var p = FindAnyObjectByType<CarPartListPanel>(); return p != null && p._open; }
    }

    private void SetOpen(bool open)
    {
        _open = open;
        if (_panel != null) _panel.SetActive(open);
        if (open) { _nextRefresh = 0f; Refresh(); }
    }

    // ── UI 만들기 ──
    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    private TMP_Text NewText(Transform parent, string name, float size, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var kf = MobileWorkButton.KoreanFont();
        if (kf != null) t.font = kf;
        t.fontSize = size; t.alignment = align; t.color = Color.white; t.raycastTarget = false;
        return t;
    }

    private Button NewButton(Transform parent, string name, Vector2 pos, Vector2 size, out TMP_Text label)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0.1f, 0.14f, 0.2f, 0.9f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        label = NewText(rt, "Text", 21f, TextAlignmentOptions.Center);
        var lr = label.rectTransform; lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = lr.offsetMax = Vector2.zero;
        return btn;
    }

    private void Build()
    {
        _canvas = new GameObject("[CarPartListPanel_Canvas]");
        var cv = _canvas.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 41;
        var scaler = _canvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _canvas.AddComponent<GraphicRaycaster>();
        if (EventSystem.current == null) _canvas.AddComponent<EventSystem>();

        // 여는 버튼은 오른쪽 위 메뉴 줄에 있다(PcHudLayout, 11/19). 단축키 L.

        // 패널 — 오른쪽 위 버튼 묶음 아래에 연다. 왼쪽 열(단계 안내·미니맵)에 열면 미니맵을 가렸다.
        // 오른쪽 끝 접힘 탭(▶)을 가리지 않게 80 띄운다
        var prt = NewRect("Panel", _canvas.transform);
        prt.anchorMin = prt.anchorMax = new Vector2(1f, 1f); prt.pivot = new Vector2(1f, 1f);
        prt.anchoredPosition = new Vector2(-80f, -150f); prt.sizeDelta = new Vector2(600f, 620f);
        var bg = prt.gameObject.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.08f, 0.12f, 0.92f);
        _panel = prt.gameObject;

        // 탭 3개
        _tabLabels = new TMP_Text[3];
        string[] names = { "풀 수 있음", "막힘", "떼어냄" };
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var tb = NewButton(prt, "Tab" + i, new Vector2(8f + i * 194f, -8f), new Vector2(190f, 40f), out _tabLabels[i]);
            _tabLabels[i].text = names[i];
            tb.onClick.AddListener(() => { _tab = idx; _nextRefresh = 0f; if (_scroll != null) _scroll.verticalNormalizedPosition = 1f; Refresh(); });
        }

        // 스크롤 영역
        var vp = NewRect("Viewport", prt);
        vp.anchorMin = Vector2.zero; vp.anchorMax = Vector2.one;
        vp.offsetMin = new Vector2(10f, 10f); vp.offsetMax = new Vector2(-10f, -56f);
        vp.gameObject.AddComponent<RectMask2D>();
        var content = NewRect("Content", vp);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0f, 100f);
        _body = content.gameObject.AddComponent<TextMeshProUGUI>();
        var kf = MobileWorkButton.KoreanFont();
        if (kf != null) _body.font = kf;
        _body.fontSize = 22f; _body.alignment = TextAlignmentOptions.TopLeft; _body.color = Color.white;
        // raycastTarget 켬: 분류 머리줄 클릭이 뒤의 월드(물건 잡기)로 새지 않고, 글자 위에서도 휠 스크롤이 된다
        _body.richText = true; _body.textWrappingMode = TextWrappingModes.Normal; _body.raycastTarget = true;
        var fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _scroll = prt.gameObject.AddComponent<ScrollRect>();
        _scroll.viewport = vp; _scroll.content = content;
        _scroll.horizontal = false; _scroll.vertical = true; _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 40f;

        _panel.SetActive(false);
    }

    // ── 내용 ──
    private void Refresh()
    {
        var svc = CarPartService.Instance;
        if (svc == null || !svc.Ready || _body == null) return;
        var defs = CarPartService.Defs;
        int n = defs.Length;

        var kids = new int[n];
        for (int p = 0; p < n; p++)
        {
            string par = defs[p].parent;
            if (string.IsNullOrEmpty(par)) continue;
            for (int q = 0; q < n; q++) if (defs[q].id == par) { kids[q]++; break; }
        }

        var lines = new List<(int cat, string text, bool hv)>[3] { new List<(int, string, bool)>(), new List<(int, string, bool)>(), new List<(int, string, bool)>() };
        for (int p = 0; p < n; p++)
        {
            int cat = CategoryOf(defs[p].id);
            var d = defs[p];
            int st = svc.StateOf(p);
            string tag = "";
            if (kids[p] > 0) tag += $" · 어셈블리(세부 {kids[p]}개)";
            if (!string.IsNullOrEmpty(d.parent)) tag += $" · {NameOf(defs, d.parent)} 안";
            // 볼트 체결 수 — 일부만 풀었거나 조이는 중이면 노랑으로 눈에 띄게
            int bn = d.bolts != null ? d.bolts.Length : 0;
            if (bn > 0 && st != (int)CarPartService.PartState.Off)
            {
                int bt = svc.BoltsTightened(p);
                tag += bt == bn ? $" · 볼트 {bn}/{bn} 체결" : $" · <color=#FFD54A>볼트 {bt}/{bn} 체결</color>";
            }
            if (d.hv) tag += " · <color=#FF5555><b>[고전압]</b></color>";
            string su = CarPartRules.SingleUseOf(d.id);
            if (su != null) tag += $" · 재사용 금지: {su}";
            string tagText = tag.Length > 0 ? $" <size=70%><color=#9FB4C8>{tag.Substring(3)}</color></size>" : "";
            // 고전압 부위는 이름·표시 점을 빨강으로 — 일반 부위(초록·회색·파랑)와 한눈에 구분
            string nm = d.hv ? $"<color=#FF8A8A>{d.name}</color>" : d.name;
            string dotOk = d.hv ? "#FF5555" : "#7CFF8A", dotLock = d.hv ? "#C0504D" : "#8A95A3", dotMid = d.hv ? "#FF5555" : "#7FD3FF";

            if (st == (int)CarPartService.PartState.Tight)
            {
                if (svc.CanRemove(p)) lines[0].Add((cat, $"<color={dotOk}>●</color> {nm}{tagText}", d.hv));
                else lines[1].Add((cat, $"<color={dotLock}>○</color> {nm}{tagText}\n    <size=72%><color=#FFC870>먼저:</color> {PrereqText(svc, p)}</size>", d.hv));
            }
            else if (st == (int)CarPartService.PartState.Off && CarPartRules.IsDamaged(d.id))
            {
                lines[2].Add((cat, $"<color=#FF5555>×</color> {nm}{tagText}\n    <size=72%><color=#FF6B6B>파손됨 — 신품 부품 보관함(책상 옆)에서 새 부품을 받으세요</color></size>", d.hv));
            }
            else if (st == (int)CarPartService.PartState.Off)
            {
                string blk = svc.MountBlockersOf(p);
                string sub = string.IsNullOrEmpty(blk) ? "<color=#9FE0B0>다시 끼울 수 있음</color>" : $"<color=#FFC870>먼저 달기: {blk}</color>";
                lines[2].Add((cat, $"<color=#FFD54A>◆</color> {nm}{tagText}\n    <size=72%>{sub}</size>", d.hv));
            }
            else
            {
                string lubeLine = "";
                if (svc.LubeSpotCount(p) > 0)
                {
                    svc.LubeStatus(p, out int lok, out int ltot, out int lex);
                    string lc = lex > 0 ? "#FF6B6B" : lok == ltot ? "#7CFF8A" : "#FFD54A";
                    lubeLine = $" <color={lc}>· {svc.LubeNameOf(p)} 도포 {lok}/{ltot}{(lex > 0 ? " (과다)" : "")}</color>";
                }
                lines[2].Add((cat, $"<color={dotMid}>◇</color> {nm}{tagText}\n    <size=72%><color=#9FE0B0>장착됨 — 볼트를 규정 토크로 조이세요</color>{lubeLine}</size>", d.hv));
            }
        }

        // 휠·타이어 — 부위 표(CarPartService)가 아니라 TireService 가 따로 관리한다(허브 너트 5개)
        var tire = TireService.Instance;
        if (tire != null)
        {
            for (int w = 0; w < TireService.Wheels; w++)
            {
                if (!tire.TryGetWheel(w, out _, out _)) continue;
                string wn = WheelNames[w];
                int nt = tire.NutsTightened(w), nn = TireService.NutsPerWheel;
                string nutTxt = nt == nn ? $"휠 너트 {nn}/{nn} 체결" : $"<color=#FFD54A>휠 너트 {nt}/{nn} 체결</color>";
                if (tire.IsWheelTight(w)) lines[0].Add((CatWheel, $"<color=#7CFF8A>●</color> {wn} <size=70%><color=#9FB4C8>{nutTxt}</color></size>", false));
                else if (tire.IsWheelOff(w)) lines[2].Add((CatWheel, $"<color=#FFD54A>◆</color> {wn}\n    <size=72%><color=#9FE0B0>허브에 가져가면 다시 끼울 수 있음</color></size>", false));
                else lines[2].Add((CatWheel, $"<color=#7FD3FF>◇</color> {wn} <size=70%><color=#9FB4C8>{nutTxt}</color></size>\n    <size=72%><color=#9FE0B0>장착됨 — 휠 너트를 규정 토크로 조이세요</color></size>", false));
            }
        }

        for (int i = 0; i < 3; i++) _count[i] = lines[i].Count;
        string[] names = { "풀 수 있음", "막힘", "떼어냄" };
        for (int i = 0; i < 3; i++) if (_tabLabels != null && _tabLabels[i] != null) _tabLabels[i].text = $"{(i == _tab ? "▶ " : "")}{names[i]} {_count[i]}";

        var sb = new StringBuilder();
        if (_tab == 0) sb.Append("<size=76%><color=#9FB4C8>지금 바로 볼트를 풀 수 있는 부위입니다. 어셈블리를 떼면 그 안의 세부 부위가 열립니다.</color></size>\n");
        else if (_tab == 1) sb.Append("<size=76%><color=#9FB4C8>아직 풀 수 없는 부위와 먼저 떼어야 하는 것입니다.</color></size>\n");
        else sb.Append("<size=76%><color=#9FB4C8>떼어낸 부위를 들어 원래 자리로 가져가면 다시 끼울 수 있습니다.</color></size>\n");
        sb.Append("<size=70%><color=#7CFF8A>초록(뗌) = 이미 뗀 것</color>  <color=#FFC870>주황 = 아직 못 뗌</color>  <color=#FF5555>빨강 = 고전압 부위(차단 절차 필요)</color></size>\n");
        string loto = FreeLoto.StatusRich();
        if (loto != null) sb.Append(loto);
        if (lines[_tab].Count == 0) sb.Append("\n(없음)");
        else sb.Append("<size=70%><color=#7F93A8>분류 이름을 누르면 펼치거나 접습니다.</color></size>\n");
        // 분류별로 묶는다 — 머리줄(▶/▼ 이름 개수)을 누르면 펼침·접힘(TMP 링크, Update 에서 클릭 확인)
        for (int c = 0; c < CatNames.Length; c++)
        {
            int cnt = 0, hvCnt = 0;
            foreach (var l in lines[_tab]) if (l.cat == c) { cnt++; if (l.hv) hvCnt++; }
            if (cnt == 0) continue;
            bool exp = _expanded.Contains(c);
            string hvMark = hvCnt > 0 ? $" <size=80%><color=#FF5555><b>고전압 {hvCnt}</b></color></size>" : "";
            sb.Append($"<link=\"cat{c}\"><color=#E6EEF7><b>{(exp ? "▼" : "▶")} {CatNames[c]}</b></color> <size=80%><color=#9FB4C8>{cnt}</color></size>{hvMark}</link>\n");
            if (!exp) continue;
            foreach (var l in lines[_tab]) if (l.cat == c) sb.Append("  ").Append(l.text.Replace("\n    ", "\n      ")).Append('\n');
        }
        if (CarPartRules.CoolantAirValue == 1) sb.Append("\n<size=76%><color=#FFB000>냉각 회로가 열려 있습니다 — 조립 후 GDS 에어빼기 필요</color></size>");
        else if (CarPartRules.CoolantAirValue == 2) sb.Append("\n<size=76%><color=#FFB000>냉각 회로 에어빼기 필요 (GDS → 부가 기능)</color></size>");
        if (ManualInspection.Summary != null) sb.Append($"\n<size=76%><color=#9FD3FF>{ManualInspection.Summary}</color></size>");
        if (SuspensionInspection.Summary != null) sb.Append($"\n<size=76%><color=#9FD3FF>{SuspensionInspection.Summary}</color></size>");
        string pend = CarPartRules.PendingLearnNames();
        if (pend != null) sb.Append($"\n<size=76%><color=#FFB000>GDS 학습·조정 필요: {pend}</color></size>");
        if (CarPartRules.ReuseCount > 0) sb.Append($"\n<size=76%><color=#FF6B6B>재사용 금지 부품을 그대로 끼운 횟수: {CarPartRules.ReuseCount}</color></size>");
        if (CarPartRules.DropCount > 0) sb.Append($"\n<size=76%><color=#FF6B6B>낙하·충격으로 부서뜨린 부품: {CarPartRules.DropCount}회</color></size>");
        if (CarPartRules.NewCount > 0) sb.Append($"\n<size=76%><color=#7CFF8A>신품으로 교체한 횟수: {CarPartRules.NewCount}</color></size>");
        _body.text = sb.ToString();
    }

    // ── 분류(정비지침서 장 구분) ──
    private static readonly string[] CatNames =
    {
        "바디 외장", "바디 내장·시트", "고전압 배터리·충전", "구동·전력 제어", "냉각·공조",
        "서스펜션·액슬", "조향", "브레이크", "휠·타이어", "기타",
    };
    private const int CatWheel = 8, CatEtc = 9;
    private static readonly string[] WheelNames = { "앞 왼쪽 휠·타이어", "앞 오른쪽 휠·타이어", "뒤 왼쪽 휠·타이어", "뒤 오른쪽 휠·타이어" };
    private readonly HashSet<int> _expanded = new HashSet<int>();

    private static bool Any(string id, params string[] keys) { foreach (var k in keys) if (id.StartsWith(k)) return true; return false; }

    /// <summary>부위 id → 분류 번호(CatNames). 새 부위가 늘어도 '기타'로 빠질 뿐 목록에서 사라지지 않는다.</summary>
    private static int CategoryOf(string id)
    {
        if (Any(id, "Brake", "Epb", "Ieb", "RemoteReservoir", "FrontWheelSpeed", "RearWheelSpeed")) return 7;
        if (Any(id, "Steering", "TieRod", "UniversalJoint")) return 6;
        if (Any(id, "BatteryPack", "BatteryTopCase", "Bmu", "MainFuse", "Pra", "Iccu", "JunctionBlock", "RearJunctionBlock", "Qra", "ComboChargeInlet", "ChargingDoor")) return 2;
        if (Any(id, "Cooling", "CoolantTank", "AcCompressor", "AcInverter", "Hvac", "Condenser", "WaterCondenser", "ThreeWayValve", "PtcHeatPump", "CoolantManifold", "Ewp", "MotorOilCooler", "RefrigerantLines", "ActiveAirFlap")) return 4;
        if (Any(id, "FrontMotor", "RearMotor", "FrontInverter", "Vcu", "Battery12V", "FrontDriveShaft")) return 3;
        if (Any(id, "FrontSuspension", "RearSuspension", "RearShock", "RearAxle", "RearLowerArm", "RearTrailingArm", "RearUpperArm", "RearAssistArm",
                "ShockInsulator", "FrontStabilizer", "RearStabilizer", "FrontLowerArm", "FrontStrut", "StrutInsulator", "FrontKnuckle", "TensionArm", "EcsUnit")) return 5;
        if (Any(id, "Seat", "Cockpit", "FloorConsole", "DoorScuff", "FloorCarpet", "PillarTrim", "InsideMirror")) return 1;
        if (Any(id, "FrontBumper", "RearBumper", "Headlamp", "Hood", "Frunk", "FrontBeam", "Door", "Tailgate", "FrontUnderCover", "RearUnderCover",
                "Fender", "SideSill", "Mirror", "RearSpoiler", "Wiper", "CowlTopCover", "VisionRoof")) return 0;
        return CatEtc;
    }

    /// <summary>분류 머리줄 클릭 → 펼침/접힘</summary>
    private void CheckHeaderClick()
    {
        if (!_open || _body == null || !Input.GetMouseButtonDown(0)) return;
        int li = TMP_TextUtilities.FindIntersectingLink(_body, Input.mousePosition, null);   // Screen Space Overlay → 카메라 null
        if (li < 0) return;
        string lid = _body.textInfo.linkInfo[li].GetLinkID();
        if (!lid.StartsWith("cat") || !int.TryParse(lid.Substring(3), out int c)) return;
        if (!_expanded.Remove(c)) _expanded.Add(c);
        _nextRefresh = 0f; Refresh();
    }

    /// <summary>'먼저:' 항목 — 이미 뗀 것은 초록 ✓, 아직인 것은 주황</summary>
    private static string PrereqText(CarPartService svc, int p)
    {
        var sb = new StringBuilder();
        foreach (var kv in svc.RemovePrereqs(p))
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(kv.Value ? $"<color=#7CFF8A>{kv.Key}(뗌)</color>" : $"<color=#FFC870>{kv.Key}</color>");
        }
        return sb.ToString();
    }

    private static string NameOf(CarPartService.PartDef[] defs, string id)
    {
        foreach (var d in defs) if (d.id == id) return d.name;
        return id;
    }
}
