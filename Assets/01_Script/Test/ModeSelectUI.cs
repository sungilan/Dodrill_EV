using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================
//  ModeSelectUI.cs
//  시작 시 모드 선택 화면
//
//  씬 구조:
//    Canvas
//      └─ ModeSelectPanel
//           ├─ Title (TMP)
//           ├─ BtnScenario (Button)
//           └─ BtnFreeMode (Button)
//
//  씬 세팅:
//    - ModeSelectPanel을 이 컴포넌트가 붙은 오브젝트에 연결
//    - ScenarioManager와 FreeModeManager를 연결
// ============================================================
public class ModeSelectUI : MonoBehaviour
{
    [Header("UI")]
    public GameObject panel;
    public Button btnScenario;
    public Button btnFreeMode;
    public TMP_Text descriptionText;

    [Header("설명 텍스트")]
    [TextArea(2,4)] public string scenarioDesc = "GDS 진단부터 배터리팩 정비까지\nP0AA6 시나리오를 순서대로 진행합니다.";
    [TextArea(2,4)] public string freeModeDesc  = "차량 부품을 자유롭게 탈거하고\n재조립하며 구조를 학습합니다.";

    [Header("연결")]
    //public FreeModeManager freeModeManager;
    // ScenarioManager or ScenarioDistributor - 시나리오 시작용
    public GameObject scenarioBootstrapRoot;

    private void Start()
    {
        ShowPanel();

        btnScenario.onClick.AddListener(OnScenarioSelected);
        btnFreeMode.onClick.AddListener(OnFreeModeSelected);

        // 호버 설명
        AddHoverEvents(btnScenario, scenarioDesc);
        AddHoverEvents(btnFreeMode, freeModeDesc);
    }

    private void ShowPanel()
    {
        if (panel != null) panel.SetActive(true);
        if (descriptionText != null) descriptionText.text = "";
    }

    private void OnScenarioSelected()
    {
        if (panel != null) panel.SetActive(false);
        if (scenarioBootstrapRoot != null) scenarioBootstrapRoot.SetActive(true);
        Debug.Log("[ModeSelectUI] 시나리오 모드 선택");
    }

    private void OnFreeModeSelected()
    {
        if (panel != null) panel.SetActive(false);
        //freeModeManager?.EnterFreeMode();
        Debug.Log("[ModeSelectUI] 자유탈거 모드 선택");
    }

    private void AddHoverEvents(Button btn, string desc)
    {
        var trigger = btn.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();

        var enter = new UnityEngine.EventSystems.EventTrigger.Entry
            { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ =>
        {
            if (descriptionText != null) descriptionText.text = desc;
        });

        var exit = new UnityEngine.EventSystems.EventTrigger.Entry
            { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
        exit.callback.AddListener(_ =>
        {
            if (descriptionText != null) descriptionText.text = "";
        });

        trigger.triggers.Add(enter);
        trigger.triggers.Add(exit);
    }
}
