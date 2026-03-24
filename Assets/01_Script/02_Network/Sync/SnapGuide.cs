using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using System.Collections;
using DG.Tweening;

/// <summary>
/// 오브젝트가 가이드 반경 안에 들어오면 스냅 + 사라지는 연출을 담당.
///
/// [흐름]
///   서버 Update에서 snapRadius 내 SyncGrab 오브젝트 감지
///       ↓
///   오브젝트 → 가이드 위치로 DOTween 스냅  (OutBack)
///   가이드   → 스케일 0으로 DOTween 사라짐 (InBack)
///       ↓
///   SyncGrab.ForceDetach() — 점유 해제 + 그랩/클릭 차단
///       ↓
///   Animator "Disappear" 있으면 클립 길이만큼 대기, 없으면 disappearDelay 대기
///       ↓
///   오브젝트 Despawn → 가이드 Despawn
///
/// [부착 위치]  가이드 오브젝트
/// [필수 컴포넌트]  NetworkObject
/// </summary>
public class SnapGuide : NetworkBehaviour
{
    // ──────────────────────────────────────────────────────
    // 인스펙터
    // ──────────────────────────────────────────────────────

    [Header("스냅 감지")]
    [Tooltip("이 거리 안에 들어온 SyncGrab 오브젝트를 스냅 대상으로 판정")]
    public float snapRadius = 0.3f;

    [Tooltip("비어 있으면 SyncGrab이 붙은 모든 오브젝트 감지. 채우면 해당 태그만 감지.")]
    public string targetTag = "";

    [Header("연출")]
    [Tooltip("오브젝트가 가이드 위치로 이동하는 시간(초)")]
    public float snapDuration = 0.2f;

    [Tooltip("가이드가 스케일 0으로 사라지는 시간(초)")]
    public float guideFadeDuration = 0.3f;

    [Tooltip("Animator 없을 때 오브젝트 Despawn 전 대기 시간(초)")]
    public float disappearDelay = 2f;

    [Header("디버그")]
    [Tooltip("씬 뷰에서 감지 반경 기즈모 표시 여부")]
    public bool showGizmo = true;

    // ──────────────────────────────────────────────────────
    // SyncVar
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// 스냅이 한 번 발동되면 true.
    /// 늦게 접속한 클라이언트가 이미 사라진 가이드를 다시 보지 않도록 처리.
    /// </summary>
    private readonly SyncVar<bool> _isTriggered = new(
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers));

    // ──────────────────────────────────────────────────────
    // 내부 상태
    // ──────────────────────────────────────────────────────

    /// <summary>Awake에서 저장한 초기 로컬 스케일. 재활성화 시 복원용.</summary>
    private Vector3 _originalScale;

    // ══════════════════════════════════════════════════════
    // 생명주기
    // ══════════════════════════════════════════════════════

    private void Awake()
    {
        _originalScale = transform.localScale;
    }

    /// <summary>클라이언트 시작 시 SyncVar 콜백 등록.</summary>
    public override void OnStartClient()
    {
        base.OnStartClient();
        _isTriggered.OnChange += OnTriggeredChanged;
    }

    /// <summary>클라이언트 종료 시 SyncVar 콜백 해제 및 DOTween 정리.</summary>
    public override void OnStopClient()
    {
        base.OnStopClient();
        _isTriggered.OnChange -= OnTriggeredChanged;
        DOTween.Kill(transform);
    }

    // ══════════════════════════════════════════════════════
    // 서버: 감지 루프
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 서버 전용 감지 루프.
    /// 매 프레임 snapRadius 내 SyncGrab 오브젝트를 순회.
    /// 이미 트리거됐으면 스킵.
    ///
    /// [주의] FindObjectsByType 비용이 우려되면
    ///        SyncGrab 생성 시 SnapGuide에 등록하는 방식으로 교체 가능.
    /// </summary>
    private void Update()
    {
        if (!IsServerInitialized || _isTriggered.Value) return;

        SyncGrab[] grabs = FindObjectsByType<SyncGrab>(FindObjectsSortMode.None);
        foreach (SyncGrab grab in grabs)
        {
            // VR/PC로 잡고 있는 오브젝트만 스냅 대상
            if (!grab.IsGrabbed) continue;

            // 태그 필터 (targetTag 비어 있으면 무시)
            if (!string.IsNullOrEmpty(targetTag) && !grab.CompareTag(targetTag)) continue;

            if (Vector3.Distance(grab.transform.position, transform.position) > snapRadius) continue;

            TriggerSnap(grab);
            break; // 첫 번째 대상만 처리
        }
    }

    // ══════════════════════════════════════════════════════
    // 스냅 트리거 (서버)
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// [서버] 스냅 시작.
    /// _isTriggered = true 설정 후 전 클라에 시각 연출 전파.
    /// 서버에서 Despawn 루틴 시작.
    /// </summary>
    [Server]
    private void TriggerSnap(SyncGrab target)
    {
        _isTriggered.Value = true;
        PlaySnapObserversRpc(target.NetworkObject, transform.position, transform.rotation);
        StartCoroutine(DespawnRoutine(target));
    }

    /// <summary>
    /// [서버] 스냅 연출 완료 후 오브젝트와 가이드를 순서대로 Despawn.
    ///
    ///  1. snapDuration 대기 (스냅 DOTween 완료)
    ///  2. ForceDetach → _isDespawning = true, 그랩 차단
    ///  3. Animator "Disappear" 또는 disappearDelay 대기
    ///  4. 오브젝트 Despawn
    ///  5. guideFadeDuration 대기
    ///  6. 가이드 Despawn
    /// </summary>
    private IEnumerator DespawnRoutine(SyncGrab target)
    {
        // 스냅 DOTween이 끝날 때까지 대기
        yield return new WaitForSeconds(snapDuration);

        // 점유 해제 + 이후 그랩/클릭 전부 차단
        target.ForceDetach();

        // 오브젝트 Animator 확인
        Animator anim = target.GetComponent<Animator>()
                     ?? target.GetComponentInChildren<Animator>();

        if (anim != null)
        {
            anim.SetTrigger("Disappear");
            yield return null; // 상태 전환 1프레임 대기

            AnimatorClipInfo[] clips = anim.GetCurrentAnimatorClipInfo(0);
            float clipLen = clips.Length > 0 ? clips[0].clip.length : disappearDelay;
            yield return new WaitForSeconds(clipLen);
        }
        else
        {
            yield return new WaitForSeconds(disappearDelay);
        }

        // 오브젝트 먼저 제거
        if (target != null && target.NetworkObject.IsSpawned)
            target.NetworkObject.Despawn();

        // 가이드 페이드 완료 후 제거
        yield return new WaitForSeconds(guideFadeDuration);

        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }

    // ══════════════════════════════════════════════════════
    // ObserversRpc — 전 클라 시각 연출
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// [ObserversRpc] 모든 클라이언트에서 스냅 연출 재생.
    ///
    ///  - 오브젝트: Rigidbody velocity 초기화 + Kinematic=true → 가이드 위치로 DOMove (OutBack)
    ///  - 가이드:   스케일 0으로 DOScale (InBack)
    /// </summary>
    [ObserversRpc]
    private void PlaySnapObserversRpc(NetworkObject targetNob, Vector3 snapPos, Quaternion snapRot)
    {
        if (targetNob == null) return;

        // 낙하 중일 수 있으므로 velocity 제거 후 Kinematic 고정
        if (targetNob.TryGetComponent(out Rigidbody rb))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // 오브젝트 스냅
        Transform targetTr = targetNob.transform;
        DOTween.Kill(targetTr);

        DOTween.Sequence()
            .Join(targetTr.DOMove(snapPos, snapDuration).SetEase(Ease.OutBack))
            .Join(targetTr.DORotateQuaternion(snapRot, snapDuration).SetEase(Ease.OutBack))
            .SetLink(targetNob.gameObject);

        // 가이드 사라짐
        DOTween.Kill(transform);
        transform.DOScale(Vector3.zero, guideFadeDuration)
            .SetEase(Ease.InBack)
            .SetLink(gameObject);
    }

    // ══════════════════════════════════════════════════════
    // SyncVar 콜백
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// _isTriggered 변경 감지.
    /// 늦게 접속한 클라이언트는 이미 트리거된 가이드를 즉시 숨김.
    /// </summary>
    private void OnTriggeredChanged(bool prev, bool next, bool asServer)
    {
        if (!next) return;
        DOTween.Kill(transform);
        transform.localScale = Vector3.zero;
    }

    // ══════════════════════════════════════════════════════
    // 디버그
    // ══════════════════════════════════════════════════════

    /// <summary>씬 뷰에서 스냅 감지 반경 시각화.</summary>
    private void OnDrawGizmosSelected()
    {
        if (!showGizmo) return;
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.25f);
        Gizmos.DrawSphere(transform.position, snapRadius);
        Gizmos.color = new Color(0f, 1f, 0.5f, 1f);
        Gizmos.DrawWireSphere(transform.position, snapRadius);
    }
}