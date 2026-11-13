using UnityEngine;

/// <summary>시나리오와 무관한 자유 실습용 볼트 표시. BoltWork.IsCurrentStep / ImpactWrench 검사를 통과한다.</summary>
public class FreeWorkBolt : MonoBehaviour
{
    /// <summary>풀릴 때 '꽉 붙어 있다 툭 풀리는' 구간(진행도). 에어건 손맛 연출용.</summary>
    public float breakAway = 0.22f;
}
