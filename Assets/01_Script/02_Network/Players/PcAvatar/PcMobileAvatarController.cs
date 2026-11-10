using FishNet.Object;
using UnityEngine;

/// <summary>
/// PC/모바일 플레이어 아바타 시스템의 총괄자. 루트(NetworkObject)에 붙는다.
///
///  - 소유자: 크라우치 입력 적분 → Animator float(Crouch), 카메라 피벗 높이, HeadPitch 기록,
///            루트 Y를 바닥에 고정. 로코모션 파라미터는 PcLocomotionAnimator에 위임.
///  - 원격: 아무것도 계산하지 않음. NetworkAnimator가 Move*/Speed/Crouch/HeadPitch를 실어오고,
///          PcAvatarIKDriver가 그 값으로 머리·다리 IK를 푼다.
///
/// 동기화:
///   루트 pos + yaw = 기존 LocalNetworkTransform
///   Move*/Speed/Crouch/HeadPitch = FishNet NetworkAnimator (Avatar Animator 대상)
/// </summary>
[DefaultExecutionOrder(100)]
public class PcMobileAvatarController : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private Animator avatarAnimator;
    [SerializeField] private Transform cameraPivot;
    [Tooltip("FreeLookController 또는 FreeLockControllerMobile — IPcLookController 구현체")]
    [SerializeField] private MonoBehaviour lookControllerBehaviour;
    [SerializeField] private PcLocomotionAnimator locomotion;

    [Header("눈높이 / 크라우치 (아바타 스케일 1 기준, 실제로는 스케일에 비례)")]
    [Tooltip("아바타 스케일 1일 때의 눈높이(m). 아바타 localScale에 자동으로 비례한다.")]
    [SerializeField] private float eyeHeight = 1.7f;
    [SerializeField] private float crouchDepth = 0.55f;
    [Tooltip("크라우치 0→1 도달 속도 (1/초)")]
    [SerializeField] private float crouchSpeed = 2.5f;
    [Tooltip("261026 촬영용: 선 키보다 더 올라갈 수 있는 높이(m, 아바타 스케일 1 기준). 0이면 기존처럼 서기까지만.")]
    [SerializeField] private float extraRaise = 1.6f;
    [Tooltip("261026 촬영용: 크라우치 최저보다 더 내려갈 수 있는 높이(m).")]
    [SerializeField] private float extraLower = 1.0f;
    [Tooltip("비우면 avatarAnimator의 Transform 스케일을 사용. 수동으로 스케일 계수를 강제하려면 0보다 큰 값.")]
    [SerializeField] private float avatarScaleOverride = 0f;

    [Tooltip("상호작용 클릭 시 손 뻗기 (Avatar의 PcArmReach)")]
    [SerializeField] private PcArmReach armReach;

    [Header("소유자 시점에서 숨길 렌더러")]
    [Tooltip("자기 카메라가 머리 안쪽을 보는 문제 방지 — 소유자에서만 ShadowsOnly로 전환(그림자는 유지). 보통 아바타 머리 SkinnedMeshRenderer.")]
    [SerializeField] private Renderer[] hideRenderersForOwner;
    [Tooltip("내 화면에서는 장갑(손)만 그리고 나머지 몸은 그림자만 남긴다.")]
    [SerializeField] private bool ownerHandsOnly = true;

    [Header("소유자 시점: 아래를 볼 때 윗옷 숨김 (모바일)")]
    [Tooltip("카메라가 몸통 안에 있어, 아래를 보면 윗옷의 안쪽(뒷면 컬링으로 찢어진 천처럼 보임)이 화면을 가린다. 이 각도 이상 내려다보면 윗옷을 그림자 전용으로 바꾼다(장갑은 유지).")]
    [SerializeField] private float hideUniformLookDownDeg = 32f;

    [Header("루트 접지")]
    [SerializeField] private bool pinRootToGround = true;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundProbeUp = 1.0f;
    [SerializeField] private float groundProbeDown = 3.0f;
    [SerializeField] private float rootYOffset = 0f;
    [Tooltip("접지 레이캐스트가 직전 높이보다 이만큼(m, 스케일 곱) 이상 높은 면(차체 밑면·리프트 팔·공구 위 등)에 맞으면 바닥이 아니라고 보고 무시. 차 아래에서 아바타가 공중에 뜨던 문제 대비.")]
    [SerializeField] private float maxGroundStepUp = 0.35f;

    private IPcLookController _look;
    private float _crouch01;
    private Renderer[] _uniformRenderers;
    private bool _uniformHidden;

    /// <summary>아바타 Animator (닉네임 태그가 머리 위치를 찾을 때 사용).</summary>
    public Animator AvatarAnimator => avatarAnimator;

    /// <summary>이 인스턴스가 로컬 소유자 시점인지 (IK 드라이버가 참조).</summary>
    public bool IsOwnerView => base.IsOwner;

    /// <summary>이 플레이어의 클라이언트 ID(손 모션이 SyncGrab.FindHeldBy 로 "내가 잡은 물체"를 찾을 때 사용).</summary>
    public int HolderId => base.OwnerId;

    /// <summary>표시용 크라우치 0..1. 원격은 Animator에서 다시 읽으므로 여기 값은 소유자 기준.</summary>
    public float Crouch01 => _crouch01;

    /// <summary>아바타 월드 스케일 계수 — 눈높이·크라우치·레이캐스트 거리를 이걸로 곱한다.</summary>
    public float AvatarScale
    {
        get
        {
            if (avatarScaleOverride > 0f) return avatarScaleOverride;
            return avatarAnimator != null ? Mathf.Max(0.01f, avatarAnimator.transform.lossyScale.y) : 1f;
        }
    }

    private void Awake()
    {
        // 프리팹의 카메라는 켜진 채로 저장돼 있다. 소유자만 자기 카메라를 켜야 하는데, 모바일 컨트롤러는 원격 복사본에서 아무것도 하지 않아
        // 'PC 화면에 모바일 플레이어의 머리 속 카메라가 겹쳐 그려지는' 문제가 있었다 -> 모든 복사본에서 일단 끄고 소유자 컨트롤러가 다시 켠다.
        DisableCameras();

        _look = lookControllerBehaviour as IPcLookController;
        if (_look == null && lookControllerBehaviour != null)
            Debug.LogWarning($"[PcMobileAvatarController] lookControllerBehaviour가 IPcLookController를 구현하지 않습니다: {lookControllerBehaviour.GetType().Name}", this);
    }

    private void DisableCameras()
    {
        foreach (var cam in GetComponentsInChildren<Camera>(true))
        {
            // 카메라가 루트 오브젝트에 붙은 구버전 프리팹은 오브젝트를 끄면 플레이어 전체가 사라지므로 컴포넌트만 끈다
            if (cam.gameObject == gameObject) cam.enabled = false;
            else cam.gameObject.SetActive(false);
            cam.tag = "Untagged";
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        // 서버(호스트 포함)에 있는 '남의' 플레이어 복사본도 물리로 움직이면 안 된다 — 아래 설명 참고
        // (FishNet 은 OnStartServer 안에서 IsOwner 사용을 막는다 → 소유 연결이 이 컴퓨터의 클라이언트(호스트 본인)인지로 판단)
        if (base.Owner == null || !base.Owner.IsLocalClient) FreezeRemoteBodies();
    }

    /// <summary>
    /// 원격 복사본의 Rigidbody 를 전부 kinematic 으로 바꾼다.
    /// 루트 Rigidbody 가 물리로 살아 있으면, 잡은 물체(가슴 앞)나 손 콜라이더와 부딪혀 몸이 밀려난다.
    /// 서버는 위치가 바뀌면 그 값을 모두에게 다시 보내므로, 잡은 사람이 가만히 있는 동안(새 위치를 안 보낼 때)
    /// 다른 사람 화면에서 아바타가 수 m 밀려나 물체와 떨어지고 → 손이 기본 자세로 돌아가 드릴이 안 보였다.
    /// 잡은 사람이 다시 움직이면 새 위치가 와서 제자리로 돌아왔다.
    /// </summary>
    private void FreezeRemoteBodies()
    {
        foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
        {
            if (rb == null || rb.isKinematic) continue;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // 다른 사람 화면에서 이 아바타가 든 물체가 손에 붙어 보이게 한다
        if (GetComponent<HeldObjectHandAttach>() == null) gameObject.AddComponent<HeldObjectHandAttach>();

        // 가까이 다가가면 머리 등이 사라지는 문제 대비: IK로 움직인 본이 애니메이터 경계 밖으로 나가도 렌더러가 컬링되지 않게
        if (avatarAnimator != null)
            foreach (var smr in avatarAnimator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.updateWhenOffscreen = true;

        // 원격(비소유) 복사본은 카메라·오디오 리스너를 끈 채로 둔다 (OnStartClient 시점에 다시 켜졌더라도 보장)
        if (!base.IsOwner) DisableCameras();

        if (!base.IsOwner) FreezeRemoteBodies();

        // 소유자: 자기 카메라에 자기 머리 안쪽이 보이지 않도록 지정 렌더러를 그림자 전용으로.
        if (base.IsOwner && hideRenderersForOwner != null)
        {
            foreach (var r in hideRenderersForOwner)
                if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        // 소유자(PC·모바일 모두): 윗옷 렌더러를 찾아 둔다 — 카메라가 몸통 안에 있어 내려다보면 윗옷 안쪽(속이 뚫려 보이는 면)이 화면을 가린다
        if (base.IsOwner && avatarAnimator != null)
        {
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (var r in avatarAnimator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.name.Contains("Uniform")) list.Add(r);
            _uniformRenderers = list.ToArray();

            // 내 화면에서는 손(장갑)만 보이게: 카메라 아래로 내 머리·어깨·소매가 비치던 문제.
            // 몸은 그림자만 남기고(바닥 그림자는 유지), 다른 사람 화면의 내 아바타는 그대로 보인다.
            if (ownerHandsOnly)
            {
                foreach (var r in avatarAnimator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (!r.name.Contains("Glove")) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                _uniformRenderers = new Renderer[0];   // 아래를 볼 때 윗옷 숨김/표시 토글이 다시 켜지 않게
            }
        }

        // 소유자: 물체를 누르거나(구역 신호) 집을 때만 손 뻗기 재생.
        // 단계 완료(OnTaskConfirmed)·도구 사용(OnItemUsed)에는 뻗지 않는다 — 드릴로 볼트를 풀거나 단계가 끝날 때
        // 드릴을 든 손 반대쪽 손이 앞으로 뻗었다 돌아오는 쓸데없는 동작이 나왔다.
        if (base.IsOwner)
        {
            InteractionEvents.OnZoneActivated += OnAnyZone;
            InteractionEvents.OnItemGrabbed += OnAnyInteraction;
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        InteractionEvents.OnZoneActivated -= OnAnyZone;
        InteractionEvents.OnItemGrabbed -= OnAnyInteraction;
    }

    // InteractionEvents 는 정적 이벤트라, 다른 사람(VR 등)의 행동이 이 클라이언트에서도 발행되면(구역 트리거·완료 알림 등)
    // 내 아바타도 같이 팔을 뻗었다. 내가 방금 직접 입력(클릭·터치·키)했을 때만 손을 뻗는다.
    private const float OwnInputWindow = 1.5f;
    private float _lastOwnInputTime = -10f;
    private bool _legacyInputBroken;

    private void PollOwnInput()
    {
        if (_legacyInputBroken) { _lastOwnInputTime = Time.unscaledTime; return; }   // 입력을 읽을 수 없으면 예전처럼 항상 허용
        try
        {
            bool pressed = Input.anyKeyDown;
            for (int i = 0; !pressed && i < Input.touchCount; i++)
                if (Input.GetTouch(i).phase == TouchPhase.Began) pressed = true;
            if (pressed) _lastOwnInputTime = Time.unscaledTime;
        }
        catch (System.InvalidOperationException) { _legacyInputBroken = true; }
    }

    private bool OwnInputRecently => Time.unscaledTime - _lastOwnInputTime < OwnInputWindow;

    private void OnAnyInteraction(string _) { if (OwnInputRecently) ReachForward(); }
    // 도구를 든 채 보내는 구역 신호(드릴로 볼트 풀기 등)이거나 무언가를 들고 있는 중이면 뻗지 않는다
    private void OnAnyZone(string _, string itemId)
    {
        if (!OwnInputRecently || !string.IsNullOrEmpty(itemId) || IsHoldingSomething) return;
        ReachForward();
    }

    private bool IsHoldingSomething => armReach != null && armReach.HoldWeight > 0.01f;

    /// <summary>타깃 좌표가 없는 상호작용(UI 버튼 등)에서 — 시선 앞쪽으로 손을 뻗는 제스처.</summary>
    public void ReachForward()
    {
        if (armReach == null) return;
        float s = AvatarScale;
        Vector3 origin = cameraPivot != null ? cameraPivot.position : transform.position + Vector3.up * (1.2f * s);
        Vector3 fwd = cameraPivot != null ? cameraPivot.forward : transform.forward;
        Vector3 point = origin + (fwd * 0.55f + Vector3.down * 0.12f) * s;
        RequestReach(point);
    }

    private void Update()
    {
        if (!base.IsOwner) return;
        PollOwnInput();
        float dt = Time.deltaTime;

        // ── 크라우치 적분 ─────────────────────────────
        // 261026: 카메라 높이 범위만 넓힌다 — 크라우치 값이 0..1 밖(서기 위/크라우치 아래)이어도 되고, 애니메이터엔 0..1 로 자른 값만 준다
        if (_look != null)
        {
            float lo = crouchDepth > 0.0001f ? -extraRaise / crouchDepth : 0f;
            float hi = crouchDepth > 0.0001f ? 1f + extraLower / crouchDepth : 1f;
            _crouch01 = Mathf.Clamp(_crouch01 + _look.CrouchInput * crouchSpeed * dt, lo, hi);
        }

        // ── 카메라 피벗 높이 (소유자 시점) — 아바타 스케일에 비례 ──
        if (cameraPivot != null)
        {
            Vector3 lp = cameraPivot.localPosition;
            lp.y = (eyeHeight - _crouch01 * crouchDepth) * AvatarScale;
            cameraPivot.localPosition = lp;
        }

        // ── Animator 파라미터 ─────────────────────────
        if (avatarAnimator != null)
        {
            avatarAnimator.SetFloat(PcAvatarAnimParams.CrouchHash, Mathf.Clamp01(_crouch01));
            if (_look != null)
                avatarAnimator.SetFloat(PcAvatarAnimParams.HeadPitchHash, _look.CurrentPitch);
        }

        // ── 아래를 볼 때 윗옷 숨김 (모바일) ───────────
        if (_uniformRenderers != null && _uniformRenderers.Length > 0 && cameraPivot != null)
        {
            float downSin = Mathf.Sin(hideUniformLookDownDeg * Mathf.Deg2Rad);
            float down = -cameraPivot.forward.y;
            bool want = _uniformHidden ? down > downSin - 0.08f : down > downSin;   // 경계에서 깜빡이지 않게 히스테리시스
            if (want != _uniformHidden)
            {
                _uniformHidden = want;
                var mode = want ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
                foreach (var r in _uniformRenderers) if (r != null) r.shadowCastingMode = mode;
            }
        }

        // ── 로코모션 블렌드 트리 ──────────────────────
        if (locomotion != null && avatarAnimator != null && _look != null)
            locomotion.TickOwner(avatarAnimator, _look.CurrentPlanarVelocity, transform, dt, _look.MoveSpeed);

        // ── 루트 Y 접지 (뜨지 않게) ───────────────────
        if (pinRootToGround)
            PinToGround();
    }

    // ═══════════════════════════════════════════════════
    //  팔 리치 — 상호작용 클릭 시 손 뻗기 (소유자 → 전 클라 동기화)
    // ═══════════════════════════════════════════════════

    /// <summary>소유자의 시점 컨트롤러가 상호작용 클릭 시 호출. 로컬 즉시 재생 + 원격 동기화.</summary>
    public void RequestReach(Vector3 worldPoint)
    {
        // 드릴·렌치를 작동 중일 때는 반대 손 뻗기를 생략 (F 를 누르는 것도 ‘행동’으로 잡혀 반대 손이 같이 움직이던 문제)
        if (ImpactWrench.RecentlyWorking) return;

        if (armReach != null) armReach.Reach(worldPoint, AvatarScale);
        if (base.IsClientInitialized && base.IsOwner)
            ReachServerRpc(worldPoint);
    }

    [ServerRpc(RequireOwnership = true)]
    private void ReachServerRpc(Vector3 worldPoint) => ReachObserversRpc(worldPoint);

    [ObserversRpc(ExcludeOwner = true)]
    private void ReachObserversRpc(Vector3 worldPoint)
    {
        if (armReach != null) armReach.Reach(worldPoint, AvatarScale);
    }

    private static readonly RaycastHit[] _groundHits = new RaycastHit[8];

    private void PinToGround()
    {
        float s = AvatarScale;
        float up = groundProbeUp * s;
        float down = groundProbeDown * s;
        Vector3 origin = transform.position + Vector3.up * up;

        int count = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits, up + down, groundMask, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            var h = _groundHits[i];
            // 자기 자신(루트/아바타에 달린 콜라이더)에 맞으면 무시 — 안 그러면
            // 루트 Y가 자기 콜라이더 위로 튕겨 올라가며 상하로 진동한다.
            if (h.collider != null && h.collider.transform.IsChildOf(transform)) continue;
            // 잡을 수 있는 물건·다른 플레이어(NetworkObject)와 직전 높이보다 한참 위의 면은 바닥이 아니다
            if (h.collider != null && h.collider.GetComponentInParent<NetworkObject>() != null) continue;
            if (h.point.y > transform.position.y + maxGroundStepUp * s) continue;
            if (h.distance < bestDist)
            {
                bestDist = h.distance;
                found = true;
            }
        }

        if (found)
        {
            Vector3 p = transform.position;
            p.y = (origin.y - bestDist) + rootYOffset * s;
            transform.position = p;
        }
    }
}
