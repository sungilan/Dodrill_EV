using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
//  PartTrayPanel.Tabs.cs — 툴박스 창의 탭 (11/19)
//
//  쓰이지 않던 인벤토리 창 활용안 중 나머지를 탭으로 붙였다.
//  · 부품 보관함      : 떼어낸 부품·휠 (PartTrayPanel.cs)
//  · 안전·조립 상태   : 고전압 차단 절차 5단계·감전 사고 수, 끼우기만 하고 아직 안 조인 부품(조립 필요), 측정 기록 — 평가 근거를 한 화면에서
//  · 특수공구         : 매뉴얼 특수공구 표의 공구(품번·쓰는 곳)와 작업장에 놓인 위치. '위치 표시'를 누르면 그 공구 위에 빛기둥이 8초 선다
//  (활용안 C '공구 퀵슬롯'은 이미 있는 공구 벨트 — PC B 키 / VR 허리 벨트 — 가 같은 역할이라 따로 만들지 않았다)
// ============================================================
public partial class PartTrayPanel
{
    private static readonly string[] TabNames = { "부품 보관함", "안전·조립 상태", "특수공구" };
    private readonly GameObject[] _pages = new GameObject[3];
    private readonly Image[] _tabBg = new Image[3];
    private int _tab;

    private static readonly Color TabOn = new Color(0.13f, 0.45f, 0.8f, 1f);
    private static readonly Color TabOff = new Color(0.13f, 0.16f, 0.22f, 1f);

    // ── 공통 틀 ──
    private void BuildTabs(RectTransform panel)
    {
        for (int i = 0; i < TabNames.Length; i++)
        {
            var b = NewRect("Tab" + i, panel);
            Place(b, new Vector2(24f + i * 214f, -18f), new Vector2(206f, 48f));
            var img = b.gameObject.AddComponent<Image>();
            img.sprite = WorldHoverLabel.RoundedSprite(); img.type = Image.Type.Sliced; img.color = TabOff;
            _tabBg[i] = img;
            int k = i;
            b.gameObject.AddComponent<Button>().onClick.AddListener(() => { ShowTab(k); RefreshCurrent(); });
            var t = NewText(b, $"<b>{TabNames[i]}</b>", 21f, TextAlignmentOptions.Center); Stretch(t.rectTransform);
        }
    }

    private static RectTransform NewPage(RectTransform panel, string name)
    {
        var page = NewRect(name, panel);
        page.anchorMin = Vector2.zero; page.anchorMax = Vector2.one;
        page.offsetMin = Vector2.zero; page.offsetMax = new Vector2(0f, -80f);   // 탭 줄 아래
        return page;
    }

    /// <summary>page 안 top 아래를 채우는 세로 스크롤 목록. 내용 RectTransform 을 돌려준다.</summary>
    private static RectTransform NewScroll(RectTransform page, float top)
    {
        var vp = NewRect("Viewport", page);
        vp.anchorMin = Vector2.zero; vp.anchorMax = Vector2.one;
        vp.offsetMin = new Vector2(20f, 20f); vp.offsetMax = new Vector2(-20f, -top);
        vp.gameObject.AddComponent<RectMask2D>();
        vp.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);   // 휠 스크롤을 받는 면
        var content = NewRect("Content", vp);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0f, 10f);
        var scroll = page.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = vp; scroll.content = content; scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40f;
        return content;
    }

    private void ShowTab(int k)
    {
        _tab = Mathf.Clamp(k, 0, _pages.Length - 1);
        for (int i = 0; i < _pages.Length; i++)
        {
            if (_pages[i] != null) _pages[i].SetActive(i == _tab);
            if (_tabBg[i] != null) _tabBg[i].color = i == _tab ? TabOn : TabOff;
        }
    }

    private void RefreshCurrent()
    {
        switch (_tab)
        {
            case 0: Refresh(); break;
            case 1: RefreshSafety(); break;
            case 2: RefreshTools(); break;
        }
    }

    // ══════════════════════════════════════════════════════
    // 탭 2: 안전·조립 상태
    // ══════════════════════════════════════════════════════
    private TMP_Text _safetyText;
    private RectTransform _safetyContent;
    private TMP_Text _logBtnText;
    private string _safetySig;

    private GameObject BuildSafetyPage(RectTransform panel)
    {
        var page = NewPage(panel, "SafetyPage");
        var sub = NewText(page, "고전압 차단 절차와 아직 볼트를 조이지 않은 부품을 한눈에 봅니다. 평가 때 확인하는 항목입니다.", 18f, TextAlignmentOptions.Left);
        sub.color = new Color(0.7f, 0.76f, 0.84f);
        Place(sub.rectTransform, new Vector2(28f, -4f), new Vector2(W - 300f, 28f));

        // 측정 기록 열기
        var b = NewRect("LogBtn", page);
        b.anchorMin = b.anchorMax = b.pivot = new Vector2(1f, 1f);
        b.anchoredPosition = new Vector2(-28f, 0f); b.sizeDelta = new Vector2(220f, 40f);
        var img = b.gameObject.AddComponent<Image>(); img.sprite = WorldHoverLabel.RoundedSprite(); img.type = Image.Type.Sliced; img.color = TabOn;
        b.gameObject.AddComponent<Button>().onClick.AddListener(() => { MeasurementLog.Toggle(); RefreshSafety(); });
        _logBtnText = NewText(b, "<b>측정 기록 보기</b>", 19f, TextAlignmentOptions.Center); Stretch(_logBtnText.rectTransform);

        _safetyContent = NewScroll(page, 56f);
        _safetyText = NewText(_safetyContent, "", 21f, TextAlignmentOptions.TopLeft);
        _safetyText.textWrappingMode = TextWrappingModes.Normal;
        var rt = _safetyText.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(12f, 0f); rt.offsetMax = new Vector2(-12f, 0f);
        rt.anchoredPosition = new Vector2(0f, -6f); rt.sizeDelta = new Vector2(-24f, 100f);
        page.gameObject.SetActive(false);
        return page.gameObject;
    }

    /// <summary>안전·조립 상태 본문(PC 탭·VR 탭 공용). maxPending: 조립 필요 목록을 몇 줄까지(VR 은 좁아서 줄인다).</summary>
    internal static string SafetyText(int maxPending)
    {
        var sb = new StringBuilder();

        // ── 고전압 차단 절차 ──
        var sm = ElectricSafetyManager.Instance;
        sb.Append("<size=110%><b>고전압 차단 절차</b></size>\n");
        if (sm == null) sb.Append("<color=#8C97A6>이 장면에는 고전압 안전 상태가 없습니다.</color>\n");
        else
        {
            bool gloves = sm.isGlovesEquipped, v12 = FreeLoto.Battery12VOff, msd = sm.isMSDRemoved, ver = HvVoltageCheck.Verified;
            float v = FreeLoto.VoltageNow();
            bool dis = msd && v <= FreeLoto.SafeV;
            string wait = !msd ? "서비스 플러그를 뽑은 뒤 약 3분" : dis ? "방전 완료" : $"방전 중 — 약 {Mathf.CeilToInt(FreeLoto.Tau * Mathf.Log(v / FreeLoto.SafeV))}초 남음";
            sb.Append(Line(1, "절연 장갑 착용", gloves));
            sb.Append(Line(2, "12V 배터리 (−) 단자 분리", v12));
            sb.Append(Line(3, "서비스 플러그(MSD) 분리", msd));
            sb.Append(Line(4, "방전 대기 · " + wait, dis));
            sb.Append(Line(5, "단자 잔류 전압 30V 이하 확인", ver));
            bool ready = gloves && msd && ver;
            sb.Append(ready ? "<color=#7CE3A0><b>고전압 부위 작업 가능</b></color>\n"
                            : "<color=#FF8A7A><b>고전압 부위(빨강)는 위 절차를 마친 뒤에만 만지세요</b></color>\n");
            int shock = FreeLoto.ShockCount;
            sb.Append(shock > 0 ? $"<color=#FF5555>감전 사고 {shock}회</color>\n" : "<color=#9BA6B4>감전 사고 없음</color>\n");
        }

        // ── 조립 상태 ──
        sb.Append("\n<size=110%><b>조립 상태</b></size>\n");
        var svc = CarPartService.Instance;
        var ts = TireService.Instance;
        int pending = 0, off = 0, stored = 0;
        var pendingSb = new StringBuilder();
        if (svc != null)
            for (int p = 0; p < CarPartService.Defs.Length; p++)
            {
                int st = svc.StateOf(p);
                if (st == (int)CarPartService.PartState.Off) { off++; if (svc.TrayHas(p)) stored++; }
                else if (st == (int)CarPartService.PartState.Mounted)
                {
                    if (++pending > maxPending) continue;
                    var d = CarPartService.Defs[p];
                    pendingSb.Append($"  · <b>{d.name}</b> — 볼트 조임 {svc.BoltsTightened(p)}/{d.bolts.Length} <color=#8FD3FF>({d.tool} · {d.tMin:0.0#}~{d.tMax:0.0#} kgf·m)</color>\n");
                }
            }
        if (ts != null)
            for (int w = 0; w < TireService.Wheels; w++)
            {
                if (ts.IsWheelOff(w)) { off++; if (ts.TrayHas(w)) stored++; }
                else if (ts.IsWheelMounted(w))
                {
                    if (++pending > maxPending) continue;
                    pendingSb.Append($"  · <b>{TireService.HubNames[w]}</b> — 너트 조임 {ts.NutsTightened(w)}/{TireService.NutsPerWheel} <color=#8FD3FF>({TireService.NutTorqueMin:0.#}~{TireService.NutTorqueMax:0.#} kgf·m)</color>\n");
                }
            }
        sb.Append($"떼어낸 부품 {off} (보관함 {stored}) · ");
        sb.Append(pending > 0 ? $"<color=#FFB347><b>조립 필요 {pending}</b></color>\n" : "<color=#7CE3A0>조립 필요 없음</color>\n");
        if (pending > 0)
        {
            sb.Append("<size=86%><color=#C9D1DC>끼우기만 하고 규정 토크까지 조이지 않은 부품입니다. 조인 볼트가 하나도 없으면 다시 뺄 수 있습니다.</color></size>\n");
            sb.Append(pendingSb);
            if (pending > maxPending) sb.Append($"  <color=#9BA6B4>… 외 {pending - maxPending}개</color>\n");
        }

        sb.Append("\n<size=110%><b>측정 기록</b></size>\n");
        sb.Append($"기록한 측정 {MeasurementLog.MeasuredCount}건\n");
        return sb.ToString();
    }

    private void RefreshSafety()
    {
        if (_safetyText == null) return;
        string s = SafetyText(99);
        if (_logBtnText != null) _logBtnText.text = MeasurementLog.Visible ? "<b>측정 기록 닫기</b>" : "<b>측정 기록 보기</b>";
        if (s == _safetySig) return;
        _safetySig = s;
        _safetyText.text = s;
        _safetyText.ForceMeshUpdate();
        float h = _safetyText.preferredHeight + 20f;
        _safetyText.rectTransform.sizeDelta = new Vector2(_safetyText.rectTransform.sizeDelta.x, h);
        _safetyContent.sizeDelta = new Vector2(0f, h + 12f);
    }

    private static string Line(int n, string text, bool done) =>
        done ? $"<color=#7CE3A0>{n}. {text} — 완료</color>\n" : $"<color=#FFC870>{n}. {text} — 아직</color>\n";

    // ══════════════════════════════════════════════════════
    // 탭 3: 특수공구 도감
    // ══════════════════════════════════════════════════════
    private class ToolInfo
    {
        public string id, name, partNo, use;
        public TMP_Text where;
        public Image btn;
    }

    // 매뉴얼 특수공구 표 + (P)HEV/EV 전용 장비 표 — 작업장(EquipmentBuilder)에 놓인 것
    internal static readonly string[][] ToolData =
    {
        new[] { "wheelsock", "타이어 휠 탈장착 소켓", "09529-N1000", "휠 너트 탈거·장착 — 휠 손상 방지 소켓" },
        new[] { "puller", "볼 조인트 풀러", "09568-GI000", "타이로드 엔드·텐션/레터럴 암 볼 조인트를 너클에서 분리 (분할 핀은 폐기)" },
        new[] { "strutnut", "스트럿 너트 공구", "0K546-F6100", "프런트 스트럿 인슐레이터 록 너트 풀기·조이기" },
        new[] { "shocknut", "쇽 록 너트 리무버", "09546-3X100", "리어 쇽 업소버 인슐레이터 록 너트 풀기·조이기" },
        new[] { "beam", "엔진 서포트 지지대(빔)", "09200-3N000", "프런트 서브 프레임 탈거·장착 때 구동계를 위에서 받침" },
        new[] { "hanger", "고전압 배터리 이송 행어", "09375-K4400", "배터리 시스템 어셈블리를 크레인 자키로 들어 옮길 때" },
        new[] { "hangerknob", "이송 행어 고정 핸들", "09375-K4104", "이송 행어를 배터리 팩에 고정" },
        new[] { "guidepin", "배터리 장착 가이드 핀", "09375-BF000", "배터리 팩을 차체에 올릴 때 장착 위치 맞춤" },
        new[] { "modhanger", "배터리 모듈 행어 세트", "09375-BF300", "배터리 모듈을 케이스에서 들어 올림" },
        new[] { "modguide", "배터리 모듈 가이드", "09375-GI900", "모듈 장착 때 위치 안내" },
        new[] { "pressjig", "배터리 모듈 면압 지그", "09375-GI800", "모듈 장착 때 눌러 고정(면압)" },
        new[] { "gapframe", "갭 필러 고정 틀", "09375-GI100", "모듈 아래 갭 필러(방열재) 재도포 — 틀 고정" },
        new[] { "gapgun", "갭 필러 작업 건", "09375-GI200", "갭 필러 도포" },
        new[] { "gapribbon", "갭 필러 리본", "09375-GI300", "도포한 갭 필러 평탄화" },
        new[] { "gaptube", "갭 필러 도포 튜브", "09375-GI240", "갭 필러 재료" },
        new[] { "balancer", "모듈 충·방전 장비", "xMB-9640", "교체 모듈 전압을 기존 모듈과 맞춤" },
        new[] { "helium", "헬륨 가스·압력 조절기", "-", "배터리 팩 기밀(누설) 점검 — 헬륨 주입" },
        new[] { "heliumdet", "헬륨 가스 감지기", "-", "기밀 점검 — 새는 곳 찾기" },
        new[] { "ventjig_top", "상부 벤팅 밸브 막음 지그", "-", "기밀 점검 때 상부 벤팅 밸브 막음" },
        new[] { "ventjig_side", "측면 벤팅 밸브 막음 지그", "-", "기밀 점검 때 측면 벤팅 밸브 막음" },
    };

    private readonly List<ToolInfo> _tools = new List<ToolInfo>();
    private const float ToolRowH = 76f;

    private GameObject BuildToolsPage(RectTransform panel)
    {
        var page = NewPage(panel, "ToolsPage");
        var sub = NewText(page, "정비 매뉴얼 특수공구 표의 공구입니다. '위치 표시'를 누르면 작업장의 그 공구 위에 빛기둥이 섭니다.", 18f, TextAlignmentOptions.Left);
        sub.color = new Color(0.7f, 0.76f, 0.84f);
        Place(sub.rectTransform, new Vector2(28f, -4f), new Vector2(W - 60f, 28f));
        var content = NewScroll(page, 44f);

        for (int i = 0; i < ToolData.Length; i++)
        {
            var d = ToolData[i];
            var info = new ToolInfo { id = d[0], name = d[1], partNo = d[2], use = d[3] };
            var row = NewRect("Tool_" + info.id, content);
            row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f); row.pivot = new Vector2(0.5f, 1f);
            row.anchoredPosition = new Vector2(0f, -i * ToolRowH); row.sizeDelta = new Vector2(0f, ToolRowH - 6f);
            var rbg = row.gameObject.AddComponent<Image>(); rbg.sprite = WorldHoverLabel.RoundedSprite(); rbg.type = Image.Type.Sliced;
            rbg.color = new Color(0.11f, 0.13f, 0.17f, 1f);

            string no = info.partNo == "-" ? "" : $" <color=#8C97A6>{info.partNo}</color>";
            var name = NewText(row, $"<b>{info.name}</b>{no}\n<size=76%><color=#8FD3FF>{info.use}</color></size>", 21f, TextAlignmentOptions.Left);
            name.overflowMode = TextOverflowModes.Ellipsis;
            name.rectTransform.anchorMin = name.rectTransform.anchorMax = new Vector2(0f, 0.5f); name.rectTransform.pivot = new Vector2(0f, 0.5f);
            name.rectTransform.anchoredPosition = new Vector2(18f, 0f); name.rectTransform.sizeDelta = new Vector2(W - 380f, ToolRowH - 8f);

            info.where = NewText(row, "", 18f, TextAlignmentOptions.Left);
            info.where.rectTransform.anchorMin = info.where.rectTransform.anchorMax = new Vector2(1f, 0.5f); info.where.rectTransform.pivot = new Vector2(0f, 0.5f);
            info.where.rectTransform.anchoredPosition = new Vector2(-310f, 0f); info.where.rectTransform.sizeDelta = new Vector2(150f, ToolRowH - 8f);

            var b = NewRect("Ping", row);
            b.anchorMin = b.anchorMax = b.pivot = new Vector2(1f, 0.5f);
            b.anchoredPosition = new Vector2(-14f, 0f); b.sizeDelta = new Vector2(140f, 44f);
            info.btn = b.gameObject.AddComponent<Image>(); info.btn.sprite = WorldHoverLabel.RoundedSprite(); info.btn.type = Image.Type.Sliced;
            info.btn.color = TabOn;
            var captured = info;
            b.gameObject.AddComponent<Button>().onClick.AddListener(() => Ping(captured));
            var bt = NewText(b, "<b>위치 표시</b>", 19f, TextAlignmentOptions.Center); Stretch(bt.rectTransform);
            _tools.Add(info);
        }
        content.sizeDelta = new Vector2(0f, ToolData.Length * ToolRowH + 4f);
        page.gameObject.SetActive(false);
        return page.gameObject;
    }

    private void RefreshTools()
    {
        var cam = Camera.main;
        foreach (var t in _tools)
        {
            var near = NearestTool(t.id, cam != null ? cam.transform.position : Vector3.zero, out int count, out float dist);
            string w = near == null ? "<color=#8C97A6>작업장에 없음</color>"
                     : $"<color=#C9D1DC>{count}개 · {dist:0.#} m</color>";
            if (t.where.text != w) t.where.text = w;
            t.btn.color = near != null ? TabOn : new Color(0.2f, 0.22f, 0.26f, 1f);
        }
    }

    internal static SpecialTool NearestTool(string id, Vector3 from, out int count, out float dist)
    {
        count = 0; dist = 0f;
        SpecialTool best = null; float bd = float.MaxValue;
        foreach (var t in SpecialTool.All)
        {
            if (t == null || t.toolId != id || !t.isActiveAndEnabled) continue;
            count++;
            float d = (t.transform.position - from).sqrMagnitude;
            if (d < bd) { bd = d; best = t; }
        }
        if (best != null) dist = Mathf.Sqrt(bd);
        return best;
    }

    private void Ping(ToolInfo info)
    {
        var cam = Camera.main;
        var t = NearestTool(info.id, cam != null ? cam.transform.position : Vector3.zero, out _, out float dist);
        if (t == null)
        {
            EquipFx.Notice("tool_ping", $"{info.name} — 이 작업장에 없습니다", "다른 작업(시나리오)에서 쓰는 공구입니다", EquipFx.Warn, 2.5f, 0.5f);
            return;
        }
        SetOpen(false);   // 창을 닫아야 빛기둥이 보인다
        ShowBeacon(t.transform);
        EquipFx.Notice("tool_ping", $"{info.name} — {dist:0.#} m", "빛기둥이 선 곳에 있습니다", EquipFx.Info, 3f, 0.5f);
    }

    /// <summary>공구 위에 8초 동안 주황 빛기둥을 세운다(내 화면에만, VR 탭도 쓴다).</summary>
    internal static void ShowBeacon(Transform target) => ToolBeacon.Show(target);

    /// <summary>공구 위에 8초 동안 서는 주황 빛기둥(내 화면에만).</summary>
    private class ToolBeacon : MonoBehaviour
    {
        private Transform _target;
        private float _until;
        private LineRenderer _lr;

        public static void Show(Transform target)
        {
            var go = new GameObject("[ToolBeacon]") { layer = 1 };
            var b = go.AddComponent<ToolBeacon>();
            b._target = target;
            b._until = Time.unscaledTime + 8f;
            b._lr = go.AddComponent<LineRenderer>();
            b._lr.positionCount = 2;
            b._lr.useWorldSpace = true;
            b._lr.widthMultiplier = 0.06f;
            b._lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b._lr.receiveShadows = false;
            var mat = new Material(Shader.Find("Sprites/Default"));
            b._lr.material = mat;
            b._lr.startColor = new Color(1f, 0.6f, 0.1f, 0.95f);
            b._lr.endColor = new Color(1f, 0.6f, 0.1f, 0f);
        }

        private void LateUpdate()
        {
            if (_target == null || Time.unscaledTime > _until) { Destroy(gameObject); return; }
            Vector3 p = _target.position;
            float pulse = 0.05f + 0.03f * Mathf.Sin(Time.unscaledTime * 6f);
            _lr.widthMultiplier = pulse;
            _lr.SetPosition(0, p);
            _lr.SetPosition(1, p + Vector3.up * 2.6f);
        }

        private void OnDestroy()
        {
            if (_lr != null && _lr.material != null) Destroy(_lr.material);
        }
    }
}
