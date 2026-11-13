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
    private bool _progressShown;   // 이 렌치가 볼트 진행도 UI를 띄운 상태인지

    /// <summary>내가 든 렌치가 지금 겨누고 있는 볼트(다음 단계 안내가 같은 볼트에 링을 겹쳐 그리지 않게 쓴다).</summary>
    public static Bolt LocalTargetBolt { get; private set; }
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
        }
        else if (CachedPlatform == PlatformType.Mobile)
        {
            _buttonHeld = MobileWorkButton.IsHeld;
        }
        SetMobileButton(_targetBolt != null);
        if (_targetBolt != null && _mobileButtonShown) MobileWorkButton.SetLabel(_targetBolt.isAssembleMode ? "볼트 조이기" : "볼트 풀기");

        bool working = _buttonHeld;
        ApplyEffects(working);
        if (working) s_lastWorkTime = Time.unscaledTime;

        if (_targetBolt != null)
        {
            // 볼트가 이미 제거(비활성화/파괴)됐다면 더 이상 대상이 아니다.
            // 비활성화된 콜라이더는 OnTriggerExit가 호출되지 않아 진행도 UI가 남던 원인.
            if (!_targetBolt.gameObject.activeInHierarchy)
            {
                ClearTarget();
                return;
            }

            // 실시간 검증: 작업 중 태스크가 바뀌는 상황 대비
            if (!IsCorrectBoltTask(_targetBolt))
            {
                ClearTarget();
                return;
            }

            // 작업이 끝난 볼트는 대기 상태로 두지 않고 대상에서 해제한다.
            if (_targetBolt.isloosened)
            {
                if (working) TriggerBoltAnimation(_targetBolt.gameObject);
                ClearTarget();
                return;
            }

            // ✅ 렌치 모드와 상관없이 볼트 자체의 모드에 따라 문구 결정
            string actionName = _targetBolt.isAssembleMode ? "볼트 체결" : "볼트 분해";

            if (working)
            {
                // 렌치 회전 방향은 물리적 일관성을 위해 currentMode 유지 가능 (필요시 수정)
                float dir = (currentMode == WrenchMode.Unscrew) ? 1f : -1f;
                if (BoltWork.IsSeized(_targetBolt) && !BoltWork.IsLubed(_targetBolt))
                    EquipFx.Notice("seized", "녹슬어 뻑뻑한 볼트", "윤활 방청제를 뿌리면 잘 풀립니다", EquipFx.Warn, 3f, 10f);
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

        ui.ShowBoltProgress(_targetBolt.Progress, title,
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

        LogChanged("hit:" + other.name, $"<color=white>[ImpactWrench]</color> 볼트 충돌 감지: {other.name}. 검증 시작...");

        Bolt detectedBolt = other.GetComponent<Bolt>();
        if (detectedBolt == null)
        {
            Debug.LogError($"<color=red>[ImpactWrench]</color> {other.name}에 Bolt 스크립트가 없습니다!");
            return;
        }

        // 이미 작업이 끝난 볼트는 대상으로 삼지 않는다 (진행도 UI가 100%로 남는 것 방지)
        if (detectedBolt.isloosened) return;

        // 검증 로직 실행
        if (!IsCorrectBoltTask(detectedBolt)) return;

        // 일치할 경우에만 아래 로직 실행
        _targetBolt = detectedBolt;
        _currentBoltGameObject = other.gameObject;
        LocalTargetBolt = detectedBolt;

        if (_currentOutlinable != null) RestoreBoltOutline();
        ChangeBoltOutlineColor();

        string actionName = _targetBolt.isAssembleMode ? "볼트 체결" : "볼트 분해";
        ShowProgress(actionName, GetPlatformInstruction(actionName, false));
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Bolt")) return;

        if (_currentBoltGameObject != null && _currentBoltGameObject == other.gameObject)
            ClearTarget();
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
        parameters.Color = WorkingOutlineColor;   // 예전 노란색(targetOutlineColor) 대신 초록 — '지금 이 볼트를 작업 중' 표시
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
    private Quaternion _bodyRot0;
    private bool _snapReady, _snapApplied;
    private float _snapW;

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
        _bodyPos0 = _snapBody.localPosition;
        _bodyRot0 = _snapBody.localRotation;
        var mb = small.sharedMesh.bounds;
        _tipLocal = transform.InverseTransformPoint(_snapBit.TransformPoint(mb.center + Vector3.right * mb.extents.x));
    }

    private void UpdateBoltSnap()
    {
        // PC·모바일도 정렬한다 — 렌치를 마음대로 기울일 수 없으므로 각도 제한 없이, 볼트를 잡은 거리(30cm) 안이면 비트를 볼트 축에 맞춘다
        bool vr = VrProbeHandling.IsVRDevice;
        float maxDist = vr ? SnapMaxDist : 0.3f, maxAng = vr ? SnapMaxAngle : 179f;
        InitBoltSnap();
        if (_snapBody == null) return;

        // 목표: 비트 축(루트 +X)을 볼트가 빠지는 반대 방향으로, 비트 끝을 볼트 머리로
        Quaternion rFull = Quaternion.identity;
        Vector3 dFull = Vector3.zero;
        bool want = false;
        if (_targetBolt != null && _targetBolt.gameObject.activeInHierarchy && IsLocalHeld)
        {
            var bt = _targetBolt.transform;
            Vector3 outDir = bt.parent != null ? bt.parent.TransformDirection(Vector3.down) : Vector3.down;   // Bolt 가 빠져나오는 방향
            outDir.Normalize();
            Vector3 head = BoltHead(_targetBolt, outDir);
            Vector3 tipWorld = transform.TransformPoint(_tipLocal);
            Vector3 axisWorld = transform.TransformDirection(Vector3.right).normalized;
            float ang = Vector3.Angle(axisWorld, -outDir);
            if (ang < maxAng && Vector3.Distance(tipWorld, head) < maxDist)
            {
                Vector3 targetL = transform.InverseTransformDirection(-outDir).normalized;
                rFull = Quaternion.FromToRotation(Vector3.right, targetL);
                dFull = transform.InverseTransformPoint(head) - _tipLocal;
                want = true;
            }
        }

        _snapW = Mathf.MoveTowards(_snapW, want ? 1f : 0f, Time.deltaTime * (want ? 8f : 6f));
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
        }
    }

    // 보이는 손은 VRIK 아바타의 손 뼈다(AutoHand Hand 는 물리·추적용이라 옮겨도 장갑이 안 따라왔다).
    // 모든 LateUpdate(VRIK 풀이) 뒤, 스키닝 전에 불리는 onBeforeRender 에서 아바타 손 뼈를 렌치와 같은 만큼 옮긴다 — 다음 프레임 VRIK 가 다시 쓰므로 되돌릴 필요 없다.
    private void OnBeforeRenderHands()
    {
        if (this == null || !isActiveAndEnabled) return;
        if (!_handOffsetWanted || _handOffFrame < Time.frameCount - 1 || !_snapApplied) return;
        if (_grabbable == null) _grabbable = GetComponentInParent<Autohand.Grabbable>();
        if (_grabbable == null) return;
        var heldBy = _grabbable.GetHeldBy();
        if (heldBy == null || heldBy.Count == 0) return;

        Vector3 pivotW = transform.TransformPoint(_tipLocal);
        Quaternion rotW = transform.rotation * _handOffR * Quaternion.Inverse(transform.rotation);
        Vector3 dW = transform.TransformVector(_handOffD);
        foreach (var h in heldBy)
        {
            if (h == null) continue;
            var bone = AvatarHandBone(h);
            if (bone == null) continue;
            bone.SetPositionAndRotation(rotW * (bone.position - pivotW) + pivotW + dW, rotW * bone.rotation);
        }
    }

    private readonly System.Collections.Generic.Dictionary<Autohand.Hand, Transform> _boneCache = new System.Collections.Generic.Dictionary<Autohand.Hand, Transform>();

    /// <summary>이 AutoHand 손에 대응하는 보이는 아바타(VRIK)의 손 뼈 — 숨은 IK 복사본은 제외, 가장 가까운 것(25cm 안).</summary>
    private Transform AvatarHandBone(Autohand.Hand h)
    {
        if (_boneCache.TryGetValue(h, out var cached) && cached != null) return cached;
        var hidden = new System.Collections.Generic.HashSet<RootMotion.FinalIK.VRIK>();
        foreach (var ah in FindObjectsByType<Autohand.AutoHandVRIK>(FindObjectsSortMode.None)) if (ah != null && ah.invisibleIK != null) hidden.Add(ah.invisibleIK);
        Transform best = null; float bestD = 0.25f * 0.25f;
        foreach (var ik in FindObjectsByType<RootMotion.FinalIK.VRIK>(FindObjectsSortMode.None))
        {
            if (ik == null || hidden.Contains(ik) || !ik.isActiveAndEnabled) continue;
            foreach (var b in new[] { ik.references.leftHand, ik.references.rightHand })
            {
                if (b == null) continue;
                float d = (b.position - h.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = b; }
            }
        }
        if (best != null) _boneCache[h] = best;
        return best;
    }

    private void RestoreHandVisuals()
    {
        for (int i = 0; i < _handVis.Count; i++)
            if (_handVis[i] != null) _handVis[i].SetPositionAndRotation(_handVisPos[i], _handVisRot[i]);
        _handVis.Clear(); _handVisPos.Clear(); _handVisRot.Clear();
    }

    private Quaternion _lastR = Quaternion.identity;
    private Vector3 _lastD;

    private void ApplySnap(Transform t, Vector3 basePos, Quaternion baseRot, Quaternion r, Vector3 d)
    {
        t.localPosition = r * (basePos - _tipLocal) + _tipLocal + d;
        t.localRotation = r * baseRot;
    }

    private void ResetBoltSnap()
    {
        _snapW = 0f;
        if (!_snapApplied) return;
        _snapApplied = false;
        if (_snapBody != null) { _snapBody.localPosition = _bodyPos0; _snapBody.localRotation = _bodyRot0; }
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
        SetArc(_progRing, c, u, v, radius, Mathf.Max(p, 0.001f), WorkingOutlineColor);
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