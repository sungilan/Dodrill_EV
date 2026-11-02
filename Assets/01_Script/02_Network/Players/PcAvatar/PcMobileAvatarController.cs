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

    [Header("루트 접지")]
    [SerializeField] private bool pinRootToGround = true;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundProbeUp = 1.0f;
    [SerializeField] private float groundProbeDown = 3.0f;
    [SerializeField] private float rootYOffset = 0f;

    private IPcLookController _look;
    private float _crouch01;

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
        _look = lookControllerBehaviour as IPcLookController;
        if (_look == null && lookControllerBehaviour != null)
            Debug.LogWarning($"[PcMobileAvatarController] lookControllerBehaviour가 IPcLookController를 구현하지 않습니다: {lookControllerBehaviour.GetType().Name}", this);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // 소유자: 자기 카메라에 자기 머리 안쪽이 보이지 않도록 지정 렌더러를 그림자 전용으로.
        if (base.IsOwner && hideRenderersForOwner != null)
        {
            foreach (var r in hideRenderersForOwner)
                if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        // 소유자: 어떤 상호작용이든(3D 클릭 + UI 버튼 + 확인 버튼 등) 손 뻗기 재생.
        if (base.IsOwner)
        {
            InteractionEvents.OnTaskConfirmed += OnAnyInteraction;
            InteractionEvents.OnZoneActivated += OnAnyZone;
            InteractionEvents.OnItemUsed += OnAnyInteraction;
            InteractionEvents.OnItemGrabbed += OnAnyInteraction;
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        InteractionEvents.OnTaskConfirmed -= OnAnyInteraction;
        InteractionEvents.OnZoneActivated -= OnAnyZone;
        InteractionEvents.OnItemUsed -= OnAnyInteraction;
        InteractionEvents.OnItemGrabbed -= OnAnyInteraction;
    }

    private void OnAnyInteraction(string _) => ReachForward();
    private void OnAnyZone(string _, string __) => ReachForward();

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
