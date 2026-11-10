using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

/// <summary>
/// [클라이언트] 평가 점수판 · 감점 알림 · 결과 화면. 씬 파일 없이 코드로 만든다(앱이 시작하면 자동으로 하나 생긴다).
///
///  - 서버가 감점을 기록하면(EvalPenaltyBroadcast) 오른쪽 위 점수판이 갱신되고, "-N 사유" 알림이 잠깐 뜬다(경고음 포함).
///  - 시나리오가 끝나면(EvalResultBroadcast) 점수·등급·영역별 감점·감점 내역을 결과 화면으로 보여 준다.
///    종료 팝업은 이 결과 화면을 닫은 뒤에 뜬다(ScenarioExitHandler).
///  - 점수판·결과 화면·힌트 버튼은 평가 모드(시나리오 ID 가 _eval 로 끝남)에서만 나온다.
///    훈련 모드에서는 위반 사유만 알려 주고, 치명 위반이면 사고 연출 뒤 종료 팝업을 띄운다.
///
/// 화면 오버레이라 VR 헤드셋에서는 보이지 않는다 → VR 에서는 표시하지 않고 로그만 남긴다(VR 표시는 월드 UI 로 별도).
/// 문구는 규칙표(evaluation_rules.json)에서 사용자의 언어(ko/en/vi)로 찾는다.
/// </summary>
public class EvaluationClientUI : MonoBehaviour
{
    public static EvaluationClientUI Instance { get; private set; }

    /// <summary>결과 화면이 떠 있는 동안 true (종료 팝업이 그 뒤로 미뤄진다)</summary>
    public static bool IsResultShowing { get; private set; }

    private static Action s_afterClose;

    /// <summary>결과 화면이 떠 있으면 닫힌 뒤에, 아니면 바로 실행한다.</summary>
    public static void RunWhenResultClosed(Action action)
    {
        if (action == null) return;
        if (IsResultShowing) s_afterClose += action;
        else action();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("[EvaluationClientUI]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<EvaluationClientUI>();
    }

    // ── 수신 등록 ─────────────────────────────────────────────

    private ClientManager _registeredOn;
    private float _nextCheck;

    private void OnEnable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated += HandleTaskState;
        ScenarioStateReceiver.OnSnapshotReceived += HandleSnapshot;
    }

    private void OnDisable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated -= HandleTaskState;
        ScenarioStateReceiver.OnSnapshotReceived -= HandleSnapshot;
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextCheck) return;
        _nextCheck = Time.unscaledTime + 1f;

        var cm = InstanceFinder.ClientManager;
        if (cm == _registeredOn) return;

        if (_registeredOn != null)
        {
            try
            {
                _registeredOn.UnregisterBroadcast<EvalPenaltyBroadcast>(OnPenalty);
                _registeredOn.UnregisterBroadcast<EvalResultBroadcast>(OnResult);
            }
            catch (Exception) { /* 이미 파괴된 매니저 */ }
            _registeredOn = null;
        }
        if (cm != null)
        {
            cm.RegisterBroadcast<EvalPenaltyBroadcast>(OnPenalty);
            cm.RegisterBroadcast<EvalResultBroadcast>(OnResult);
            _registeredOn = cm;
        }
    }

    private static bool IsXr()
    {
        try { return UnityEngine.XR.XRSettings.isDeviceActive; }
        catch (Exception) { return false; }
    }

    private static string Lang()
    {
        try
        {
            string code = LocalizationSettings.SelectedLocale != null ? LocalizationSettings.SelectedLocale.Identifier.Code : "ko";
            if (string.IsNullOrEmpty(code)) return "ko";
            code = code.ToLowerInvariant();
            return code.StartsWith("en") ? "en" : code.StartsWith("vi") ? "vi" : "ko";
        }
        catch (Exception) { return "ko"; }
    }

    private static string RuleText(string ruleId)
    {
        var rule = EvaluationRules.Get(ruleId);
        return rule != null ? rule.Message(Lang()) : ruleId;
    }

    // ── 감점 알림 ─────────────────────────────────────────────

    private Canvas _hudCanvas;
    private TextMeshProUGUI _scoreText;
    private TextMeshProUGUI _toastText;
    private Coroutine _toastCo;

    private void OnPenalty(EvalPenaltyBroadcast msg, Channel channel)
    {
        Debug.Log($"[Eval] 감점 알림: {msg.ruleId} -{msg.points} → {msg.score}점{(msg.fatal ? " (치명)" : "")}");

        bool eval = EvaluationMode.ClientIsEvaluation;
        if (eval && msg.ruleId == EvaluationMode.HintRuleId) OnHintGranted();
        if (IsXr()) return;

        EnsureHud();
        string lang = Lang();
        if (eval) SetScore(msg.score);

        // 훈련 모드: 점수 없이 사유만 (무엇을 잘못했는지 알려 주는 용도)
        string text = eval ? $"-{msg.points}  {RuleText(msg.ruleId)}"
                           : (lang == "en" ? "Warning: " : lang == "vi" ? "Cảnh báo: " : "주의: ") + RuleText(msg.ruleId);
        if (msg.fatal)
            text += lang == "en" ? "\nCritical violation — training failed." : lang == "vi" ? "\nVi phạm nghiêm trọng." : "\n치명 위반 — 훈련에서 탈락했습니다.";
        _toastText.text = text;
        if (_toastCo != null) StopCoroutine(_toastCo);
        _toastCo = StartCoroutine(ToastRoutine());

        try { Managers.Sound.Play("Task_Fail", Define.Sound.Effect); }
        catch (Exception) { /* 소리 매니저가 없는 씬 */ }
    }

    private IEnumerator ToastRoutine()
    {
        var color = new Color(1f, 0.35f, 0.35f, 1f);
        _toastText.color = color;
        _toastText.transform.localScale = Vector3.one * 1.12f;
        for (float t = 0f; t < 0.25f; t += Time.unscaledDeltaTime)
        {
            _toastText.transform.localScale = Vector3.one * Mathf.Lerp(1.12f, 1f, t / 0.25f);
            yield return null;
        }
        _toastText.transform.localScale = Vector3.one;

        for (float t = 0f; t < 3.5f; t += Time.unscaledDeltaTime) yield return null;

        for (float t = 0f; t < 0.8f; t += Time.unscaledDeltaTime)
        {
            _toastText.color = new Color(color.r, color.g, color.b, 1f - t / 0.8f);
            yield return null;
        }
        _toastText.text = string.Empty;
        _toastCo = null;
    }

    private void EnsureHud()
    {
        if (_hudCanvas != null) return;

        _hudCanvas = MakeCanvas("EvalHud", 90);

        // 점수판: 오른쪽 위 (상단 메뉴 아이콘 줄 아래)
        var panel = new GameObject("ScorePanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(_hudCanvas.transform, false);
        var prt = (RectTransform)panel.transform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(1f, 1f);
        prt.anchoredPosition = new Vector2(-24f, -190f);
        prt.sizeDelta = new Vector2(300f, 64f);
        var bg = panel.GetComponent<Image>();
        bg.color = new Color(0.05f, 0.08f, 0.16f, 0.82f);
        bg.raycastTarget = false;

        _scoreText = MakeText(panel.transform, "Score", string.Empty, 30f, Color.white, FontStyles.Normal, TextAlignmentOptions.Center);
        Stretch(_scoreText.rectTransform, 8f);
        _scorePanel = panel;

        // 힌트 버튼: 점수판 바로 아래 (평가 모드에서만 보인다)
        _hintButtonGo = new GameObject("HintButton", typeof(RectTransform), typeof(Image), typeof(Button));
        _hintButtonGo.transform.SetParent(_hudCanvas.transform, false);
        var hrt = (RectTransform)_hintButtonGo.transform;
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(1f, 1f);
        hrt.anchoredPosition = new Vector2(-24f, -262f);
        hrt.sizeDelta = new Vector2(300f, 56f);
        _hintButtonGo.GetComponent<Image>().color = new Color(0.85f, 0.55f, 0.1f, 0.92f);
        _hintButton = _hintButtonGo.GetComponent<Button>();
        _hintButton.onClick.AddListener(OnHintClicked);
        _hintLabel = MakeText(_hintButtonGo.transform, "Label", string.Empty, 24f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(_hintLabel.rectTransform, 4f);

        // 알림: 힌트 버튼 아래 (오른쪽 정렬, 넓게)
        _toastText = MakeText(_hudCanvas.transform, "Toast", string.Empty, 28f, new Color(1f, 0.35f, 0.35f), FontStyles.Bold, TextAlignmentOptions.TopRight);
        var trt = _toastText.rectTransform;
        trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(1f, 1f);
        trt.anchoredPosition = new Vector2(-24f, -332f);
        trt.sizeDelta = new Vector2(620f, 140f);

        RefreshEvalHud();
    }

    // ── 평가 모드 HUD (점수판 · 힌트) ────────────────────────────

    private GameObject _scorePanel;
    private GameObject _hintButtonGo;
    private Button _hintButton;
    private TextMeshProUGUI _hintLabel;

    private int _hintsUsed;
    private readonly HashSet<int> _hintedTasks = new HashSet<int>();
    private int _pendingHintTask = -1;
    private float _pendingHintUntil;
    private TaskState _task;
    private ScenarioData _hudScenario;

    private void HandleTaskState(TaskStateBroadcast broadcast)
    {
        _task = broadcast.currentTask;
        OnScenarioState();
    }

    private void HandleSnapshot(ScenarioSnapshotBroadcast snapshot)
    {
        _task = snapshot.currentTask;
        OnScenarioState();
    }

    private void OnScenarioState()
    {
        var scenario = ScenarioStateReceiver.Instance != null ? ScenarioStateReceiver.Instance.CurrentScenario : null;
        bool restarted = _task.taskIndex <= 0 && _task.status == TaskStatus.Running;
        if (!ReferenceEquals(scenario, _hudScenario) || restarted)
        {
            // 새 시나리오(또는 처음부터 다시): 힌트 기록·점수 초기화
            _hudScenario = scenario;
            _hintsUsed = 0;
            _hintedTasks.Clear();
            _pendingHintTask = -1;
            if (EvaluationMode.IsEvaluation(scenario) && !IsXr())
            {
                EnsureHud();
                SetScore(EvaluationRules.Set != null ? EvaluationRules.Set.startScore : 100);
            }
        }
        RefreshEvalHud();
    }

    private void SetScore(int score)
    {
        if (_scoreText == null) return;
        string lang = Lang();
        _scoreText.text = lang == "en" ? $"Score  <b>{score}</b>" : lang == "vi" ? $"Điểm  <b>{score}</b>" : $"평가 점수  <b>{score}</b>점";
    }

    /// <summary>점수판·힌트 버튼을 평가 모드에서만 보이게 하고, 남은 힌트 수를 갱신한다</summary>
    private void RefreshEvalHud()
    {
        if (_hudCanvas == null) return;
        bool eval = EvaluationMode.ClientIsEvaluation && !IsXr();
        if (_scorePanel != null) _scorePanel.SetActive(eval);

        bool running = _task.status == TaskStatus.Running;
        bool hintedHere = _hintedTasks.Contains(_task.taskIndex);
        int left = Mathf.Max(0, EvaluationMode.MaxHints - _hintsUsed);
        if (_hintButtonGo != null) _hintButtonGo.SetActive(eval && running && (left > 0 || hintedHere));
        if (_hintButton != null) _hintButton.interactable = _pendingHintTask < 0;
        if (_hintLabel != null)
        {
            string lang = Lang();
            var rule = EvaluationRules.Get(EvaluationMode.HintRuleId);
            int pts = rule != null ? rule.points : 5;
            _hintLabel.text = hintedHere
                ? (lang == "en" ? "Show hint again [H]" : lang == "vi" ? "Xem lại gợi ý [H]" : "힌트 다시 보기 [H]")
                : (lang == "en" ? $"Hint [H] -{pts} ({left} left)" : lang == "vi" ? $"Gợi ý [H] -{pts} (còn {left})" : $"힌트 [H] -{pts}점 (남은 {left}회)");
        }
    }

    private void OnHintClicked()
    {
        if (!EvaluationMode.ClientIsEvaluation || _task.status != TaskStatus.Running) return;
        int idx = _task.taskIndex;

        // 이 단계에서 이미 힌트를 받았다 → 감점 없이 다시 보여 준다
        if (_hintedTasks.Contains(idx))
        {
            GuideSystem.Instance?.ShowHintForCurrentTask();
            return;
        }
        if (_hintsUsed >= EvaluationMode.MaxHints) return;
        if (_pendingHintTask >= 0) return;   // 응답 대기 중

        var cm = InstanceFinder.ClientManager;
        if (cm == null || !InstanceFinder.IsClientStarted) return;

        _pendingHintTask = idx;
        _pendingHintUntil = Time.unscaledTime + 3f;   // 서버가 거절하면(횟수 초과 등) 응답이 없으므로 3초 뒤 다시 누를 수 있게
        cm.Broadcast(new EvalHintRequest { taskIndex = idx });
        RefreshEvalHud();
        Debug.Log($"[Eval] 힌트 요청 — 단계 {idx}");
    }

    /// <summary>서버가 힌트 감점을 기록했다 → 지금 단계의 가이드를 보여 준다</summary>
    private void OnHintGranted()
    {
        _hintsUsed++;
        _hintedTasks.Add(_task.taskIndex);
        _pendingHintTask = -1;
        GuideSystem.Instance?.ShowHintForCurrentTask();
        RefreshEvalHud();
    }

    private void LateUpdate()
    {
        // PC: 시점 조작 중에는 커서가 잠겨 버튼을 누를 수 없으므로 H 키로도 힌트를 쓴다
        if (_hintButtonGo != null && _hintButtonGo.activeInHierarchy && Input.GetKeyDown(KeyCode.H))
            OnHintClicked();

        // 힌트 응답이 3초 안에 안 오면(서버 거절) 버튼을 다시 켠다
        if (_pendingHintTask >= 0 && Time.unscaledTime > _pendingHintUntil)
        {
            _pendingHintTask = -1;
            RefreshEvalHud();
        }
    }

    // ── 결과 화면 ─────────────────────────────────────────────

    private Canvas _resultCanvas;

    private void OnResult(EvalResultBroadcast msg, Channel channel)
    {
        Debug.Log($"[Eval] 결과 수신: {msg.score}점 등급 {msg.grade}{(msg.fatal ? " (치명 탈락)" : "")} 감점줄={(string.IsNullOrEmpty(msg.lines) ? 0 : msg.lines.Split('\n').Length)}");
        if (IsXr()) return;   // VR 은 오버레이가 보이지 않아 닫을 수도 없다 — 로그만

        if (!EvaluationMode.ClientIsEvaluation)
        {
            // 훈련 모드: 결과 화면 없음. 치명 위반이면 사고 연출 뒤 이 사람의 종료 팝업만 띄운다
            if (msg.fatal) StartCoroutine(ShowEndPopupAfterAccident());
            return;
        }

        if (msg.fatal)
        {
            // 치명 탈락: 사고 연출(감전·화재, 약 5초)이 끝난 뒤에 결과를 보여 주고, 닫으면 이 사람의 훈련 종료 팝업을 띄운다
            StartCoroutine(ShowFatalResultRoutine(msg));
            return;
        }
        ShowResult(msg);
    }

    /// <summary>이 기기에서 사고 연출이 났는지 (그렇다면 사고 팝업이 종료 팝업 역할을 하므로 종료 팝업을 따로 띄우지 않는다)</summary>
    private static bool LocalAccident => EmergencyEventManager.Instance != null && EmergencyEventManager.Instance.HasAccident;

    /// <summary>사고 연출이 끝날 때까지 기다린다 (사고가 없거나 연출이 안 끝나면 최대 maxSeconds)</summary>
    private static IEnumerator WaitAccidentEffects(float maxSeconds)
    {
        for (float t = 0f; t < maxSeconds; t += Time.unscaledDeltaTime)
        {
            var eem = EmergencyEventManager.Instance;
            if (eem != null && eem.HasAccident && eem.EffectsFinished) yield break;
            yield return null;
        }
    }

    private static void InvokeEndPopup()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        try { DoDrill.Popup_EndGame.RequestShow(); }
        catch (Exception e) { Debug.LogWarning($"[Eval] 종료 팝업 호출 실패: {e.Message}"); }
    }

    /// <summary>
    /// 평가 모드 치명 탈락: 사고 연출(감전 약 2초, 화재 약 5초)이 끝나면 결과 화면 → 닫으면 사고 팝업(EmergencyEventManager 가 이어서 띄움).
    /// 이 기기에서 사고 연출이 없었으면(다른 경로의 치명 판정) 결과 화면을 닫은 뒤 일반 종료 팝업을 띄운다.
    /// </summary>
    private IEnumerator ShowFatalResultRoutine(EvalResultBroadcast msg)
    {
        yield return WaitAccidentEffects(8f);
        ShowResult(msg);
        if (!LocalAccident) RunWhenResultClosed(InvokeEndPopup);
    }

    /// <summary>훈련 모드 치명 탈락: 사고 팝업이 이미 종료 팝업 역할을 한다. 사고 연출이 없었을 때만 종료 팝업을 띄운다.</summary>
    private IEnumerator ShowEndPopupAfterAccident()
    {
        yield return WaitAccidentEffects(6f);
        if (!LocalAccident) InvokeEndPopup();
    }

    private static string Category(string id)
    {
        string lang = Lang();
        switch (id)
        {
            case "safety": return lang == "ko" ? "안전" : "Safety";
            case "procedure": return lang == "ko" ? "절차" : "Procedure";
            case "technique": return lang == "ko" ? "기술" : "Technique";
            case "environment": return lang == "ko" ? "환경" : "Environment";
            default: return id;
        }
    }

    private void ShowResult(EvalResultBroadcast msg)
    {
        if (_resultCanvas != null) Destroy(_resultCanvas.gameObject);
        string lang = Lang();

        _resultCanvas = MakeCanvas("EvalResult", 200);

        // 화면 전체를 덮는 어두운 배경 (뒤쪽 클릭 차단)
        var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        dim.transform.SetParent(_resultCanvas.transform, false);
        Stretch((RectTransform)dim.transform, 0f);
        dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

        var card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        card.transform.SetParent(dim.transform, false);
        var crt = (RectTransform)card.transform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(860f, 0f);
        card.GetComponent<Image>().color = new Color(0.06f, 0.09f, 0.18f, 0.97f);
        var vlg = card.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(48, 48, 40, 40);
        vlg.spacing = 14f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fitter = card.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        MakeText(card.transform, "Title", lang == "en" ? "Training Result" : lang == "vi" ? "Kết quả huấn luyện" : "훈련 결과", 44f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);

        Color gradeColor = msg.grade == "S" ? new Color(0.35f, 0.9f, 1f) : msg.grade == "A" ? new Color(0.45f, 0.9f, 0.5f)
                         : msg.grade == "B" ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.4f, 0.4f);
        string scoreLine = lang == "en" ? $"<size=84><b>{msg.score}</b></size> pts    Grade <b>{msg.grade}</b>"
                         : lang == "vi" ? $"<size=84><b>{msg.score}</b></size> điểm    Hạng <b>{msg.grade}</b>"
                         : $"<size=84><b>{msg.score}</b></size>점    등급 <b>{msg.grade}</b>";
        MakeText(card.transform, "Score", scoreLine, 40f, gradeColor, FontStyles.Normal, TextAlignmentOptions.Center);

        if (msg.fatal)
            MakeText(card.transform, "Fatal", lang == "en" ? "Critical violation — training failed" : lang == "vi" ? "Vi phạm nghiêm trọng" : "치명 위반으로 탈락했습니다",
                     30f, new Color(1f, 0.4f, 0.4f), FontStyles.Bold, TextAlignmentOptions.Center);

        // 감점 내역과 영역별 합계
        var perCategory = new Dictionary<string, int>();
        var list = new StringBuilder();
        int count = 0, shown = 0;
        if (!string.IsNullOrEmpty(msg.lines))
        {
            foreach (var line in msg.lines.Split('\n'))
            {
                var p = line.Split('|');
                if (p.Length < 3) continue;
                int.TryParse(p[1], out int pts);
                var rule = EvaluationRules.Get(p[0]);
                string cat = rule != null ? rule.category : "-";
                perCategory[cat] = (perCategory.TryGetValue(cat, out int v) ? v : 0) + pts;
                count++;
                if (shown < 10)
                {
                    list.Append($"<color=#FF7070>-{pts}</color>  {RuleText(p[0])}\n");
                    shown++;
                }
            }
        }

        if (count == 0)
        {
            MakeText(card.transform, "None", lang == "en" ? "No penalties." : lang == "vi" ? "Không bị trừ điểm." : "감점이 없습니다.",
                     32f, new Color(0.7f, 0.95f, 0.75f), FontStyles.Normal, TextAlignmentOptions.Center);
        }
        else
        {
            var sum = new StringBuilder();
            foreach (var cat in new[] { "safety", "procedure", "technique", "environment" })
                if (perCategory.TryGetValue(cat, out int v) && v > 0)
                    sum.Append($"{Category(cat)} <color=#FF7070>-{v}</color>     ");
            if (sum.Length > 0)
                MakeText(card.transform, "Categories", sum.ToString().TrimEnd(), 30f, Color.white, FontStyles.Normal, TextAlignmentOptions.Center);

            if (count > shown) list.Append(lang == "en" ? $"... and {count - shown} more" : lang == "vi" ? $"... và {count - shown} mục khác" : $"… 외 {count - shown}건");
            MakeText(card.transform, "List", list.ToString().TrimEnd(), 26f, new Color(0.9f, 0.92f, 0.98f), FontStyles.Normal, TextAlignmentOptions.TopLeft);
        }

        int sec = Mathf.Max(0, msg.totalSeconds);
        MakeText(card.transform, "Time", (lang == "en" ? "Time " : lang == "vi" ? "Thời gian " : "소요 시간 ") + $"{sec / 60}:{sec % 60:00}",
                 24f, new Color(0.65f, 0.7f, 0.8f), FontStyles.Normal, TextAlignmentOptions.Center);

        // 확인 버튼
        var btnGo = new GameObject("Confirm", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGo.transform.SetParent(card.transform, false);
        btnGo.GetComponent<LayoutElement>().preferredHeight = 84f;
        btnGo.GetComponent<Image>().color = new Color(0.15f, 0.42f, 0.85f, 1f);
        var label = MakeText(btnGo.transform, "Label", lang == "en" ? "OK" : lang == "vi" ? "Xác nhận" : "확인", 36f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(label.rectTransform, 0f);
        btnGo.GetComponent<Button>().onClick.AddListener(CloseResult);

        IsResultShowing = true;
        Cursor.lockState = CursorLockMode.None;   // PC 는 시점 고정이 걸려 있어 버튼을 누를 수 없다
        Cursor.visible = true;
    }

    private void CloseResult()
    {
        if (_resultCanvas != null) Destroy(_resultCanvas.gameObject);
        _resultCanvas = null;
        IsResultShowing = false;

        var cb = s_afterClose;
        s_afterClose = null;
        cb?.Invoke();   // 미뤄 둔 종료 팝업 등
    }

    // ── UI 만들기 도우미 ───────────────────────────────────────

    private static Canvas MakeCanvas(string name, int sortingOrder)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(Instance != null ? Instance.transform : null, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    private static TextMeshProUGUI MakeText(Transform parent, string name, string text, float size, Color color, FontStyles style, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        var font = MobileWorkButton.KoreanFont();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style;
        t.alignment = align;
        t.richText = true;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    private static void Stretch(RectTransform rt, float margin)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(margin, margin);
        rt.offsetMax = new Vector2(-margin, -margin);
    }
}
