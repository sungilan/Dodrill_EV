using UnityEngine;

/// <summary>시나리오와 무관한 자유 실습용 볼트 표시. BoltWork.IsCurrentStep / ImpactWrench 검사를 통과한다.</summary>
public class FreeWorkBolt : MonoBehaviour
{
    /// <summary>풀릴 때 '꽉 붙어 있다 툭 풀리는' 구간(진행도). 에어건 손맛 연출용.</summary>
    public float breakAway = 0.22f;

    // 토크렌치 규정값(TorqueSpec). 기본은 휠 너트 — 차체 부품 볼트(CarPartService)는 매뉴얼 값으로 바꿔 넣는다
    public float torqueMin = 11f;
    public float torqueMax = 13f;
    public string specPart = "휠 너트";
    public string specSource = "승용 휠 너트 규정";

    /// <summary>에어 임팩렌치에 맞는 소켓(1=10mm, 2=17mm, 3=27mm). 휠 너트 17mm, 차체 부품 볼트 10mm.</summary>
    public int socket = 2;
}
