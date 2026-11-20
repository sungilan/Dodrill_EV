using EPOOutline;
using UnityEngine;
using XRAirpotrSecurity;

// ============================================================
//  ImpactWrench.cs  — 단계별 볼트 상호작용 검증 및 동적 문구 로직
// ============================================================
public class ImpactWrench : MonoBehaviour
{
    // 매 프레임 Utils.GetPlatformType()(XR 설정 조회)을 부르던 것을 한 번만 조회하도록 캐시 (D-24)
    private PlatformType? _cachedPlatform;
    private PlatformType CachedPlatform => _cachedPlatform ??= Utils.GetPlatformType();

    public enum WrenchMode { Unscrew, Screw }

    [Header("설정")]
    public WrenchMode currentMode = WrenchMode.Unscrew;
    public float rotationSpeed = 1f;

    [Header("비주얼")]
    public MeshRenderer modeIndicator;
    public Color unscrewColor = Color.red;
    public Color screwColor = Color.green;
    public Animator wrenchAnimator;

    [Header("── ★ EPO 아웃라인 설정 ──")]
    [Tooltip("볼트 감지 시 변경할 아웃라인 색상")]
    public Color targetOutlineColor = new Color(1f, 1f, 0f, 1f); // 노란색

    [Tooltip("아웃라인 복구 시 설정할 색상 (기본값: 흰색)")]
    public Color restoreOutlineColor = Color.white;

    [Range(0, 10)]
    public float outlineWidth = 4f;

    [Header("오디오")]
    public AudioSource workAudio;

    [Header("PC 설정")]
    public KeyCode workKey = KeyCode.F;

    private static readonly int IsWorkingHash = Animator.StringToHash("isWorking");

    private Bolt _targetBolt;
    private GameObject _currentBoltGameObject;
    private Outlinable _currentOutlinable;

    private bool _buttonHeld;
    private bool _wasWorking;
    private SyncGrab _sync;
    private bool _mobileButtonShown;

    // 내가 작업(F/버튼)을 누르고 있는 렌치가 있는지 — 아바타가 ‘작업 중 반대 손 뻗기’를 생략하는 데 쓴다
    private static int s_workingCount;
    private static float s_lastWorkTime = -10f;
    public static bool RecentlyWorking => s_workingCount > 0 || Time.unscaledTime - s_lastWorkTime < 0.8f;

    /// <summary>
    /// 이 렌치를 '내가' 들고 있는가. 렌치는 모든 클라이언트에 같은 물체로 존재하므로, 다른 사람이 든 렌치가
    /// 볼트에 닿아도 내 화면에 안내(‘F 키를 눌러…’)가 뜨지 않도록 한다.
    /// </summary>
    private bool IsLocalHeld
    {
        get
        {
            if(_sync == null) _sync = GetComponentInParent<SyncGrab>();
            return _sync == null || (_sync.IsOwner && _sync.IsGrabbed && !_sync.IsHolstered);   // 벨트에 꽂힌 렌치는 작업하지 않는다
        }
    }
    private bool _progressShown;
    private bool _needRelease;   // 이 렌치가 볼트 진행도 UI를 띄운 상태인지

    /// <summary>내가 든 렌치가 지금 겨누고 있는 볼트(다음 단계 안내가 같은 볼트에 링을 겹쳐 그리지 않게 쓴다).</summary>
    public static Bolt LocalTargetBolt { get; private set; }
    /// <summary>내가 든 렌치가 볼트 정렬로 옮겨진 정도(0~1). PC 아바타 손목이 팔 길이를 넘어서도 손잡이를 따라가게 할 때 쓴다(PcArmReach).</summary>
    public static float LocalSnapWeight => s_snapOwner != null && s_snapOwner.isActiveAndEnabled ? s_snapOwner._snapW : 0f;
    private static ImpactWrench s_snapOwner;
    /// <summary>지금 작업 버튼을 누르고 있는가(진동 피드백용).</summary>
    public bool IsWorking => _wasWorking;
    /// <summary>지금 겨누고 있는 볼트(진동 피드백용).</summary>
    public Bolt TargetBolt => _targetBolt;

    // 지금 작업 중인 볼트 표시: 초록 아웃라인 + 볼트를 감싸는 초록 진행 링 (CMS VR 참고)
    private static readonly Color WorkingOutlineColor = new Color(0.25f, 1f, 0.35f, 1f);
    private LineRenderer _progRing, _progTrack;

    // ── 생명주기 ───────────────────────────────────────────

    private void Awake()
    {
        UpdateVisuals();
    }

    private void OnDisable()
    {
        RestoreHandVisuals();
        ResetBoltSnap();
        _buttonHeld = false;
        ApplyEffects(false);
        SetMobileButton(false);
        RestoreBoltOutlineForced();
        // 렌치를 내려놓거나 치워도(비활성화) 볼트 진행도 UI가 남지 않도록 반드시 숨긴다.
        HideProgress();
    }

    private void Update()
    {
        // 다른 사람이 든(또는 아무도 안 든) 렌치는 내 입력·안내와 무관하다
        if (!IsLocalHeld)
        {
            if (_targetBolt != null) ClearTarget();
            _buttonHeld = false;
            ApplyEffects(false);
            SetMobileButton(false);
            return;
        }

        // 입력 상태 확인
        if (CachedPlatform == PlatformType.PC)
        {
            _buttonHeld = Input.GetKey(workKey);
            // (11/20 진단) 'F 를 눌러도 볼트가 안 풀린다' 재현용 — 누를 때마다 왜 일을 못 하는지 한 줄 남긴다(처음 몇 번 시도에서 안 풀리던 원인 확인)
            if (Input.GetKeyDown(workKey))
                Debug.Log($"<color=white>[ImpactWrench]</color> {workKey} 눌림 — 대상볼트={(_targetBolt != null ? _targetBolt.name : "없음")}, 다시눌러야함={_needRelease}, 벨트={(_sync != null && _sync.IsHolstered)}, 잡힘={(_sync != null && _sync.IsGrabbed)}, 후보볼트={(_blockedCandidate != null ? _blockedCandidate.name : "없음")}");
        }
        else if (CachedPlatform == PlatformType.Mobile)
        {
            _buttonHeld = MobileWorkButton.IsHeld;
        }
        else if (CachedPlatform == PlatformType.VR)
        {
            // VRPress/VRRelease(AutoHand 누름·뗌 이벤트)는 뗌을 놓치면 계속 도는 상태로 남았다 → 든 손의 실제 트리거 값으로 덮어쓴다
            var hand = LocalHands.HoldingHand(transform);
            if (hand == null) _buttonHeld = false;
            else
            {
                bool on = LocalHands.TriggerHeld(hand, out bool valid);
                bool next = valid ? on : hand.IsSqueezing();   // 입력을 못 읽으면 손의 현재 누름 상태(이벤트 누적값 대신)
                if (next != _buttonHeld) Debug.Log($"[ImpactWrench] VR 트리거 {(next ? "누름" : "뗌")} — {(valid ? "입력 축" : "손 상태")} 기준");
                _buttonHeld = next;
            }
        }
        SetMobileButton(_targetBolt != null);
        if (_targetBolt != null && _mobileButtonShown) MobileWorkButton.SetLabel(_targetBolt.isAssembleMode ? "볼트 조이기" : "볼트 풀기");

        bool working = _buttonHeld;
        if (!working) _needRelease = false;
        ApplyEffects(working);
        if (working) s_lastWorkTime = Time.unscaledTime;
        UpdateBlockedHint();

        if (_targetBolt != null)
        {
            // 볼트가 이미 제거(비활성화/파괴)됐다면 더 이상 대상이 아니다.
            // 비활성화된 콜라이더는 OnTriggerExit가 호출되지 않아 진행도 UI가 남던 원인.
            // 11/18: 누른 채로 대상을 잃으면(볼트 사라짐·단계 바뀜) 손을 뗄 때까지 다음 볼트를 잡지 않는다.
            //  휠 너트 마지막 하나를 조이는 순간 서버가 '체결 완료'로 바꾸며 조인 너트를 치우고 같은 자리에 풀기용 너트를 새로 만든다.
            //  여기서 _needRelease 를 안 세우면 F 를 누르고 있는 그대로 새 너트를 잡아 바로 풀어 '휠 탈거 1/5'가 되던 문제.
            if (!_targetBolt.gameObject.activeInHierarchy)
            {
                if (working) _needRelease = true;
                ClearTarget();
                return;
            }

            // 실시간 검증: 작업 중 태스크가 바뀌는 상황 대비
            if (!IsCorrectBoltTask(_targetBolt))
            {
                if (working) _needRelease = true;
                ClearTarget();
                return;
            }

            // 작업이 끝난 볼트는 대기 상태로 두지 않고 대상에서 해제한다.
            if (_targetBolt.isloosened)
            {
                if (working) { TriggerBoltAnimation(_targetBolt.gameObject); _needRelease = true; }
                ClearTarget();
                return;
            }

            // 윤곽선 색 갱신: 방청제를 뿌리면 갈색 → 초록
            if (_currentOutlinable != null)
            {
                var wc = BoltHint.WorkColor(_targetBolt);
                if (_currentOutlinable.OutlineParameters.Color != wc) _currentOutlinable.OutlineParameters.Color = wc;
            }

            // ✅ 렌치 모드와 상관없이 볼트 자체의 모드에 따라 문구 결정
            string actionName = _targetBolt.isAssembleMode ? "볼트 체결" : "볼트 분해";

            if (working)
            {
                // 렌치 회전 방향은 물리적 일관성을 위해 currentMode 유지 가능 (필요시 수정)
                float dir = (currentMode == WrenchMode.Unscrew) ? 1f : -1f;
                if (BoltHint.NeedsLube(_targetBolt)) BoltHint.NoticeSeized();
                _targetBolt.InteractWithTool(dir * rotationSpeed * BoltWork.SpeedFactor(_targetBolt) * Time.deltaTime, transform.forward);

                // [작업 중]
                ShowProgress($"{actionName} 중...", GetPlatformInstruction(actionName, true));
            }
            else
            {
                // [대기 중]
                ShowProgress(actionName, GetPlatformInstruction(actionName, false));
            }
        }
    }

    // ── 볼트 진행도 UI ──────────────────────────────────────

    private void ShowProgress(string title, string instruction)
    {
        var ui = InteractionProgressBarUI.Instance;
        if (ui == null || _targetBolt == null) return;

        ui.ShowBoltProgress(_targetBolt.Progress, BoltPartName.Title(title, _targetBolt),   // '볼트 분해 — 프런트 언더 커버'처럼 어느 부품 볼트인지
            _currentBoltGameObject != null ? _currentBoltGameObject.name : _targetBolt.name,
            instruction);
        _progressShown = true;
    }

    private void HideProgress()
    {
        if (!_progressShown) return;
        _progressShown = false;
        InteractionProgressBarUI.Instance?.HideBoltProgress();
    }

    /// <summary>현재 볼트 대상 해제 — 아웃라인 복구 + 진행도 UI 숨김.</summary>
    private void ClearTarget()
    {
        RestoreBoltOutline();
        HideProgress();
        if (LocalTargetBolt == _targetBolt) LocalTargetBolt = null;
        _targetBolt = null;
        _currentBoltGameObject = null;
        HideRing();
    }

    // ── 단계 검증 로직 ──────────────────────────────────────

    // ── 단계 검증 로직 (디버그 강화) ──────────────────────────────────────
    private bool IsCorrectBoltTask(Bolt bolt)
    {
        if(bolt == null) return false;
        if(bolt.GetComponent<FreeWorkBolt>() != null) return true;   // 타이어 휠 너트 등 자유 실습 — 시나리오 단계와 무관

        // 1. 볼트 그룹의 설정 ID 가져오기
        BoltGroupCounter group = bolt.GetComponentInParent<BoltGroupCounter>();
        if(group == null) return false;

        // 2. 데이터 확보 (StateReceiver -> ScenarioReceiver 순으로 탐색)
        var stateReceiver = ScenarioStateReceiver.Instance;
        ScenarioData data = null;

        if(stateReceiver != null && stateReceiver.CurrentScenario != null)
        {
            data = stateReceiver.CurrentScenario;
        }
        else
        {
            // StateReceiver에 데이터가 없을 경우 원본 데이터 로더에서 확인
            var receiver = FindFirstObjectByType<ScenarioReceiver>();
            if(receiver != null && receiver.ReceivedData != null)
            {
                data = receiver.ReceivedData;
            }
        }

        // 데이터가 아예 로드되지 않은 극초기 상태라면 차단하지 않고 일단 허용(true)
        if(data == null)
        {
            // Debug.LogWarning("[ImpactWrench] 모든 경로에 시나리오 데이터 없음 - 일시 허용");
            return true;
        }

        // 3. 현재 인덱스를 사용하여 실제 시나리오 데이터에서 moduleId 추출
        // stateReceiver가 없으면 인덱스 0으로 가정하거나 데이터 검증 패스
        int currentIndex = (stateReceiver != null) ? stateReceiver.CurrentTaskState.taskIndex : 0;
        var tasks = data.scenario.tasks;

        if(currentIndex < 0 || currentIndex >= tasks.Count) return false;

        // ✅ JSON에 정의된 실제 문자열 ID 추출
        string currentModuleId = tasks[currentIndex].moduleId;

        // 4. 비교 (대소문자/공백 무시)
        bool isMatch = group.taskId.Trim().Equals(currentModuleId.Trim(), System.StringComparison.OrdinalIgnoreCase);

        // 렌치가 볼트에 닿아 있는 동안 매 프레임 불려서 로그가 폭주했다 → 결과가 바뀔 때만 남긴다
        if(!isMatch)
            LogChanged("mismatch:" + currentModuleId + ">" + group.taskId, $"<color=orange>[ImpactWrench]</color> 단계 불일치: 현재[<b>{currentModuleId}</b>] vs 볼트설정[<b>{group.taskId}</b>]");
        else
            LogChanged("match:" + currentModuleId, $"<color=lime>[ImpactWrench]</color> 단계 일치 확인: {currentModuleId}");

        return isMatch;
    }

    private static string s_lastLogKey;
    private static string s_lastHitLog;
    private static void LogChanged(string key, string message)
    {
        if(s_lastLogKey == key) return;
        s_lastLogKey = key;
        Debug.Log(message);
    }

    // ── 효과 적용 ───────────────────────────────────────────

    private void ApplyEffects(bool working)
    {
        if (working == _wasWorking) return;
        _wasWorking = working;
        if (working) s_workingCount++;
        else { s_workingCount = Mathf.Max(0, s_workingCount - 1); s_lastWorkTime = Time.unscaledTime; }

        if (wrenchAnimator != null)
            wrenchAnimator.SetBool(IsWorkingHash, working);

        if (workAudio != null)
        {
            if (working && !workAudio.isPlaying) workAudio.Play();
            else if (!working) workAudio.Stop();
        }
    }

    // ── 볼트 감지 (OnTriggerEnter/Exit) ────────────────────

    private void OnTriggerEnter(Collider other) => TryAcquire(other);

    // 렌치를 든 뒤에 이미 볼트 안에 들어가 있는 경우(날아오는 중 진입)도 잡도록
    private void OnTriggerStay(Collider other)
    {
        if (_targetBolt == null) TryAcquire(other);
    }

    private void SetMobileButton(bool show)
    {
        if (CachedPlatform != PlatformType.Mobile) return;
        if (show == _mobileButtonShown) return;
        _mobileButtonShown = show;
        MobileWorkButton.SetVisible(show);
    }

    private void TryAcquire(Collider other)
    {
        if (!other.CompareTag("Bolt")) return;
        if (!IsLocalHeld) return;
        if (_needRelease) return;   // 하나를 끝냈으면 버튼을 놓았다 다시 눌러야 다음 볼트(휠 너트가 누른 채로 줄줄이 풀리던 문제)

        // 감지 로그는 검증 로그(LogChanged)와 키를 따로 둔다 — 한 키를 같이 쓰면 다른 단계 볼트에 대고 있는 동안
        // OnTriggerStay 마다 '감지'↔'단계 불일치'가 번갈아 키를 바꿔 초당 수백 줄이 찍혔다(11/16)
        if (s_lastHitLog != other.name) { s_lastHitLog = other.name; Debug.Log($"<color=white>[ImpactWrench]</color> 볼트 충돌 감지: {other.name}. 검증 시작..."); }

        Bolt detectedBolt = other.GetComponent<Bolt>();
        if (detectedBolt == null)
        {
            Debug.LogError($"<color=red>[ImpactWrench]</color> {other.name}에 Bolt 스크립트가 없습니다!");
            return;
        }

        // 이미 작업이 끝난 볼트는 대상으로 삼지 않는다 (진행도 UI가 100%로 남는 것 방지)
        if (detectedBolt.isloosened) return;

        // 검증 로직 실행 — 다른 단계의 볼트면 노랑으로 깜빡이고 지금 단계를 알려 준다(조용히 무시하면 '왜 안 풀리지?'에서 막혔다)
        // 이웃 그룹 볼트가 트리거에 스치기만 해도 경고가 뜨던 문제(11/16) — 바로 띄우지 않고 후보로만 둔다.
        // 실제로 맞는 볼트를 못 잡은 채, 그 볼트에 대고 작업 버튼을 누르고 있을 때만 UpdateBlockedHint 가 띄운다.
        if (!IsCorrectBoltTask(detectedBolt)) { _blockedCandidate = detectedBolt; return; }

        // 일치할 경우에만 아래 로직 실행
        _targetBolt = detectedBolt;
        _currentBoltGameObject = other.gameObject;
        LocalTargetBolt = detectedBolt;

        if (_currentOutlinable != null) RestoreBoltOutline();
        ChangeBoltOutlineColor();

        string actionName = _targetBolt.isAssembleMode ? "볼트 체결" : "볼트 분해";
        ShowProgress(actionName, GetPlatformInstruction(actionName, false));
        if (BoltHint.NeedsLube(_targetBolt)) BoltHint.NoticeSeized();
    }

    private Bolt _blockedCandidate;
    private float _blockedPressT;

    private void UpdateBlockedHint()
    {
        var b = _blockedCandidate;
        if (b == null || _targetBolt != null || !_buttonHeld || !b.gameObject.activeInHierarchy) { _blockedPressT = 0f; return; }
        _blockedPressT += Time.deltaTime;
        if (_blockedPressT < 0.35f) return;
        _blockedPressT = -999f;   // 누르는 동안 한 번만
        BoltHint.ShowBlocked(b);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Bolt")) return;
        if (_blockedCandidate != null && _blockedCandidate.gameObject == other.gameObject) _blockedCandidate = null;

        if (_currentBoltGameObject != null && _currentBoltGameObject == other.gameObject)
        {
            // 손이 볼트에서 벗어난 게 아니라 볼트가 치워져서(콜라이더 꺼짐·비활성) 빠진 거면 위 Update 와 같이 손을 떼야 다음 볼트
            if (_buttonHeld && (!other.enabled || !other.gameObject.activeInHierarchy)) _needRelease = true;
            ClearTarget();
        }
    }

    private void ChangeBoltOutlineColor()
    {
        if (_currentBoltGameObject == null) return;

        _currentOutlinable = _currentBoltGameObject.GetComponent<Outlinable>();
        if (_currentOutlinable == null)
        {
            _currentOutlinable = _currentBoltGameObject.AddComponent<Outlinable>();
            _currentOutlinable.AddAllChildRenderersToRenderingList();
        }

        _currentOutlinable.enabled = true;
        var parameters = _currentOutlinable.OutlineParameters;
        parameters.Color = BoltHint.WorkColor(_targetBolt);   // 초록 = 지금 작업 가능, 갈색 = 녹슨 볼트(방청제 필요)
        parameters.BlurShift = outlineWidth;
        parameters.Enabled = true;
    }

    private void RestoreBoltOutline()
    {
        if (_currentOutlinable == null) return;
        _currentOutlinable.OutlineParameters.Color = RestoreColorFor(_currentOutlinable);
        _currentOutlinable = null;
    }

    // 체결을 끝낸 볼트는 흰색이 아니라 초록(완료 표시)으로 되돌린다
    private Color RestoreColorFor(Outlinable o)
    {
        var bolt = o != null ? o.GetComponent<Bolt>() : null;
        return bolt != null && bolt.IsMarkedDone ? Bolt.DoneOutlineColor : restoreOutlineColor;
    }

    private void RestoreBoltOutlineForced()
    {
        if (_currentOutlinable != null)
        {
            _currentOutlinable.OutlineParameters.Color = RestoreColorFor(_currentOutlinable);
            _currentOutlinable = null;
        }
        if (LocalTargetBolt == _targetBolt) LocalTargetBolt = null;
        _targetBolt = null;
        _currentBoltGameObject = null;
        HideRing();
    }

    // ── 진행 링 ─────────────────────────────────────

    // ── 볼트에 착 붙기 (VR) ─────────────────────────
    // CMS VR 처럼 렌치가 볼트를 겨누면 비트 끝이 볼트 머리에 맞춰 붙고, 볼트가 빠져나오는 동안 렌치도 같이 따라 나온다.
    // 손(루트)은 그대로 두고 모델(본체·비트 메시)과 쥔 손의 모델만 옮긴다(손 모델은 그리는 동안만). 비트는 Animator 가 매 프레임 쓰므로 현재 값 기준,
    // 본체는 애니메이션이 없어 시작 때 값 기준으로 매번 다시 계산한다(누적 방지).
    private const float SnapMaxDist = 0.08f;    // 이보다 멀면 붙이지 않는다(m)
    private const float SnapMaxAngle = 45f;     // 비트 방향과 볼트 축이 이보다 어긋나면 붙이지 않는다
    private Transform _snapBody, _snapBit;
    private Vector3 _bodyPos0, _tipLocal;
    private Vector3 _snapPivot, _gripLocal;   // 정렬 회전 중심(루트 로컬) / 손잡이(손목) 위치
    private Quaternion _bodyRot0;
    private bool _snapReady, _snapApplied;
    private float _snapW;
    // 손이 붙는 기준(Auto Hand 잡기 포즈) — 모델과 같이 옮겨야 PC 아바타 손목(SetWristSnap)·팔 IK 가 드릴을 따라온다
    private Transform[] _snapPoses;
    private Vector3[] _snapPosePos;
    private Quaternion[] _snapPoseRot;

    private void InitBoltSnap()
    {
        if (_snapReady) return;
        _snapReady = true;
        MeshFilter big = null, small = null;
        float bigV = -1f, smallV = float.MaxValue;
        foreach (Transform c in transform)
        {
            var mf = c.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var sz = mf.sharedMesh.bounds.size;
            float v = sz.x * sz.y * sz.z;
            if (v > bigV) { bigV = v; big = mf; }
            if (v < smallV) { smallV = v; small = mf; }
        }
        if (big == null || small == null || big == small) return;
        _snapBody = big.transform;
        _snapBit = small.transform;
        var gps = GetComponentsInChildren<Autohand.GrabbablePose>(true);
        _snapPoses = new Transform[gps.Length];
        _snapPosePos = new Vector3[gps.Length];
        _snapPoseRot = new Quaternion[gps.Length];
        for (int i = 0; i < gps.Length; i++)
        {
            var t = gps[i].transform;
            _snapPoses[i] = t;
            _snapPosePos[i] = transform.InverseTransformPoint(t.position);
            _snapPoseRot[i] = Quaternion.Inverse(transform.rotation) * t.rotation;
        }
        _bodyPos0 = _snapBody.localPosition;
        _bodyRot0 = _snapBody.localRotation;
        var mb = small.sharedMesh.bounds;
        _tipLocal = transform.InverseTransformPoint(_snapBit.TransformPoint(mb.center + Vector3.right * mb.extents.x));
        // 손잡이 위치 — 아직 정렬 전(포즈가 원위치)이라 잡기 포즈의 손목 위치를 그대로 쓴다
        if (_sync == null) _sync = GetComponentInParent<SyncGrab>();
        _gripLocal = _sync != null ? transform.InverseTransformPoint(_sync.HandTargetPoint(true)) : Vector3.zero;
        _snapPivot = _tipLocal;
    }

    private void UpdateBoltSnap()
    {
        // PC·모바일도 정렬한다 — 렌치를 마음대로 기울일 수 없으므로 각도 제한 없이, 볼트를 잡은 거리(30cm) 안이면 비트를 볼트 축에 맞춘다
        bool vr = VrDevice.IsActive;
        float maxDist = vr ? SnapMaxDist : 0.3f, maxAng = vr ? SnapMaxAngle : 179f;
        InitBoltSnap();
        if (_snapBody == null) return;

        // 목표: 비트 축(루트 +X)을 볼트가 빠지는 반대 방향으로, 비트 끝을 볼트 머리로
        Quaternion rFull = Quaternion.identity;
        Vector3 dFull = Vector3.zero;
        bool want = false;
        // 11/16 VR 테스트: 정렬하면서 렌치 모델이 최대 8cm·45° 옮겨져 쥔 손에서 떨어져 보였다(손 모델 보정이 아바타 IK 와 어긋남).
        // VR 은 손에 쥔 그대로 두고(볼트 링·진행도는 그대로), 정렬은 PC·모바일만 한다.
        if (vr) { if (_snapApplied || _snapW > 0f) { ResetBoltSnap(); RestoreHandVisuals(); } return; }
        if (_targetBolt != null && _targetBolt.gameObject.activeInHierarchy && IsLocalHeld)
        {
            var bt = _targetBolt.transform;
            Vector3 outDir = bt.parent != null ? bt.parent.TransformDirection(Vector3.down) : Vector3.down;   // Bolt 가 빠져나오는 방향
            outDir.Normalize();
            Vector3 head = BoltHead(_targetBolt, outDir);
            Vector3 tipWorld = transform.TransformPoint(_tipLocal);
            Vector3 axisWorld = transform.TransformDirection(Vector3.right).normalized;
            float ang = Vector3.Angle(axisWorld, -outDir);
            // 이미 붙어 있으면 8cm 더 멀어질 때까지 유지 — 경계(30cm)에서 Q/E 로 높이를 바꾸면 렌치·손이 붙었다 떨어졌다 하며 앞뒤로 튀어 보였다
            float distLim = _snapW > 0f ? maxDist + 0.08f : maxDist;
            if (ang < maxAng && Vector3.Distance(tipWorld, head) < distLim)
            {
                _snapPivot = _tipLocal;
                Vector3 targetL = transform.InverseTransformDirection(-outDir).normalized;
                rFull = Quaternion.FromToRotation(Vector3.right, targetL);
                dFull = transform.InverseTransformPoint(head) - _tipLocal;
                want = true;
            }
        }

        _snapW = Mathf.MoveTowards(_snapW, want ? 1f : 0f, Time.deltaTime * (want ? 8f : 6f));
        if (_snapW > 0f && IsLocalHeld) s_snapOwner = this;
        else if (s_snapOwner == this) s_snapOwner = null;
        if (_snapW <= 0f)
        {
            if (_snapApplied) ResetBoltSnap();
            return;
        }
        if (want) { _lastR = rFull; _lastD = dFull; }   // 놓을 때는 마지막 자세에서 부드럽게 풀린다

        Quaternion r = Quaternion.Slerp(Quaternion.identity, _lastR, _snapW);
        Vector3 d = _lastD * _snapW;
        ApplySnap(_snapBody, _bodyPos0, _bodyRot0, r, d);
        ApplySnap(_snapBit, _snapBit.localPosition, _snapBit.localRotation, r, d);   // Animator 가 막 쓴 값 기준
        ApplySnapPoses(r, d);
        _snapApplied = true;
        OffsetHandVisuals(r, d);
    }

    // ── 렌치를 쥔 손 모델도 같이 옮기기 ─────────────
    // 렌치 모델만 볼트 쪽으로 옮기면 손에서 떨어져 보였다. 보이는 손은 아바타 장갑(손 뼈에 스킨)이고, 잡은 Hand 가 곧 그 손 뼈다.
    // 그래서 같은 이동·회전을 손 뼈(Hand) 자체에 '그리기 직전'(IK 가 끝난 뒤)에만 적용하고, 그린 직후 원래대로 돌려놓는다.
    // 손의 물리·추적·포인터 판정 위치는 건드리지 않는다.
    // (예전: 손 아래 '렌더러가 있는 자식'을 옮겼는데 그게 장갑이 아니라 UI·그랩 포인터여서 손은 안 따라오고 포인터만 어긋났다)
    private readonly System.Collections.Generic.List<Transform> _handVis = new System.Collections.Generic.List<Transform>();
    private readonly System.Collections.Generic.List<Vector3> _handVisPos = new System.Collections.Generic.List<Vector3>();
    private readonly System.Collections.Generic.List<Quaternion> _handVisRot = new System.Collections.Generic.List<Quaternion>();
    private bool _handRestoreHooked;
    private Autohand.Grabbable _grabbable;
    private bool _handOffsetWanted;
    private Quaternion _handOffR = Quaternion.identity;
    private Vector3 _handOffD;
    private int _handOffFrame = -1;

    private void OffsetHandVisuals(Quaternion r, Vector3 d)
    {
        _handOffsetWanted = true;
        _handOffR = r; _handOffD = d;
        _handOffFrame = Time.frameCount;
        if (!_handRestoreHooked)
        {
            _handRestoreHooked = true;
            Application.onBeforeRender += OnBeforeRenderHands;
            UnityEngine.Rendering.RenderPipelineManager.endContextRendering += OnEndRenderingHands;
        }
    }

    // 손 뼈는 그리는 동안만 옮기고 그린 직후 되돌린다(잡는 동안 VRIK 가 그 뼈를 다시 쓰지 않아 그대로 두면 오프셋이 쌓였다)
    private void OnEndRenderingHands(UnityEngine.Rendering.ScriptableRenderContext ctx, System.Collections.Generic.List<Camera> cams) => RestoreHandVisuals();

    // 보이는 손은 VRIK 아바타의 손 뼈다(AutoHand Hand 는 물리·추적용이라 옮겨도 장갑이 안 따라왔다).
    // 모든 LateUpdate(VRIK 풀이) 뒤, 스키닝 전에 불리는 onBeforeRender 에서 아바타 손 뼈를 렌치와 같은 만큼 옮긴다 — 다음 프레임 VRIK 가 다시 쓰므로 되돌릴 필요 없다.
    private void OnBeforeRenderHands()
    {
        if (this == null || !isActiveAndEnabled) return;
        RestoreHandVisuals();
        if (!_handOffsetWanted || _handOffFrame < Time.frameCount - 1 || !_snapApplied) return;
        if (_grabbable == null) _grabbable = GetComponentInParent<Autohand.Grabbable>();
        if (_grabbable == null) return;
        var heldBy = _grabbable.GetHeldBy();
        if (heldBy == null || heldBy.Count == 0) return;

        Vector3 pivotW = transform.TransformPoint(_snapPivot);
        Quaternion rotW = transform.rotation * _handOffR * Quaternion.Inverse(transform.rotation);
        Vector3 dW = transform.TransformVector(_handOffD);
        foreach (var h in heldBy)
        {
            if (h == null) continue;
            var bone = AvatarHandBone(h);
            if (bone == null) continue;
            _handVis.Add(bone); _handVisPos.Add(bone.localPosition); _handVisRot.Add(bone.localRotation);
            bone.SetPositionAndRotation(rotW * (bone.position - pivotW) + pivotW + dW, rotW * bone.rotation);
        }
    }

    // 아바타 손 뼈 찾기는 ToolRenderSnap 공용 캐시를 쓴다(못 찾았을 때 매 렌더 프레임 재검색 방지).
    // (11/13 버그) 예전엔 VRIK references.left/rightHand 를 옮겼는데, AutoHandVRIK 는 잡는 동안 그 참조를
    // AutoHand 손의 handGrabPoint 로 바꿔 끼워 잡기 조인트가 렌치를 끌고 다녔다 → Animator 의 실제 손 뼈만 옮긴다.
    private Transform AvatarHandBone(Autohand.Hand h) => ToolRenderSnap.FindAvatarHandBone(h);

    private void RestoreHandVisuals()
    {
        for (int i = 0; i < _handVis.Count; i++)
            if (_handVis[i] != null) { _handVis[i].localPosition = _handVisPos[i]; _handVis[i].localRotation = _handVisRot[i]; }
        _handVis.Clear(); _handVisPos.Clear(); _handVisRot.Clear();
    }

    private Quaternion _lastR = Quaternion.identity;
    private Vector3 _lastD;

    private void ApplySnap(Transform t, Vector3 basePos, Quaternion baseRot, Quaternion r, Vector3 d)
    {
        t.localPosition = r * (basePos - _snapPivot) + _snapPivot + d;
        t.localRotation = r * baseRot;
    }

    /// <summary>잡기 포즈 기준점도 모델과 같은 이동·회전(루트 로컬 기준)으로 옮긴다. r = identity, d = 0 이면 원위치.</summary>
    private void ApplySnapPoses(Quaternion r, Vector3 d)
    {
        if (_snapPoses == null) return;
        for (int i = 0; i < _snapPoses.Length; i++)
        {
            var t = _snapPoses[i];
            if (t == null) continue;
            Vector3 lp = r * (_snapPosePos[i] - _snapPivot) + _snapPivot + d;
            t.SetPositionAndRotation(transform.TransformPoint(lp), transform.rotation * (r * _snapPoseRot[i]));
        }
    }

    private void ResetBoltSnap()
    {
        _snapW = 0f;
        if (!_snapApplied) return;
        _snapApplied = false;
        if (_snapBody != null) { _snapBody.localPosition = _bodyPos0; _snapBody.localRotation = _bodyRot0; }
        ApplySnapPoses(Quaternion.identity, Vector3.zero);
        // 비트는 다음 프레임 Animator 가 원래 값으로 되돌린다
    }

    private static Vector3 BoltHead(Bolt bolt, Vector3 outDir)
    {
        var r = bolt.GetComponentInChildren<Renderer>();
        if (r == null) return bolt.transform.position;
        var b = r.bounds;
        Vector3 e = b.extents;
        float along = Mathf.Abs(e.x * outDir.x) + Mathf.Abs(e.y * outDir.y) + Mathf.Abs(e.z * outDir.z);
        return b.center + outDir * along;
    }

    private void LateUpdate()
    {
        UpdateBoltSnap();
        if (_targetBolt == null || !_targetBolt.gameObject.activeInHierarchy || !IsLocalHeld) { HideRing(); return; }
        DrawRing(_targetBolt);
    }

    private void DrawRing(Bolt bolt)
    {
        if (_progRing == null) { _progTrack = MakeRing("[BoltProgressTrack]"); _progRing = MakeRing("[BoltProgressRing]"); }
        if (_progRing == null) return;

        // 볼트 크기에 맞춘 반지름
        Bounds b = default; bool any = false;
        foreach (var r in bolt.GetComponentsInChildren<Renderer>(false))
        {
            if (r is LineRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        Vector3 c = any ? b.center : bolt.transform.position;
        float radius = Mathf.Clamp(any ? b.extents.magnitude * 1.1f : 0.025f, 0.018f, 0.06f);

        var cam = Camera.main;
        Vector3 n = cam != null ? (cam.transform.position - c).normalized : bolt.transform.up;
        Vector3 u = Vector3.Cross(n, Vector3.up);
        if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(n, Vector3.right);
        u.Normalize();
        Vector3 v = Vector3.Cross(n, u);

        float p = Mathf.Clamp01(bolt.Progress);
        SetArc(_progTrack, c, u, v, radius, 1f, new Color(1f, 1f, 1f, 0.28f));
        SetArc(_progRing, c, u, v, radius, Mathf.Max(p, 0.001f), BoltHint.WorkColor(bolt));
        _progRing.widthMultiplier = _progTrack.widthMultiplier = Mathf.Clamp(radius * 0.18f, 0.0025f, 0.007f);
    }

    private static void SetArc(LineRenderer lr, Vector3 c, Vector3 u, Vector3 v, float r, float frac, Color col)
    {
        if (!lr.gameObject.activeSelf) lr.gameObject.SetActive(true);
        lr.startColor = lr.endColor = col;
        int count = Mathf.Max(2, Mathf.CeilToInt(48 * frac) + 1);
        lr.positionCount = count;
        for (int i = 0; i < count; i++)
        {
            float a = Mathf.PI * 0.5f - Mathf.PI * 2f * frac * i / (count - 1);   // 12시 방향에서 시계 방향으로 찬다
            lr.SetPosition(i, c + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r);
        }
    }

    private static LineRenderer MakeRing(string goName)
    {
        var go = new GameObject(goName);
        go.layer = 1;   // TransparentFX — VR·PC 카메라 모두 그림
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        var sh = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
        var m = new Material(sh);
        m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
        m.renderQueue = 4000;
        lr.material = m;
        go.SetActive(false);
        return lr;
    }

    private void HideRing()
    {
        if (_progRing != null && _progRing.gameObject.activeSelf) _progRing.gameObject.SetActive(false);
        if (_progTrack != null && _progTrack.gameObject.activeSelf) _progTrack.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_handRestoreHooked)
        {
            Application.onBeforeRender -= OnBeforeRenderHands;
            UnityEngine.Rendering.RenderPipelineManager.endContextRendering -= OnEndRenderingHands;
            _handRestoreHooked = false;
        }
        RestoreHandVisuals();
        if (_progRing != null) Destroy(_progRing.gameObject);
        if (_progTrack != null) Destroy(_progTrack.gameObject);
    }

    // ── 외부 연동 ─────────────────────────────────────

    public void VRPress() => _buttonHeld = true;
    public void VRRelease() => _buttonHeld = false;

    public void ToggleMode()
    {
        currentMode = (currentMode == WrenchMode.Unscrew) ? WrenchMode.Screw : WrenchMode.Unscrew;
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (modeIndicator != null)
            modeIndicator.material.color = (currentMode == WrenchMode.Unscrew) ? unscrewColor : screwColor;
    }

    private void TriggerBoltAnimation(GameObject boltGO)
    {
        // 대상 해제(아웃라인 복구·진행도 UI 숨김)는 호출한 쪽의 ClearTarget()이 담당한다.
        var ca = boltGO.GetComponent<ClickableAnimator>();
        if (ca == null || ca.IsOpen) return;

        ca.Open();
    }

    private string GetPlatformInstruction(string actionName, bool isWorking)
    {
        PlatformType platform = CachedPlatform;
        if (isWorking)
        {
            return platform switch
            {
                PlatformType.VR => "트리거를 계속 당겨 작업을 완료하세요.",
                PlatformType.PC => $"{workKey} 키를 계속 눌러 작업을 완료하세요.",
                PlatformType.Mobile => "작업 버튼을 계속 누르고 있어 작업을 완료하세요.",
                _ => "작업을 완료할 때까지 유지하세요."
            };
        }
        else
        {
            return platform switch
            {
                PlatformType.VR => $"트리거를 당겨 {actionName}를 시작하세요.",
                PlatformType.PC => $"{workKey} 키를 눌러 {actionName}를 시작하세요.",
                PlatformType.Mobile => $"화면의 작업 버튼을 눌러 {actionName}를 시작하세요.",
                _ => $"{actionName}를 시작하세요."
            };
        }
    }
}