using UnityEngine;
using Autohand;

// ============================================================
//  GreaseTool.cs
//  구리스 스프레이 / 에어건 겸용 — 도포량 누적 → Lubricate 완료 판정.
//
//  씬 배치:
//    AirGun, GreaseSpray 프리팹 루트에 부착.
//    SyncGrab + Grabbable 도 함께 부착.
//    nozzleTip: 도구 끝 Transform (파티클 발사 위치).
//
//  동작 흐름 (VR):
//    AutoHand Squeeze → OnActivate() → 파티클 + Raycast 판정
//    대상 오브젝트에 GreaseTarget 컴포넌트가 있으면 dosingAmount 누적
//    targetValue(100) 도달 → TrainingFlowManager.ProcessStepAction(Lubricate)
//
//  동작 흐름 (PC):
//    FreeLookController 클릭 → 오브젝트 근처에 있을 때 자동 누적 (OnHoldTarget)
//
//  기존 시스템 연동:
//    InteractionEvents.FireZoneActivated() 발행으로 ScenarioRunner 도 수신.
// ============================================================

[RequireComponent(typeof(TaskItem))]
public class GreaseTool : MonoBehaviour
{
    [Header("도구 설정")]
    [Tooltip("도포 속도 (초당 포인트, targetValue=100 기준)")]
    public float dosingSpeed = 20f;

    [Tooltip("레이캐스트 최대 거리 (m)")]
    public float raycastRange = 0.3f;

    [Tooltip("파티클 시스템 (분사 이펙트, 없으면 생략)")]
    public ParticleSystem sprayParticle;

    [Tooltip("도구 끝 Transform (레이캐스트 발사 원점)")]
    public Transform nozzleTip;

    [Header("상태")]
    public float currentAmount = 0f;
    public bool  isActivated   = false;

    private GreaseTarget _currentTarget;
    private TaskItem     _taskItem;
    private Grabbable    _grabbable;

    private void Awake()
    {
        _taskItem  = GetComponent<TaskItem>();
        _grabbable = GetComponent<Grabbable>();

        if (_grabbable != null)
        {
            _grabbable.OnSqueezeEvent += OnActivate;
            _grabbable.OnUnsqueezeEvent += OnDeactivate;
        }

        if (nozzleTip == null) nozzleTip = transform;
    }

    private void OnDestroy()
    {
        if (_grabbable != null)
        {
            _grabbable.OnSqueezeEvent   -= OnActivate;
            _grabbable.OnUnsqueezeEvent -= OnDeactivate;
        }
    }

    // ── AutoHand 이벤트 ──────────────────────

    private void OnActivate(Hand hand, Grabbable grabbable)
    {
        isActivated = true;
        sprayParticle?.Play();
    }

    private void OnDeactivate(Hand hand, Grabbable grabbable)
    {
        isActivated = false;
        sprayParticle?.Stop();
    }

    // ── PC 클릭 트리거 (FreeLookController → OnPCActivate) ──

    /// <summary>FreeLookController 에서 이 도구를 들고 클릭 유지 중 호출</summary>
    public void OnPCActivate(bool active)
    {
        isActivated = active;
        if (active) sprayParticle?.Play();
        else        sprayParticle?.Stop();
    }

    // ── 업데이트 ─────────────────────────────

    private void Update()
    {
        if (!isActivated) return;

        DetectTarget();

        if (_currentTarget != null)
            ApplyDosing();
    }

    private void DetectTarget()
    {
        // 노즐 끝에서 Raycast
        Ray ray = new Ray(nozzleTip.position, nozzleTip.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, raycastRange))
        {
            var target = hit.collider.GetComponent<GreaseTarget>()
                      ?? hit.collider.GetComponentInParent<GreaseTarget>();

            if (target != _currentTarget)
            {
                _currentTarget = target;
                if (target != null)
                    Debug.Log($"[GreaseTool] 대상 감지: {target.targetId}");
            }
        }
        else
        {
            _currentTarget = null;
        }
    }

    private void ApplyDosing()
    {
        float delta = dosingSpeed * Time.deltaTime;
        _currentTarget.currentAmount = Mathf.Min(
            _currentTarget.currentAmount + delta,
            _currentTarget.targetAmount);

        // 텍스처/비주얼 업데이트
        _currentTarget.UpdateVisual(_currentTarget.currentAmount / _currentTarget.targetAmount);

        // 완료 판정
        if (_currentTarget.currentAmount >= _currentTarget.targetAmount &&
            !_currentTarget.isCompleted)
        {
            _currentTarget.isCompleted = true;
            OnDosingComplete(_currentTarget);
        }
    }

    private void OnDosingComplete(GreaseTarget target)
    {
        Debug.Log($"[GreaseTool] {target.targetId} 도포 완료");

        // TrainingFlowManager 보고
        TrainingFlowManager.Instance?.ProcessStepAction(
            RepairActionType.Lubricate,
            target.targetId,
            target.currentAmount);

        // 기존 ScenarioRunner 호환 이벤트
        InteractionEvents.FireZoneActivated(target.targetId, _taskItem?.prefabId ?? "GreaseTool");
    }

    // ── 리셋 ─────────────────────────────────

    public void ResetTool()
    {
        currentAmount  = 0f;
        isActivated    = false;
        _currentTarget = null;
        sprayParticle?.Stop();
    }
}

// ============================================================
//  GreaseTarget.cs
//  도포 대상 오브젝트 — Busbar_A1, PRA_Terminal_Set 등에 부착
// ============================================================
public class GreaseTarget : MonoBehaviour
{
    [Tooltip("NetworkObjectFinder 키 — JSON targetObjName 과 일치")]
    public string targetId;

    [Tooltip("완료로 인정하는 도포량 (GreaseTool.dosingSpeed 기준, 보통 100)")]
    public float targetAmount = 100f;

    [Header("상태 (런타임)")]
    public float currentAmount = 0f;
    public bool  isCompleted   = false;

    [Header("비주얼 피드백 (선택)")]
    [Tooltip("도포 진행에 따라 색이 변할 Renderer (없으면 생략)")]
    public Renderer targetRenderer;

    [Tooltip("도포 전 색상 (건조)")]
    public Color dryColor   = Color.gray;

    [Tooltip("도포 완료 색상 (번들거림)")]
    public Color wetColor   = Color.white;

    private void Awake()
    {
        // NetworkObjectFinder 에 자동 등록
        NetworkObjectFinder.Instance?.Register(targetId, gameObject);
    }

    /// <summary>progress: 0.0 ~ 1.0</summary>
    public void UpdateVisual(float progress)
    {
        if (targetRenderer == null) return;
        targetRenderer.material.color = Color.Lerp(dryColor, wetColor, progress);

        // Smoothness 올리기 (있는 경우)
        if (targetRenderer.material.HasProperty("_Smoothness"))
            targetRenderer.material.SetFloat("_Smoothness", Mathf.Lerp(0.2f, 0.9f, progress));
    }

    public void Reset()
    {
        currentAmount = 0f;
        isCompleted   = false;
        UpdateVisual(0f);
    }
}
