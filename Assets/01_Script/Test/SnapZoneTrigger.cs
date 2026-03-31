using UnityEngine;

public class SnapZoneTrigger : MonoBehaviour
{
    [Tooltip("이 트리거에 반응해야 할 부품")]
    public InteractablePart targetPart;

    private void OnTriggerEnter(Collider other)
    {
        // 들어온 오브젝트가 타겟 부품이 맞는지 확인
        if(targetPart != null && other.gameObject == targetPart.gameObject)
        {
            targetPart.SetInsideSnapZone(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if(targetPart != null && other.gameObject == targetPart.gameObject)
        {
            targetPart.SetInsideSnapZone(false);
        }
    }
}