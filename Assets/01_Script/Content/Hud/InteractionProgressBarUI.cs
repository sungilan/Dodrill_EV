using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

public class InteractionProgressBarUI : MonoBehaviour
{
    public static InteractionProgressBarUI Instance;

    [Header("볼트 가이드용 패널")]
    public CanvasGroup boltPanelGroup;
    public Slider boltProgressBar;
    public TextMeshProUGUI boltTaskText;
    public TextMeshProUGUI boltTargetText;
    public TextMeshProUGUI boltInstructionText;

    [Header("방전 타이머용 패널")]
    public CanvasGroup timerPanelGroup;
    public TextMeshProUGUI timerText;

    private void Awake()
    {
        Instance = this;
        // 문구가 길어져도 패널 밖으로 튀어나오지 않도록 자동 크기 조절
        FitText(boltTaskText, 0.6f);
        FitText(boltTargetText, 0.6f);
        FitText(boltInstructionText, 0.6f);
        FitText(timerText, 0.6f);
        // 초기화: 둘 다 숨김
        if(boltPanelGroup != null) boltPanelGroup.alpha = 0;
        //if(timerPanelGroup != null) timerPanelGroup.alpha = 0;
    }
    // ShowBoltProgress는 사용 중인 도구가 매 프레임 호출한다. 호출이 끊기면(도구 비활성화·볼트 제거·단계 전환 등)
    // 호출자의 정리 코드와 무관하게 패널이 스스로 사라지도록 하는 안전장치.
    private const float HideAfterSilence = 0.4f;
    private float _lastShowTime;
    private bool _boltVisible;

    private void Update()
    {
        if(_boltVisible && Time.unscaledTime - _lastShowTime > HideAfterSilence)
            HideBoltProgress();
    }

    private static void FitText(TextMeshProUGUI tmp, float minRatio)
    {
        if(tmp == null) return;
        float max = tmp.fontSize;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMax = max;
        tmp.fontSizeMin = Mathf.Max(8f, max * minRatio);
        tmp.overflowMode = TextOverflowModes.Ellipsis;
    }


    // --- 볼트 가이드 제어 ---
    public void ShowBoltProgress(float value, string taskName, string objName, string instruction = "")
    {
        _lastShowTime = Time.unscaledTime;
        _boltVisible = true;

        // (11/19) 가운데 큰 패널(860×560) 대신 감전 위험 게이지와 같은 자리·모양의 얇은 막대 — PC·모바일은 화면에, VR 은 시선 앞 월드 캔버스에(VrUi.NewHudCanvas).
        // 감전 위험 게이지가 함께 떠 있으면 그 위로 올라간다. 예전 VR 패널(boltPanelGroup)은 쓰지 않는다.
        if(boltPanelGroup != null) { boltPanelGroup.DOKill(); boltPanelGroup.alpha = 0f; }
        float r = value;
        if(boltProgressBar != null && boltProgressBar.maxValue > boltProgressBar.minValue)
            r = Mathf.InverseLerp(boltProgressBar.minValue, boltProgressBar.maxValue, value);
        ShowCompact(Mathf.Clamp01(r), taskName, objName, instruction);
    }

    public void HideBoltProgress()
    {
        _boltVisible = false;
        if(_compact != null && _compact.activeSelf) _compact.SetActive(false);
        if(boltPanelGroup == null || boltPanelGroup.alpha <= 0) return;

        boltPanelGroup.DOKill();
        boltPanelGroup.DOFade(0f, 0.2f).SetEase(Ease.InQuad);
    }

    // --- 방전 타이머 제어 ---
    public void ShowTimer(string timeStr)
    {
        if(timerPanelGroup == null) return;
        if(timerPanelGroup.alpha < 0.9f && !DOTween.IsTweening(timerPanelGroup))
        {
            timerPanelGroup.DOKill();
            timerPanelGroup.DOFade(1f, 0.3f);
        }
        if(timerText != null) timerText.text = timeStr;
    }

    public void HideTimer()
    {
        if(timerPanelGroup == null) return;
        timerPanelGroup.DOKill();
        timerPanelGroup.DOFade(0f, 0.3f);
    }

    // --- PC 용 얇은 진행 막대 ---
    private GameObject _compact;
    private RectTransform _compactBack;
    private Image _compactFill;
    private TextMeshProUGUI _compactLabel, _compactSub;

    private void ShowCompact(float r, string taskName, string objName, string instruction)
    {
        if(_compact == null) BuildCompact();
        if(!_compact.activeSelf) _compact.SetActive(true);
        // 감전 위험 게이지가 같이 뜨면 그 '위'로 올라간다 — 아래로 내리면 선임 정비사 대화창(화면 아래)을 가렸다(11/19).
        // 위로 올릴 때는 안내 한 줄을 빼서 조준점(화면 가운데)까지 내려오지 않게 한다.
        bool both = HvShockGauge.Visible || FreeDischargeHud.Visible;
        _compactBack.anchoredPosition = new Vector2(0f, both ? -92f : -170f);
        _compactLabel.rectTransform.anchoredPosition = new Vector2(0f, both ? 30f : 38f);
        if (_compactSub.gameObject.activeSelf == both) _compactSub.gameObject.SetActive(!both);
        _compactFill.rectTransform.anchorMax = new Vector2(r, 1f);
        // 부품 이름이 한글일 때만 붙인다 — 'PartBolt RearUnderCover-1' 같은 내부 이름은 보이지 않게(작업명에 이미 부위 이름이 있다)
        string target = HasHangul(objName) && (taskName == null || !taskName.Contains(objName)) ? $" — {objName}" : "";
        _compactLabel.text = $"<b>{taskName}{target}</b>  <color=#9FE3FF>{Mathf.RoundToInt(r * 100f)}%</color>";
        string sub = instruction ?? "";
        // 모바일에는 F 키가 없다 — 오른쪽 아래 작업 버튼을 가리키게
        if (GameScenePlatformManager.CurrentPlatform == XRAirpotrSecurity.PlatformType.Mobile)
            sub = sub.Replace("F 키를", "작업 버튼을").Replace("F키를", "작업 버튼을").Replace("(F)", "(작업 버튼)");
        _compactSub.text = sub;
    }

    private static bool HasHangul(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char ch in s) if (ch >= '가' && ch <= '힣') return true;
        return false;
    }

    private void BuildCompact()
    {
        _compact = VrUi.NewHudCanvas("[BoltProgress_Canvas]", 69).gameObject;

        _compactBack = NewRect("Back", _compact.transform);
        _compactBack.anchorMin = _compactBack.anchorMax = new Vector2(0.5f, 0.5f); _compactBack.pivot = new Vector2(0.5f, 0.5f);
        _compactBack.sizeDelta = new Vector2(620f, 34f);
        var back = _compactBack.gameObject.AddComponent<Image>(); back.raycastTarget = false;
        back.color = new Color(0.03f, 0.07f, 0.12f, 0.85f);

        var fill = NewRect("Fill", _compactBack);
        fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f); fill.pivot = new Vector2(0f, 0.5f);
        fill.offsetMin = new Vector2(3f, 3f); fill.offsetMax = new Vector2(-3f, -3f);
        _compactFill = fill.gameObject.AddComponent<Image>(); _compactFill.raycastTarget = false;
        _compactFill.color = new Color(0.25f, 0.75f, 1f, 0.95f);

        _compactLabel = NewText(_compactBack, "Label", 24f, new Vector2(0f, 38f), Color.white);
        _compactSub = NewText(_compactBack, "Sub", 19f, new Vector2(0f, -34f), new Color(0.8f, 0.9f, 1f));
        VrUi.Finish(_compact);   // VR: 맨 앞에 그리기
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    private TextMeshProUGUI NewText(Transform parent, string name, float size, Vector2 pos, Color color)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(900f, 36f);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var f = boltTaskText != null ? boltTaskText.font : MobileWorkButton.KoreanFont();
        if(f != null) t.font = f;
        t.fontSize = size; t.color = color; t.alignment = TextAlignmentOptions.Center;
        t.richText = true; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
        t.outlineWidth = 0.22f; t.outlineColor = new Color32(0, 0, 0, 220);
        return t;
    }

    private void OnDestroy()
    {
        if(_compact != null) Destroy(_compact);
    }
}
