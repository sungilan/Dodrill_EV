using System;
using MasterServerToolkit.MasterServer;
using MasterServerToolkit.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.UI;

public class StartPanel : UIView
{
    [SerializeField] private GameObject clientManager; // S_ClientManager

    protected override void Awake()
    {
        base.Awake();
        Mst.Events.AddListener(MstEventKeys.showStartPanel, (_) => Show());
        Mst.Events.AddListener(MstEventKeys.hideStartPanel, (_) => Hide());
    }

    private void Start()
    {
        AddSoloButton();
        // (11/20) '혼자 하기' 아래의 시나리오 전환 버튼(▶ [P0AA6] …)은 뺀다 — 혼자 하기는 SoloModeLauncher.ScenarioId 기본 시나리오로 시작한다.
        // 다시 쓰려면 ShowSoloScenarioButton 을 true 로.
        if (ShowSoloScenarioButton) AddSoloScenarioButton();
    }

    // StartButton OnClick에 연결
    public void OnStartButtonClick()
    {
        // 프리팹에는 씬 오브젝트(S_ClientManager)를 담을 수 없어 비어 있을 수 있다 — 꺼져 있는 MST 클라이언트(AuthBehaviour 가 붙은 오브젝트)를 찾아 켠다
        if (clientManager == null)
        {
            var auth = FindFirstObjectByType<MasterServerToolkit.Bridges.AuthBehaviour>(FindObjectsInactive.Include);
            if (auth != null) clientManager = auth.gameObject;
        }
        if (clientManager != null) clientManager.SetActive(true);
        else Debug.LogError("[StartPanel] MST 클라이언트(AuthBehaviour) 오브젝트를 찾지 못했습니다 — 씬에 S_ClientManager 가 있어야 합니다");
        Mst.Events.Invoke(MstEventKeys.hideLanguageToggle);
        Mst.Events.Invoke(MstEventKeys.hideLanguageSelection);
        Mst.Events.Invoke(MstEventKeys.hideStartPanel);
        Mst.Events.Invoke(MstEventKeys.showSignInView);
    }

    // QuitButton OnClick에 연결
    public void OnQuitButtonClick()
    {
        Mst.Events.Invoke(MstEventKeys.showExitPopup);

    }

    // ── 혼자 하기 ─────────────────────────────────────────────
    // 방 만들기·로그인 없이, 인터넷 없이 이 기기에서 바로 훈련을 시작한다. (SoloModeLauncher)

    private const string SoloButtonName = "BTN_Solo";
    private const string SoloLabel = "혼자 하기";

    /// <summary>
    /// '시작' 버튼을 복제해 '혼자 하기' 버튼을 만든다(씬 파일을 건드리지 않고 같은 모양을 유지).
    /// 순서: 시작 - 혼자 하기 - 종료.
    /// </summary>
    private void AddSoloButton()
    {
        try
        {
            if (transform.Find(SoloButtonName) != null) return;
            var start = transform.Find("BTN_Start");
            if (start == null) return;
            var parent = start.parent;

            // 간격(GAP)도 하나 더 복제해 버튼 사이 간격을 맞춘다
            Transform gap = null;
            foreach (Transform c in parent)
                if (c.name == "GAP") { gap = c; break; }

            var solo = Instantiate(start.gameObject, parent, false);
            solo.name = SoloButtonName;

            var button = solo.GetComponent<Button>();
            if (button != null)
            {
                // 복제된 버튼에는 '시작'의 인스펙터 연결(영구 리스너)이 따라오므로 새 이벤트로 통째로 바꾼다
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(OnSoloButtonClick);
            }

            // 글자: 지역화 컴포넌트가 '시작'으로 되돌리지 않게 제거하고 직접 쓴다
            foreach (var loc in solo.GetComponentsInChildren<LocalizeStringEvent>(true))
                DestroyImmediate(loc);
            var label = solo.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = SoloLabel;

            int idx = start.GetSiblingIndex();
            solo.transform.SetSiblingIndex(idx + 2);
            if (gap != null)
            {
                var gap2 = Instantiate(gap.gameObject, parent, false);
                gap2.name = "GAP";
                gap2.transform.SetSiblingIndex(idx + 3);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[StartPanel] 혼자 하기 버튼 추가 실패: {e.Message}");
        }
    }

    // ── 혼자 하기 시나리오 고르기 ─────────────────────────────
    // 혼자 하기는 방 옵션이 없어 시나리오가 SoloModeLauncher.ScenarioId 로 고정이었다.
    // '혼자 하기' 아래에 버튼을 하나 더 복제해, 누를 때마다 Resources/Scenarios 의 시나리오를 차례로 넘긴다.

    private const bool ShowSoloScenarioButton = false;
    private const string SoloScenarioButtonName = "BTN_SoloScenario";

    [Serializable] private class ScenarioHead { public string scenarioId; public NameHead scenarioName; }
    [Serializable] private class NameHead { public string ko; public string en; }

    private readonly System.Collections.Generic.List<(string id, string name)> _soloScenarios = new();
    private int _soloScenarioIndex;
    private TMP_Text _soloScenarioLabel;

    private void AddSoloScenarioButton()
    {
        try
        {
            if (transform.Find(SoloScenarioButtonName) != null) return;
            var solo = transform.Find(SoloButtonName);
            if (solo == null) return;

            foreach (var ta in Resources.LoadAll<TextAsset>(LobbySettings.Current.scenarioResourcePath))
            {
                ScenarioHead h = null;
                try { h = JsonUtility.FromJson<ScenarioHead>(ta.text); } catch { }
                if (h == null || string.IsNullOrEmpty(h.scenarioId)) continue;
                if (!ScenarioHooks.ScenarioAllowed(h.scenarioId)) continue;   // 콘텐츠 필터(시연용 목록)
                string n = h.scenarioName != null && !string.IsNullOrEmpty(h.scenarioName.ko) ? h.scenarioName.ko : h.scenarioId;
                _soloScenarios.Add((h.scenarioId, n));
            }
            if (_soloScenarios.Count <= 1) return;
            _soloScenarios.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            _soloScenarioIndex = Mathf.Max(0, _soloScenarios.FindIndex(s => s.id == SoloModeLauncher.ScenarioId));

            var go = Instantiate(solo.gameObject, solo.parent, false);
            go.name = SoloScenarioButtonName;
            var button = go.GetComponent<Button>();
            if (button != null)
            {
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(OnSoloScenarioButtonClick);
            }
            _soloScenarioLabel = go.GetComponentInChildren<TMP_Text>(true);
            if (_soloScenarioLabel != null) { _soloScenarioLabel.enableAutoSizing = true; _soloScenarioLabel.fontSizeMin = 12; }
            go.transform.SetSiblingIndex(solo.GetSiblingIndex() + 1);
            ApplySoloScenario();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[StartPanel] 혼자 하기 시나리오 버튼 추가 실패: {e.Message}");
        }
    }

    private void OnSoloScenarioButtonClick()
    {
        if (_soloScenarios.Count == 0) return;
        _soloScenarioIndex = (_soloScenarioIndex + 1) % _soloScenarios.Count;
        ApplySoloScenario();
    }

    private void ApplySoloScenario()
    {
        var s = _soloScenarios[_soloScenarioIndex];
        SoloModeLauncher.ScenarioId = s.id;
        if (_soloScenarioLabel != null) _soloScenarioLabel.text = $"▶ {s.name}";
        Debug.Log($"[StartPanel] 혼자 하기 시나리오: {s.id}");
    }

    public void OnSoloButtonClick()
    {
        Debug.Log("[StartPanel] 혼자 하기 선택");
        Mst.Events.Invoke(MstEventKeys.hideLanguageToggle);
        Mst.Events.Invoke(MstEventKeys.hideLanguageSelection);
        Mst.Events.Invoke(MstEventKeys.hideStartPanel);

        SoloModeLauncher.Begin(reason =>
        {
            // 시작에 실패하면 처음 화면으로 돌아온다
            Debug.LogWarning($"[StartPanel] 혼자 하기 실패 → 시작 화면 복귀: {reason}");
            Mst.Events.Invoke(MstEventKeys.showStartPanel);
        });
    }
}
