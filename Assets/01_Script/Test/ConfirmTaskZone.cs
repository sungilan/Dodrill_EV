using UnityEngine;
using Autohand;

[RequireComponent(typeof(Collider))]
public class TaskConfirmZone : MonoBehaviour
{
    [Tooltip("JSON의 targetZoneId와 반드시 일치")]
    public string zoneId;

    [Tooltip("체크 시 시작할 때 콜라이더를 끄고 감지 기능을 차단합니다.")]
    public bool startDisabled = true;

    private Collider _col;
    private static readonly System.Collections.Generic.Dictionary<string, TaskConfirmZone> _registry = new();

    public static TaskConfirmZone Find(string id) => _registry.TryGetValue(id, out var zone) ? zone : null;

    private void Awake()
    {
        _col = GetComponent<Collider>();
        _col.isTrigger = true;

        if(!string.IsNullOrEmpty(zoneId))
            _registry[zoneId] = this;
    }

    private void Start()
    {
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
        if(_col != null && !_col.enabled) return;

        // 1. TaskItem 감지
        var item = other.GetComponent<TaskItem>() ?? other.GetComponentInParent<TaskItem>();
        if(item != null)
        {
            if(!IsCurrentTaskValid(item.prefabId)) return;

            Debug.Log($"<color=cyan>[Zone:{zoneId}]</color> ✅ 올바른 아이템 진입: {item.prefabId}");
            InteractionEvents.FireTaskConfirmed(zoneId);  // ← zoneId 전송
            return;
        }

        // 2. Hand 감지 (빈손)
        var hand = other.GetComponent<Hand>() ?? other.GetComponentInParent<Hand>();
        if(hand != null && hand.GetHeldGrabbable() == null)
        {
            if(!IsCurrentTaskValid("")) return;
            Debug.Log($"<color=green>[Zone:{zoneId}]</color> 빈손 감지");
            InteractionEvents.FireTaskConfirmed(zoneId);  // ← zoneId 전송
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

    public void Activate()
    {
        if(_col == null) _col = GetComponent<Collider>();

        _col.enabled = true;
        Debug.Log($"<color=white>[Zone:{zoneId}]</color> <b>활성화(ON)</b>됨");

        var renderer = GetComponent<MeshRenderer>();
        if(renderer != null) renderer.enabled = true;
    }

    public void Deactivate()
    {
        if(_col == null) _col = GetComponent<Collider>();

        _col.enabled = false;
        Debug.Log($"<color=white>[Zone:{zoneId}]</color> <b>비활성화(OFF)</b>됨");

        var renderer = GetComponent<MeshRenderer>();
        if(renderer != null) renderer.enabled = false;
    }
}
