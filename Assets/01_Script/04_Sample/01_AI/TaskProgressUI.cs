using DG.Tweening;
using FishNet;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.UI;
using static ScenarioRunner;

namespace DoDrill.Training
{
    // ============================================================
    //  TaskProgressUI.cs
    //  전 플랫폼 Task 진행 UI (VR HUD / PC / Mobile 공용)
    //
    //  역할:
    //    1. 현재 Task 설명 표시 (task index, moduleId, description)
    //    2. "완료" 버튼 → InteractionEvents.FireTaskConfirmed 발행
    //       → NetworkInteractionBridge가 TaskConfirmSignal로 서버 전달
    //    3. 모든 모듈 타입의 폴백 완료 경로 제공
    //
    //  ConfirmTaskModule:    버튼 클릭 → FireTaskConfirmed(moduleId) → 정상 경로
    //  GrabZone/Use/All:     VR은 물리 인터랙션 우선, 실패 시 버튼 폴백
    //
    //  배치: Canvas(World Space 또는 Screen Space) 하위에 이 컴포넌트 부착
    //        Inspector에서 UI 요소 연결 필요
    // ============================================================

    public class TaskProgressUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject _panel;
        [SerializeField] private TMP_Text   _progressText;    // "1 / 8"
        [SerializeField] private TMP_Text   _moduleText;      // "HandHygiene"
        [SerializeField] private TMP_Text   _descriptionText; // extra.description
        [SerializeField] private TMP_Text   _statusText;      // Running / Failed
        [SerializeField] private Button     _confirmButton;   // 완료 버튼
        [SerializeField] private TMP_Text   _confirmBtnLabel; // 버튼 텍스트
        [SerializeField] private TMP_Text _boltProgressText;

        [Header("Notification UI (New)")]
        [SerializeField] private CanvasGroup _notifCanvasGroup; // 알림 패널의 CanvasGroup
        [SerializeField] private TMP_Text _notifText;          // 알림 텍스트 메쉬
        [SerializeField] private float _displayDuration = 3f;  // 표시 시간

        [Header("Localization Settings")]
        [SerializeField] private UnityEngine.Localization.LocalizedString _lsRunning;
        [SerializeField] private UnityEngine.Localization.LocalizedString _lsFailed;
        [SerializeField] private UnityEngine.Localization.LocalizedString _lsCompleted;
        [SerializeField] private UnityEngine.Localization.LocalizedString _lsRetrying;

        private ScenarioData _scenarioData;
        private int          _currentTaskIndex = -1;
        private int          _totalCount;
        private string       _currentModuleId  = string.Empty;
        private bool         _confirmAllowed;

        // ScenarioData.modules[].config.actionType 값 — UI 확인으로 끝나는 단계
        private const string ConfirmActionType = "ConfirmTask";

        // ── 생명주기 ──────────────────────────────────────────────

        // ── 텍스트 넘침 방지 ──────────────────────────────────────

        /// <summary>글이 길어져도 박스 밖으로 튀어나오지 않도록 자동 크기 조절 + 말줄임.</summary>
        private static void FitText(TMP_Text t, float minRatio)
        {
            if (t == null) return;
            float max = t.fontSize;
            t.enableAutoSizing = true;
            t.fontSizeMax = max;
            t.fontSizeMin = Mathf.Max(8f, max * minRatio);
            t.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>제목이 없을 때의 대비책: "Front_Cover_Removal" → "Front Cover Removal".</summary>
        private static string PrettifyId(string id)
            => string.IsNullOrEmpty(id) ? string.Empty : id.Replace('_', ' ');

        private void Awake()
        {
            FitText(_descriptionText, 0.6f);
            FitText(_moduleText, 0.7f);
            FitText(_boltProgressText, 0.7f);
            SetupCompactLayout();

            // 시작 시 알림창을 확실히 숨김
            if(_notifCanvasGroup != null)
            {
                _notifCanvasGroup.alpha = 0f;
                _notifCanvasGroup.interactable = false;
                _notifCanvasGroup.blocksRaycasts = false;
            }
        }

        private void Start()
        {
            if (_scenarioData != null) return;

            var sr = FindFirstObjectByType<ScenarioStateReceiver>();
            if (sr?.CurrentScenario != null)
            {
                _scenarioData = sr.CurrentScenario;
                _totalCount   = sr.TotalTaskCount;
                Refresh(sr.CurrentTaskState);
                return;
            }

            var receiver = FindFirstObjectByType<ScenarioReceiver>();
            if (receiver?.ReceivedData != null)
                _scenarioData = receiver.ReceivedData;
        }

        private void OnEnable()
        {
            ScenarioStateReceiver.OnTaskStateUpdated += OnTaskStateUpdated;
            ScenarioStateReceiver.OnSnapshotReceived += OnSnapshotReceived;
            ScenarioReceiver.OnScenarioReceived      += OnScenarioReceived;
            InstanceFinder.ClientManager?.RegisterBroadcast<BoltProgressBroadcast>(OnBoltProgressReceived);
            InstanceFinder.ClientManager?.RegisterBroadcast<TaskNotificationBroadcast>(OnTaskNotificationReceived);

            if (_confirmButton != null)
                _confirmButton.onClick.AddListener(OnConfirmClicked);
        }

        private void OnDisable()
        {
            ScenarioStateReceiver.OnTaskStateUpdated -= OnTaskStateUpdated;
            ScenarioStateReceiver.OnSnapshotReceived -= OnSnapshotReceived;
            ScenarioReceiver.OnScenarioReceived      -= OnScenarioReceived;
            InstanceFinder.ClientManager?.UnregisterBroadcast<BoltProgressBroadcast>(OnBoltProgressReceived);
            InstanceFinder.ClientManager?.UnregisterBroadcast<TaskNotificationBroadcast>(OnTaskNotificationReceived);

            if (_confirmButton != null)
                _confirmButton.onClick.RemoveListener(OnConfirmClicked);
        }

        // ── 수신 핸들러 ───────────────────────────────────────────

        private void OnScenarioReceived(ScenarioData data)
        {
            if (data == null) return;
            _scenarioData = data;

            var sr = FindFirstObjectByType<ScenarioStateReceiver>();
            if (sr != null && sr.CurrentTaskState.taskIndex >= 0)
            {
                _totalCount = sr.TotalTaskCount;
                Refresh(sr.CurrentTaskState);
            }
        }

        private void OnTaskStateUpdated(TaskStateBroadcast broadcast)
        {
            _totalCount = broadcast.totalTaskCount;

            if (_scenarioData == null)
            {
                var sr = FindFirstObjectByType<ScenarioStateReceiver>();
                if (sr?.CurrentScenario != null)
                    _scenarioData = sr.CurrentScenario;
                else
                {
                    var receiver = FindFirstObjectByType<ScenarioReceiver>();
                    if (receiver?.ReceivedData != null)
                        _scenarioData = receiver.ReceivedData;
                }
            }

            Refresh(broadcast.currentTask);
        }

        private void OnSnapshotReceived(ScenarioSnapshotBroadcast broadcast)
        {
            _scenarioData = broadcast.scenarioData;
            _totalCount   = broadcast.totalTaskCount;
            Refresh(broadcast.currentTask);
        }

        private void OnBoltProgressReceived(BoltProgressBroadcast msg, FishNet.Transporting.Channel channel)
        {
            if(_boltProgressText == null) return;

            int remaining = msg.total - msg.removed;

            if(remaining > 0)
            {
                _boltProgressText.gameObject.SetActive(true);
                _boltProgressText.text = $"남은 볼트: <color=yellow>{remaining}</color> / {msg.total}";
            }
            else
            {
                _boltProgressText.gameObject.SetActive(false);
            }
        }

        /// <summary>서버가 보낸 단계 이름(어느 언어든)과 같은 title 을 가진 모듈을 찾아 현재 언어로 돌려준다. 못 찾으면 받은 그대로.</summary>
        private string ResolveLocalizedTitle(string received)
        {
            if (string.IsNullOrEmpty(received) || _scenarioData?.modules == null) return received;
            foreach (var entry in _scenarioData.modules)
            {
                var extra = entry?.config?.extra;
                if (extra == null) continue;
                foreach (var kv in extra)
                {
                    if (kv.key != "title") continue;
                    if (kv.ko == received || kv.en == received || kv.vi == received)
                    {
                        string local = kv.GetLocalized();
                        return string.IsNullOrEmpty(local) ? received : local;
                    }
                }
            }
            return received;
        }

        private void OnTaskNotificationReceived(TaskNotificationBroadcast msg, FishNet.Transporting.Channel channel)
        {
            if(_notifCanvasGroup == null || _notifText == null) return;

            // ── 1. 이름 결정 로직 (ScenarioRunner가 보낸 이름을 우선 사용) ──
            string activePlayerName = msg.clientName;

            // 서버가 이름을 찾지 못해 빈 값이 오면 접속자 목록(UserStateRow)과 같은 규칙의 번호로 대체
            if(string.IsNullOrEmpty(activePlayerName))
                activePlayerName = msg.clientId >= 0 ? $"Player {msg.clientId + 1}" : "Player";

            // ── 2. 텍스트 구성 및 색상 적용 ──
            // 요청하신 형광 초록 계열 색상 (#00f800) 적용
            string pColor = "#00f800";
            // 서버는 자기 언어 설정(기본 en)으로 단계 이름을 골라 보내므로, 시나리오 데이터에서 같은 단계를 찾아 이 기기의 언어로 다시 표시한다
            string taskTitle = ResolveLocalizedTitle(msg.taskName);
            _notifText.text = $"<color={pColor}><b>{activePlayerName}</b></color>(이)가\n<b>{taskTitle}</b>를 완료하였습니다.";

            // ── 3. DOTween 연출 (나타났다 사라지기) ──
            _notifCanvasGroup.DOKill(); // 진행 중인 연출 즉시 중단

            Sequence seq = DOTween.Sequence();

            // UI 초기화
            _notifCanvasGroup.alpha = 0f;
            _notifCanvasGroup.gameObject.SetActive(true);

            // 연출 시작
            seq.Append(_notifCanvasGroup.DOFade(1f, 0.5f).SetEase(Ease.OutCubic)); // 부드럽게 나타남
            seq.AppendInterval(_displayDuration);                                 // 설정된 시간만큼 대기
            seq.Append(_notifCanvasGroup.DOFade(0f, 0.8f).SetEase(Ease.InSine));   // 천천히 사라짐
            seq.OnComplete(() => {
                _notifCanvasGroup.gameObject.SetActive(false);
            });
        }

        private void Refresh(TaskState state)
        {
            if(_boltProgressText != null) _boltProgressText.gameObject.SetActive(false);

            _currentTaskIndex = state.taskIndex;

            bool visible = state.status == TaskStatus.Running
                        || state.status == TaskStatus.Failed;
            _panel?.SetActive(visible);

            if (_progressText != null)
                _progressText.text = $"{state.taskIndex + 1} / {_totalCount}";
            UpdateProgressBar(state.taskIndex);

            if(_statusText != null)
            {
                var lse = _statusText.GetComponent<LocalizeStringEvent>();
                if(lse != null)
                {
                    lse.StringReference = state.status switch
                    {
                        TaskStatus.Running => _lsRunning,
                        TaskStatus.Failed => _lsFailed,
                        TaskStatus.Completed => _lsCompleted,
                        _ => null
                    };
                }
                //_statusText.gameObject.SetActive(true);
            }

            _confirmAllowed = false;

            if (_scenarioData != null && state.taskIndex < _scenarioData.scenario.tasks.Count)
            {
                var taskDef = _scenarioData.scenario.tasks[state.taskIndex];
                _currentModuleId = taskDef.moduleId;
                var config = _scenarioData.GetModuleConfig(taskDef.moduleId);

                // '완료' 버튼은 UI 확인으로 끝나는 단계(actionType = ConfirmTask)에서만 허용.
                // 물건 집기·존 도착·측정 단계는 실제 상호작용으로만 완료되어야 한다.
                _confirmAllowed = config != null
                    && string.Equals(config.actionType, ConfirmActionType, System.StringComparison.OrdinalIgnoreCase);

                // 내부 ID(Front_Cover_Removal 등)를 그대로 보여주지 않고 시나리오의 단계 제목(extra.title)을 쓴다.
                if (_moduleText != null)
                {
                    string title = config?.GetExtra("title");
                    _moduleText.text = string.IsNullOrEmpty(title) ? PrettifyId(taskDef.moduleId) : title;
                }

                if (_descriptionText != null)
                {
                    string desc = config?.GetExtra("description");
                    _descriptionText.text = string.IsNullOrEmpty(desc) ? PrettifyId(taskDef.moduleId) : desc;
                }
            }

            // 완료 버튼: Running 상태이면서 UI 확인 단계일 때만 활성화
            if (_confirmButton != null)
                _confirmButton.interactable = state.status == TaskStatus.Running && _confirmAllowed;

            if (_confirmBtnLabel != null)
                _confirmBtnLabel.text = state.status == TaskStatus.Failed ? "재시도 대기 중..." : "완료";

            ResizePanel();
        }

        // ── 컴팩트 패널 · 진행도 바 (공군 프로젝트 방식) ──────────────
        //  - PC·모바일(오버레이 캔버스) 패널만: 작게 줄이고, 설명 글 양에 맞춰 높이가 변하게 한다. VR(월드 스페이스)은 그대로.
        //  - '3 / 42' 가 줄바꿈되던 것을 NoWrap + 자동 크기로 막는다.
        //  - 구분선(Divider)을 진행도 바로 쓴다 (현재 단계 / 전체 단계).
        private const float PanelWidth = 420f;
        private const float PanelPad = 16f;
        private const float DescTop = 62f;
        private bool _compact;
        private Image _barFill;

        private void SetupCompactLayout()
        {
            if (_panel == null) return;
            var cv = _panel.GetComponentInParent<Canvas>();
            if (cv == null || cv.rootCanvas.renderMode == RenderMode.WorldSpace) return;
            _compact = true;

            var prt = (RectTransform)_panel.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot = new Vector2(0f, 1f);
            prt.sizeDelta = new Vector2(PanelWidth, prt.sizeDelta.y);

            float inner = PanelWidth - PanelPad * 2f;

            // 머리줄: [1 / 42] ........ [진행 중]
            if (_progressText != null)
            {
                var row = _progressText.transform.parent as RectTransform;
                if (row != null && row != prt)
                {
                    row.anchorMin = row.anchorMax = new Vector2(0f, 1f);
                    row.pivot = new Vector2(0f, 1f);
                    row.anchoredPosition = new Vector2(PanelPad, -12f);
                    row.sizeDelta = new Vector2(inner, 30f);
                }
                var pr = _progressText.rectTransform;
                pr.anchorMin = new Vector2(0f, 0f); pr.anchorMax = new Vector2(0f, 1f); pr.pivot = new Vector2(0f, 0.5f);
                pr.anchoredPosition = Vector2.zero; pr.sizeDelta = new Vector2(150f, 0f);
                _progressText.textWrappingMode = TextWrappingModes.NoWrap;
                _progressText.overflowMode = TextOverflowModes.Ellipsis;
                _progressText.enableAutoSizing = true;
                _progressText.fontSizeMax = Mathf.Max(_progressText.fontSizeMax, _progressText.fontSize);
                _progressText.fontSizeMin = Mathf.Max(10f, _progressText.fontSizeMax * 0.6f);
            }
            if (_statusText != null)
            {
                var sr = _statusText.rectTransform;
                sr.anchorMin = new Vector2(1f, 0f); sr.anchorMax = new Vector2(1f, 1f); sr.pivot = new Vector2(1f, 0.5f);
                sr.anchoredPosition = Vector2.zero; sr.sizeDelta = new Vector2(150f, 0f);
                _statusText.alignment = TextAlignmentOptions.MidlineRight;
                _statusText.textWrappingMode = TextWrappingModes.NoWrap;
            }
            // 모듈 ID/제목 줄은 숨긴다(설명 문장이 핵심, 내부 ID 노출 방지)
            if (_moduleText != null) _moduleText.gameObject.SetActive(false);

            // 구분선 = 진행도 바 자리
            var div = FindDivider();
            if (div != null)
            {
                div.anchorMin = div.anchorMax = new Vector2(0f, 1f); div.pivot = new Vector2(0f, 1f);
                div.anchoredPosition = new Vector2(PanelPad, -48f); div.sizeDelta = new Vector2(inner, 5f);
            }

            if (_descriptionText != null)
            {
                _descriptionText.textWrappingMode = TextWrappingModes.Normal;
                _descriptionText.overflowMode = TextOverflowModes.Overflow;
                _descriptionText.enableAutoSizing = false;   // 높이를 글 양에 맞추므로 글자 크기는 고정
                _descriptionText.alignment = TextAlignmentOptions.TopLeft;
                var dr = _descriptionText.rectTransform;
                dr.anchorMin = dr.anchorMax = new Vector2(0f, 1f); dr.pivot = new Vector2(0f, 1f);
                dr.anchoredPosition = new Vector2(PanelPad, -DescTop); dr.sizeDelta = new Vector2(inner, 40f);
            }

            ResizePanel();
        }

        private RectTransform FindDivider()
        {
            if (_panel == null) return null;
            foreach (var im in _panel.GetComponentsInChildren<Image>(true))
                if (im.name.IndexOf("Divider", System.StringComparison.OrdinalIgnoreCase) >= 0 || im.name == "Line") return im.rectTransform;
            return null;
        }

        private void ResizePanel()
        {
            if (!_compact || _panel == null || _descriptionText == null) return;
            var prt = (RectTransform)_panel.transform;
            float inner = PanelWidth - PanelPad * 2f;

            Vector2 pref = _descriptionText.GetPreferredValues(_descriptionText.text, inner, 0f);
            float h = Mathf.Clamp(pref.y, 24f, 260f);
            var dr = _descriptionText.rectTransform;
            dr.anchoredPosition = new Vector2(PanelPad, -DescTop);
            dr.sizeDelta = new Vector2(inner, h);

            float bottom = DescTop + h + 16f;
            if (_confirmButton != null && _confirmButton.gameObject.activeInHierarchy)
            {
                var crt = (RectTransform)_confirmButton.transform;
                crt.anchorMin = crt.anchorMax = new Vector2(0f, 1f); crt.pivot = new Vector2(0f, 1f);
                crt.anchoredPosition = new Vector2(PanelPad, -(bottom - 4f)); crt.sizeDelta = new Vector2(inner, 40f);
                bottom += 48f;
            }
            prt.sizeDelta = new Vector2(PanelWidth, bottom);
        }

        private void UpdateProgressBar(int idx)
        {
            if (!_compact || _panel == null) return;
            if (_barFill == null)
            {
                var div = FindDivider();
                if (div == null) return;
                var existing = div.Find("ProgressFill");
                GameObject go = existing != null ? existing.gameObject : new GameObject("ProgressFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(div, false);
                _barFill = go.GetComponent<Image>();
                _barFill.raycastTarget = false;
                _barFill.color = new Color(0.25f, 0.82f, 1f, 1f);
                var frt = _barFill.rectTransform;
                frt.pivot = new Vector2(0f, 0.5f);
                frt.offsetMin = frt.offsetMax = Vector2.zero;
                var dimg = div.GetComponent<Image>();
                if (dimg != null) dimg.color = new Color(1f, 1f, 1f, 0.18f);
            }
            float frac = _totalCount > 0 && idx >= 0 ? Mathf.Clamp01((idx + 1f) / _totalCount) : 0f;
            var r = _barFill.rectTransform;
            r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(frac, 1f);
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        // ── 완료 버튼 클릭 ─────────────────────────────────────────

        private void OnConfirmClicked()
        {
            if (_currentTaskIndex < 0 || !_confirmAllowed || string.IsNullOrEmpty(_currentModuleId)) return;

            // 정식 확인 경로: InteractionEvents → NetworkInteractionBridge → TaskConfirmSignal → 서버.
            // 서버(ScenarioRunner.OnReceiveTaskConfirm)가 현재 단계·moduleId 일치를 검증한 뒤 처리한다.
            // 디버그 명령을 쓰지 않으므로 DebugBootstrapper.isDebugMode 와 무관하게 동작한다.
            InteractionEvents.FireTaskConfirmed(_currentModuleId);

#if UNITY_EDITOR
            Debug.Log($"[TaskProgressUI] 완료 버튼: moduleId={_currentModuleId}, task={_currentTaskIndex}");
#endif
        }
    }
}
