using UnityEngine;

public class ElectricSafetyManager : MonoBehaviour
{
    public static ElectricSafetyManager Instance;

    [Header("Safety Status")]
    public bool isMSDRemoved = false; // MSD 제거 여부
    public bool isGlovesEquipped = false; // 절연 장갑 착용 여부

    void Awake()
    {
        Instance = this;
    }

    // 전원 차단 확인 함수
    public bool IsSafeToWork()
    {
        return isMSDRemoved && isGlovesEquipped;
    }
}