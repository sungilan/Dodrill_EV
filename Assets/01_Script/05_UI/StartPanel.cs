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
    }

    // StartButton OnClick에 연결
    public void OnStartButtonClick()
    {
        clientManager.SetActive(true);
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
