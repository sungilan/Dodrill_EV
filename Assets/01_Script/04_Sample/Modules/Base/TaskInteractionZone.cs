//using UnityEngine;
//using Autohand;

///// <summary>
///// 환자 신체 부위 또는 특정 위치에 배치하는 트리거 존
///// zoneId로 식별되며 아이템/손이 진입할 때 InteractionEvents를 발생시킴
/////
///// 씬 세팅:
/////   환자 오브젝트 하위에 빈 GameObject를 만들고 이 컴포넌트 + Collider(IsTrigger) 부착
/////   zoneId를 JSON의 targetZoneId와 일치시킬 것
/////
///// 감지 대상:
/////   1. TaskItem이 있는 오브젝트 → (zoneId, prefabId) 이벤트
/////   2. AutoHand의 Hand → (zoneId, "") 이벤트 (빈손 터치)
///// </summary>
//[RequireComponent(typeof(Collider))]
//public class TaskInteractionZone : MonoBehaviour
//{
//    [Tooltip("JSON의 targetZoneId와 반드시 일치")]
//    public string zoneId;

//    [Tooltip("비활성 상태로 시작할지 여부 (Task 시작 시 활성화)")]
//    public bool startDisabled = true;

//    private Collider _col;

//    // ── 정적 레지스트리 ──────────────────────────────────────
//    private static readonly System.Collections.Generic.Dictionary<string, TaskInteractionZone>
//        _registry = new();

//    public static TaskInteractionZone Find(string id) =>
//        _registry.TryGetValue(id, out var zone) ? zone : null;

//    // ── 생명주기 ─────────────────────────────────────────────

//    private void Awake()
//    {
//        _col           = GetComponent<Collider>();
//        _col.isTrigger = true;

//        if (!string.IsNullOrEmpty(zoneId))
//            _registry[zoneId] = this;

//        if (startDisabled) gameObject.SetActive(false);
//    }

//    private void OnDestroy()
//    {
//        if (!string.IsNullOrEmpty(zoneId))
//            _registry.Remove(zoneId);
//    }

//    // ── 트리거 감지 ──────────────────────────────────────────

//    private void OnTriggerEnter(Collider other)
//    {
//        // 1. TaskItem 감지 (아이템을 들고 존에 가져옴)
//        var item = other.GetComponent<TaskItem>()
//                   ?? other.GetComponentInParent<TaskItem>();
//        if (item != null)
//        {
//            InteractionEvents.FireZoneActivated(zoneId, item.prefabId);
//            return;
//        }

//        // 2. AutoHand Hand 감지 (빈손으로 터치)
//        var hand = other.GetComponent<Hand>()
//                   ?? other.GetComponentInParent<Hand>();
//        if (hand != null)
//        {
//            // 아이템을 들고 있으면 이미 위에서 처리됨 (item 우선)
//            // 빈손인 경우에만 발생
//            if (hand.GetHeldGrabbable() == null)
//                InteractionEvents.FireZoneActivated(zoneId, string.Empty);
//        }
//    }

//    // ── 외부 제어 ────────────────────────────────────────────

//    public void Activate()   => gameObject.SetActive(true);
//    public void Deactivate() => gameObject.SetActive(false);
//}
using UnityEngine;
using Autohand;

[RequireComponent(typeof(Collider))]
public class TaskInteractionZone : MonoBehaviour
{
    [Tooltip("JSON의 targetZoneId와 반드시 일치")]
    public string zoneId;

    [Tooltip("비활성 상태로 시작할지 여부 (Task 시작 시 활성화)")]
    public bool startDisabled = true;

    private Collider _col;
    private static readonly System.Collections.Generic.Dictionary<string, TaskInteractionZone> _registry = new();

    public static TaskInteractionZone Find(string id) => _registry.TryGetValue(id, out var zone) ? zone : null;

    private void Awake()
    {
        _col = GetComponent<Collider>();
        _col.isTrigger = true;

        if(!string.IsNullOrEmpty(zoneId))
            _registry[zoneId] = this;

        if(startDisabled) gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if(!string.IsNullOrEmpty(zoneId))
            _registry.Remove(zoneId);
    }

    // ── 트리거 감지 로직 (로그 강화) ──────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        // [로그 1] 일단 뭐가 닿았는지 무조건 출력 (범인 검거용)
        Debug.Log($"<color=yellow>[Zone:{zoneId}]</color> 무언가 진입함: <b>{other.name}</b> (Layer: {LayerMask.LayerToName(other.gameObject.layer)})");

        // 1. TaskItem 감지
        var item = other.GetComponent<TaskItem>() ?? other.GetComponentInParent<TaskItem>();
        if(item != null)
        {
            Debug.Log($"<color=cyan>[Zone:{zoneId}]</color> TaskItem 감지 성공! ID: <b>{item.prefabId}</b>");
            InteractionEvents.FireZoneActivated(zoneId, item.prefabId);
            return;
        }

        // 2. AutoHand Hand 감지
        var hand = other.GetComponent<Hand>() ?? other.GetComponentInParent<Hand>();
        if(hand != null)
        {
            if(hand.GetHeldGrabbable() == null)
            {
                Debug.Log($"<color=green>[Zone:{zoneId}]</color> 빈손 터치 감지됨.");
                InteractionEvents.FireZoneActivated(zoneId, string.Empty);
            }
            else
            {
                Debug.Log($"<color=orange>[Zone:{zoneId}]</color> 손이 들어왔으나 물건을 들고 있음. (물건 자체 처리를 기다림)");
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        Debug.Log($"<color=gray>[Zone:{zoneId}]</color> 무언가 나감: {other.name}");
    }

    // ── 외부 제어 ────────────────────────────────────────────

    public void Activate()
    {
        Debug.Log($"<color=white>[Zone:{zoneId}]</color> <b>활성화(ON)</b>됨.");
        gameObject.SetActive(true);
    }

    public void Deactivate()
    {
        Debug.Log($"<color=white>[Zone:{zoneId}]</color> 비활성화(OFF)됨.");
        gameObject.SetActive(false);
    }
}