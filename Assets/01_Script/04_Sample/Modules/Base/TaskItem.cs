using UnityEngine;
using Autohand;

/// <summary>
/// 스폰되는 모든 인터랙션 오브젝트에 붙이는 컴포넌트
/// prefabId로 InteractionEvents에 이벤트를 발생시킴
/// Grabbable이 없어도 동작 (데모/테스트용 프리미티브에서도 사용 가능)
/// </summary>
public class TaskItem : MonoBehaviour
{
    [Tooltip("ScenarioData의 prefabId와 일치해야 함")]
    public string prefabId;

    private Grabbable _grabbable;

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

    private void OnDestroy()
    {
        if (_grabbable != null)
        {
            _grabbable.OnGrabEvent    -= OnGrabbed;
            _grabbable.OnSqueezeEvent -= OnUsed;
        }
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
}
