
using UnityEngine;
using Autohand;

[RequireComponent(typeof(Collider))]
public class TaskInteractionZone : MonoBehaviour
{
    [Tooltip("JSON의 targetZoneId와 반드시 일치")]
    public string zoneId;

    [Tooltip("체크 시 시작할 때 콜라이더를 끄고 감지 기능을 차단합니다.")]
    public bool startDisabled = true;

    private Collider _col;
    private static readonly System.Collections.Generic.Dictionary<string, TaskInteractionZone> _registry = new();

    public static TaskInteractionZone Find(string id) => _registry.TryGetValue(id, out var zone) ? zone : null;

    private void Awake()
    {
        _col = GetComponent<Collider>();
        _col.isTrigger = true;

        // 전역 레지스트리에 등록 (오브젝트가 꺼져있지 않아야 등록이 확실함)
        if(!string.IsNullOrEmpty(zoneId))
            _registry[zoneId] = this;
    }

    private void Start()
    {
        // Start 시점에 설정에 따라 콜라이더 비활성화
        if(startDisabled)
        {
            Deactivate();
        }
    }

    private void OnDestroy()
    {
        if(!string.IsNullOrEmpty(zoneId))
            _registry.Remove(zoneId);
    }

    private void OnTriggerEnter(Collider other)
    {
        // 콜라이더가 꺼져있으면 물리 엔진이 이 함수를 호출하지 않지만, 
        // 로직 보호를 위해 한 번 더 체크합니다.
        if(_col != null && !_col.enabled) return;

        // 1. TaskItem 감지
        var item = other.GetComponent<TaskItem>() ?? other.GetComponentInParent<TaskItem>();
        if(item != null)
        {
            if(!IsCurrentTaskValid(item.prefabId)) return;

            Debug.Log($"<color=cyan>[Zone:{zoneId}]</color> ✅ 올바른 아이템 진입: {item.prefabId}");
            InteractionEvents.FireZoneActivated(zoneId, item.prefabId);
            return;
        }

        // 2. Hand 감지
        var hand = other.GetComponent<Hand>() ?? other.GetComponentInParent<Hand>();
        if(hand != null && hand.GetHeldGrabbable() == null)
        {
            if(!IsCurrentTaskValid("")) return;
            InteractionEvents.FireZoneActivated(zoneId, string.Empty);
        }
    }

    private bool IsCurrentTaskValid(string itemId)
    {
        var receiver = GameObject.FindAnyObjectByType<ScenarioStateReceiver>();
        if(receiver == null) return true;

        var config = receiver.GetCurrentTaskConfig();
        if(config == null) return true;

        if(!string.IsNullOrEmpty(config.targetZoneId) && config.targetZoneId != zoneId) return false;

        if(config.requiredItems != null && config.requiredItems.Count > 0)
        {
            return config.requiredItems.Contains(itemId);
        }

        return true;
    }

    // ── 외부 제어 (매니저에서 호출) ────────────────────────────────────────────

    public void Activate()
    {
        if(_col == null) _col = GetComponent<Collider>();

        _col.enabled = true; // 콜라이더 활성화
        Debug.Log($"<color=white>[Zone:{zoneId}]</color> <b>활성화(ON)</b>됨");

        // 가이드용 메쉬가 있다면 같이 켜줌 (선택 사항)
        var renderer = GetComponent<MeshRenderer>();
        if(renderer != null) renderer.enabled = true;
    }

    public void Deactivate()
    {
        if(_col == null) _col = GetComponent<Collider>();

        _col.enabled = false; // 콜라이더 비활성화
        Debug.Log($"<color=white>[Zone:{zoneId}]</color> <b>비활성화(OFF)</b>됨");

        // 가이드용 메쉬가 있다면 같이 꺼줌 (선택 사항)
        var renderer = GetComponent<MeshRenderer>();
        if(renderer != null) renderer.enabled = false;
    }
}