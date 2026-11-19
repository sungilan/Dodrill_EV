using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XRAirpotrSecurity;

// ============================================================
//  PcHudLayout.cs
//
//  (11/19) PC 화면 배치 정리 — 씬(Game.unity)에 놓인 UI 를 런타임에 손본다. 씬 파일은 건드리지 않는다.
//   · 오른쪽 위 메뉴 버튼 줄(CNV_Controller_PC)을 20% 줄이고, 오른쪽 위 모서리는 그대로 둔다.
//     → 화면 위쪽 가운데에 고전압 상태·알림이 들어갈 자리가 생긴다.
//   · 메뉴 줄에 '부위 목록'·'환경설정' 버튼을 더 넣는다(점검표 버튼을 복제, 기존 기능 연결은 떼어냄).
//   · 미니맵·키보드 안내는 평소 숨기고, 단계 안내 패널 아래 동그란 작은 버튼으로 켜고 끈다(설정은 기기에 저장).
//     기존 옆으로 밀어 접는 탭(◀)은 숨긴다.
//     끄는 것은 SetActive 가 아니라 CanvasGroup(투명·클릭 통과)로 한다 — 미니맵 아이콘(MiniMapComponent)이 켜질 때
//     '켜져 있는' MiniMapController 를 찾아 등록하는데, 꺼 두면 등록을 못 해 지도를 켜도 아이콘이 없었다.
//   · 씬을 불러오는 동안 그리는 카메라가 하나도 없으면 화면이 지워지지 않아 이전 화면(옛 UI)이 겹쳐 남았다
//     → 그때만 검은색으로 지우는 빈 카메라를 켠다.
//   · 씬이 뜨자마자 바로 손본다(1초마다 찾으면 처음 한순간 옛 배치가 보였다).
//  아이콘 그림: Resources/HudIcons/ 에 parts.png · settings.png · minimap.png · keyboard.png 를 Sprite 로 넣으면 그걸 쓰고,
//  없으면 글자(부위·설정·지도·키)로 대신한다.
//  모바일(Mobile_UI)도 같은 규칙으로 손본다 — 메뉴 줄 CNV_Controller_Mobile, 단계 패널 TaskProgressUI_Mobile,
//  미니맵 CanvasMiniMap_PC_01. 모바일은 손가락으로 누르므로 동그란 버튼을 더 크게(64) 만든다.
//  VR 에서는 아무것도 하지 않는다.
// ============================================================
public class PcHudLayout : MonoBehaviour
{
    private const float MenuScale = 0.8f;
    private const float RoundPc = 50f, RoundMobile = 64f;   // 동그란 버튼 지름(모바일은 손가락 크기)
    private float Round => _mobile ? RoundMobile : RoundPc;
    private bool _mobile;
    private const float RoundY = -212f;                  // 단계 안내 패널 아래, 미니맵 위(TaskProgressUI_PC 기준)
    private const string PrefMap = "hud.minimap", PrefKeys = "hud.keyboard";

    private float _nextCheck;
    private RectTransform _menu;                         // 손본 메뉴 줄(씬이 다시 불리면 새 것으로 바뀐다)
    private RectTransform _task;
    private GameObject _map, _keys;
    private CanvasGroup _mapGroup, _keysGroup;
    private Camera _clearCam;
    private bool _mapOn, _keysOn;
    private Image _mapRing, _keysRing;

    private static Sprite s_disc, s_ring;
    private static PcHudLayout s_inst;

    /// <summary>미니맵 보이기(환경설정 창·동그란 버튼). 저장된다.</summary>
    public static bool MapOn
    {
        get => s_inst != null ? s_inst._mapOn : PlayerPrefs.GetInt(PrefMap, 0) == 1;
        set { if (s_inst != null) { s_inst._mapOn = value; s_inst.Save(); } else PlayerPrefs.SetInt(PrefMap, value ? 1 : 0); }
    }

    /// <summary>키보드 안내 보이기(PC). 저장된다.</summary>
    public static bool KeysOn
    {
        get => s_inst != null ? s_inst._keysOn : PlayerPrefs.GetInt(PrefKeys, 0) == 1;
        set { if (s_inst != null) { s_inst._keysOn = value; s_inst.Save(); } else PlayerPrefs.SetInt(PrefKeys, value ? 1 : 0); }
    }

    private void Awake()
    {
        s_inst = this;
        _mapOn = PlayerPrefs.GetInt(PrefMap, 0) == 1;
        _keysOn = PlayerPrefs.GetInt(PrefKeys, 0) == 1;
    }
    private static readonly Color OnColor = new Color(0.30f, 0.80f, 1f, 1f);
    private static readonly Color OffColor = new Color(0.55f, 0.60f, 0.66f, 0.9f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isBatchMode || FindAnyObjectByType<PcHudLayout>() != null) return;
        var go = new GameObject("[PcHudLayout]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<PcHudLayout>();
    }

    private void Update()
    {
        bool vr = Utils.GetPlatformType() == PlatformType.VR;
        if (!vr) UpdateClearCamera();
        if (Time.unscaledTime >= _nextCheck)
        {
            // 아직 못 찾았으면 매 프레임 가깝게, 다 찾았으면 1초마다(씬이 다시 불리면 새 것을 찾는다)
            bool found = _menu != null && _task != null;
            _nextCheck = Time.unscaledTime + (found ? 1f : 0.05f);
            if (vr) return;
            if (_menu == null) TryMenu();
            if (_task == null) TryTaskPanel();
        }
        Apply(_mapGroup, _mapOn);
        Apply(_keysGroup, _keysOn);
    }

    private static void Apply(CanvasGroup g, bool on)
    {
        if (g == null) return;
        float a = on ? 1f : 0f;
        if (g.alpha != a) { g.alpha = a; g.interactable = on; g.blocksRaycasts = on; }
    }

    // 그리는 카메라가 하나도 없을 때(씬 불러오는 중)만 켜서 화면을 검게 지운다
    private void UpdateClearCamera()
    {
        if (_clearCam == null)
        {
            var go = new GameObject("[ClearCamera]");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            _clearCam = go.AddComponent<Camera>();
            _clearCam.clearFlags = CameraClearFlags.SolidColor;
            _clearCam.backgroundColor = Color.black;
            _clearCam.cullingMask = 0;
            _clearCam.depth = -100f;
            _clearCam.enabled = false;
        }
        int others = Camera.allCamerasCount - (_clearCam.enabled ? 1 : 0);
        bool need = others == 0;
        if (_clearCam.enabled != need) _clearCam.enabled = need;
    }

    private void OnDestroy() { if (_clearCam != null) Destroy(_clearCam.gameObject); }

    // ── 오른쪽 위 메뉴 줄 ──
    private void TryMenu()
    {
        var go = GameObject.Find("CNV_Controller_PC");
        if (go == null) go = GameObject.Find("CNV_Controller_Mobile");
        if (go == null) return;
        var rt = (RectTransform)go.transform;
        _menu = rt;

        // 오른쪽 위 모서리를 고정점으로 바꾼 뒤 줄인다(가운데 기준으로 줄이면 모서리에서 떨어져 보인다)
        Vector2 newPivot = new Vector2(1f, 1f);
        rt.anchoredPosition += Vector2.Scale(newPivot - rt.pivot, rt.rect.size);
        rt.pivot = newPivot;

        // 부위 목록은 점검표 바로 왼쪽, 환경설정(톱니)은 종료(전원) 바로 왼쪽
        AddMenuButton(rt, "HLD_PartList", "HLD_CheckList", "parts", "부위", CarPartListPanel.Toggle);
        AddMenuButton(rt, "HLD_Settings", "HLD_ExitBtnHolder", "settings", "설정", SettingsPanel.Toggle);
        rt.localScale = Vector3.one * MenuScale;
    }

    /// <summary>
    /// 점검표 버튼을 복제해 메뉴 줄에 버튼을 하나 더 넣는다(모양·크기를 기존 버튼과 맞추려고). 복제본의 기존 기능 연결은 모두 떼어낸다.
    /// before = 이 버튼 바로 왼쪽에 넣는다(정렬 그룹이 있는 PC 메뉴). 모바일 메뉴는 맨 왼쪽에 이어 붙인다.
    /// 아이콘: Resources/HudIcons/{iconRes}.png 가 있으면 그 그림, 없으면 fallback 글자.
    /// </summary>
    private void AddMenuButton(RectTransform menu, string name, string beforeName, string iconRes, string fallback, UnityEngine.Events.UnityAction onClick)
    {
        var src = menu.Find("HLD_CheckList");
        if (src == null || menu.Find(name) != null) return;
        var before = menu.Find(beforeName);
        float each = menu.childCount > 0 ? menu.rect.width / menu.childCount : 110f;
        bool layout = menu.GetComponent<HorizontalLayoutGroup>() != null;
        // 모바일 메뉴 줄은 정렬 그룹 없이 버튼마다 자리(x)가 박혀 있다 → 맨 왼쪽 버튼보다 한 칸(간격) 더 왼쪽에 둔다
        float minX = float.MaxValue, step = 132f;
        if (!layout)
        {
            float prev = float.NaN;
            var xs = new System.Collections.Generic.List<float>();
            for (int i = 0; i < menu.childCount; i++) xs.Add(((RectTransform)menu.GetChild(i)).anchoredPosition.x);
            xs.Sort();
            foreach (var x in xs) { if (!float.IsNaN(prev) && x - prev > 1f) step = Mathf.Min(step, x - prev); prev = x; }
            if (xs.Count > 0) minX = xs[0];
        }

        // 꺼진 임시 부모 밑에 복제 → 복제본의 스크립트가 깨어나기(Awake·OnEnable) 전에 떼어낸다
        var holder = new GameObject("tmp");
        holder.SetActive(false);
        var clone = Instantiate(src.gameObject, holder.transform);
        clone.name = name;
        foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            string ns = mb.GetType().Namespace ?? "";
            if (ns.StartsWith("UnityEngine") || ns.StartsWith("TMPro")) continue;
            DestroyImmediate(mb);
        }
        var btn = clone.GetComponentInChildren<Button>(true);
        if (btn != null)
        {
            btn.onClick = new Button.ButtonClickedEvent();   // 씬에 저장된 점검표 열기 연결을 지운다
            btn.onClick.AddListener(onClick);
        }
        var icon = FindDeep(clone.transform, "IMG_Icon");
        var sprite = Resources.Load<Sprite>("HudIcons/" + iconRes);
        if (icon != null)
        {
            var img = icon.GetComponent<Image>();
            if (sprite != null && img != null)
            {
                // 복제 원본(점검표)의 아이콘 칸은 120×120 에 살짝 비껴 있다 → 다른 버튼(집·소리·종료)과 같은 100×100 가운데로
                var irt = (RectTransform)icon;
                irt.anchoredPosition = Vector2.zero; irt.sizeDelta = new Vector2(100f, 100f);
                img.sprite = sprite; img.color = Color.white; img.preserveAspect = true;
            }
            else
            {
                if (img != null) img.enabled = false;
                var t = NewText(icon, fallback, 34f);
                ((RectTransform)t.transform).sizeDelta = new Vector2(100f, 60f);
            }
        }
        clone.transform.SetParent(menu, false);
        Destroy(holder);
        clone.SetActive(true);
        if (layout)
        {
            clone.transform.SetSiblingIndex((before != null ? before : src).GetSiblingIndex());
            menu.sizeDelta += new Vector2(each, 0f);                  // 오른쪽 위 고정이라 왼쪽으로 한 칸 늘어난다
        }
        else
        {
            var crt = (RectTransform)clone.transform;
            crt.anchoredPosition = new Vector2(minX - step, ((RectTransform)src).anchoredPosition.y);
        }
    }

    // ── 미니맵·키보드 안내 ──
    private void TryTaskPanel()
    {
        var go = GameObject.Find("TaskProgressUI_PC");
        _mobile = false;
        if (go == null) { go = GameObject.Find("TaskProgressUI_Mobile"); _mobile = go != null; }
        if (go == null) return;
        _task = (RectTransform)go.transform;
        // PC 는 단계 패널 아래에, 모바일은 단계 패널과 나란히(같은 부모) 미니맵이 있다
        var map = _mobile ? (_task.parent != null ? _task.parent.Find("CanvasMiniMap_PC_01") : null) : _task.Find("CanvasMiniMap_PC");
        var keys = _mobile ? null : _task.Find("KeyBoardPanel");
        if (_mobile && map != null)
        {
            // 큰 동그란 버튼이 들어갈 자리만큼 미니맵을 아래로
            ((RectTransform)map).anchoredPosition += new Vector2(0f, -30f);   // 씬을 불러올 때마다 새 패널이라 한 번만 적용된다
        }
        _map = map != null ? map.gameObject : null;
        _keys = keys != null ? keys.gameObject : null;
        HideChild(map, "TogglePanel");
        HideChild(keys, "TogglePanel");
        _mapOn = PlayerPrefs.GetInt(PrefMap, 0) == 1;
        _keysOn = PlayerPrefs.GetInt(PrefKeys, 0) == 1;
        _mapGroup = GroupOf(_map);
        _keysGroup = GroupOf(_keys);
        if (_mapGroup != null) _mapGroup.alpha = -1f;    // 처음 한 번은 반드시 적용되게
        if (_keysGroup != null) _keysGroup.alpha = -1f;

        if (s_disc == null) s_disc = HvStatusHud.MakeCircle(false);
        if (s_ring == null) s_ring = HvStatusHud.MakeCircle(true);
        if (_map != null) _mapRing = NewRound(_task, "Round_Minimap", 0f, "minimap", "지도", () => { _mapOn = !_mapOn; Save(); });
        if (_keys != null) _keysRing = NewRound(_task, "Round_Keyboard", Round + 10f, "keyboard", "키", () => { _keysOn = !_keysOn; Save(); });
        Save();
    }

    private void Save()
    {
        PlayerPrefs.SetInt(PrefMap, _mapOn ? 1 : 0);
        PlayerPrefs.SetInt(PrefKeys, _keysOn ? 1 : 0);
        if (_mapRing != null) _mapRing.color = _mapOn ? OnColor : OffColor;
        if (_keysRing != null) _keysRing.color = _keysOn ? OnColor : OffColor;
    }

    private Image NewRound(Transform parent, string name, float x, string iconRes, string fallback, UnityEngine.Events.UnityAction onClick)
    {
        var old = parent.Find(name);
        if (old != null) Destroy(old.gameObject);

        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, RoundY); rt.sizeDelta = new Vector2(Round, Round);
        var disc = rt.gameObject.AddComponent<Image>();
        disc.sprite = s_disc; disc.color = new Color(0.07f, 0.10f, 0.15f, 0.92f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = disc;
        var cb = btn.colors; cb.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f); btn.colors = cb;
        btn.onClick.AddListener(onClick);

        var ring = NewRect("Ring", rt);
        ring.anchorMin = Vector2.zero; ring.anchorMax = Vector2.one; ring.offsetMin = ring.offsetMax = Vector2.zero;
        var ringImg = ring.gameObject.AddComponent<Image>();
        ringImg.sprite = s_ring; ringImg.raycastTarget = false;

        var sprite = Resources.Load<Sprite>("HudIcons/" + iconRes);
        if (sprite != null)
        {
            var ic = NewRect("Icon", rt);
            ic.anchorMin = ic.anchorMax = new Vector2(0.5f, 0.5f); ic.sizeDelta = new Vector2(Round * 0.56f, Round * 0.56f);
            var img = ic.gameObject.AddComponent<Image>();
            img.sprite = sprite; img.preserveAspect = true; img.raycastTarget = false;
        }
        else NewText(rt, fallback, 19f);
        return ringImg;
    }

    // ── 유틸 ──
    private static CanvasGroup GroupOf(GameObject go)
    {
        if (go == null) return null;
        var g = go.GetComponent<CanvasGroup>();
        return g != null ? g : go.AddComponent<CanvasGroup>();
    }

    private static void HideChild(Transform t, string child)
    {
        if (t == null) return;
        var c = t.Find(child);
        if (c != null) c.gameObject.SetActive(false);
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var r = FindDeep(t.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    private static TMP_Text NewText(Transform parent, string text, float size)
    {
        var rt = NewRect("Label", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(80f, 40f);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var kf = MobileWorkButton.KoreanFont();
        if (kf != null) t.font = kf;
        t.text = $"<b>{text}</b>"; t.fontSize = size; t.color = Color.white;
        t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
        return t;
    }
}
