using Autohand;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;

/// <summary>
/// 스폰되는 모든 인터랙션 오브젝트에 붙이는 컴포넌트
/// prefabId로 InteractionEvents에 이벤트를 발생시킴
/// Grabbable이 없어도 동작 (데모/테스트용 프리미티브에서도 사용 가능)
/// </summary>
public class TaskItem : MonoBehaviour
{
    [Tooltip("ScenarioData의 prefabId와 일치해야 함")]
    public string prefabId;

    [Header("Localization")]
    [Tooltip("아이템의 표시 명칭 (가이드 마커 등에 사용)")]
    public UnityEngine.Localization.LocalizedString itemName;

    private Grabbable _grabbable;

    // --- 추가된 부분: Registry 시스템 ---
    public static Dictionary<int, TaskItem> Registry = new Dictionary<int, TaskItem>();
    private void Awake()
    {
        // Grabbable이 있으면 이벤트 연결, 없으면 스킵 (데모 큐브 등)
        _grabbable = GetComponent<Grabbable>();
        if (_grabbable != null)
        {
            _grabbable.OnGrabEvent    += OnGrabbed;
            _grabbable.OnSqueezeEvent += OnUsed;
        }
    }
    private void OnEnable()
    {
        // 인스턴스 ID를 키로 등록
        if(!Registry.ContainsKey(gameObject.GetInstanceID()))
            Registry.Add(gameObject.GetInstanceID(), this);
    }
    private void OnDisable()
    {
        // 레지스트리에서 제거
        Registry.Remove(gameObject.GetInstanceID());
    }

    private void OnDestroy()
    {
        if (_grabbable != null)
        {
            _grabbable.OnGrabEvent    -= OnGrabbed;
            _grabbable.OnSqueezeEvent -= OnUsed;
        }

        ForceReleaseAndCleanup();
    }

    private void OnGrabbed(Hand hand, Grabbable grabbable)
    {
        InteractionEvents.FireItemGrabbed(prefabId);
    }

    private void OnUsed(Hand hand, Grabbable grabbable)
    {
        InteractionEvents.FireItemUsed(prefabId);
    }

    /// <summary>ScenarioSpawner에서 스폰 직후 호출하여 ID를 설정</summary>
    public void SetPrefabId(string id) => prefabId = id;

    /// <summary>
    /// 자신을 잡고 있는 손을 강제로 해제하고 관련 UI(핸들/레이저)를 정리합니다.
    /// </summary>
    private void ForceReleaseAndCleanup()
    {
        if (_grabbable == null) return;

        // 1. PC 모드 (SyncGrab) 대응
        var syncGrab = GetComponent<SyncGrab>() ?? GetComponentInParent<SyncGrab>();
        if (syncGrab != null && syncGrab.IsGrabbed)
        {
            syncGrab.StopPCHold();     // PC 레이저 및 가이드 UI 즉시 파괴
            syncGrab.RequestRelease(); // 서버/클라이언트 물리 해제
        }

        // 2. VR 모드 (AutoHand) 대응
        if (_grabbable.IsHeld())
        {
            _grabbable.ForceHandsRelease(); // 잡고 있는 모든 VR 손에서 강제 해제
        }
    }
}
