using UnityEngine;

// ============================================================
//  ElectricSafetyManager.cs
//  고전압 안전 상태 싱글톤.
//
//  테스트 시 빠른 우회:
//    Inspector 에서 isMSDRemoved / isGlovesEquipped 를 true 로 설정하거나
//    Debug 커맨드: EV/SetSafeMode 1
//
//  InteractablePart.CheckBoltsForUnlock() 에서 참조.
//  ElectricSafetyManager.Instance.IsSafeToWork() == false 이면
//  부품을 잡을 수 없도록 막는다.
// ============================================================

public class ElectricSafetyManager : MonoBehaviour
{
    public static ElectricSafetyManager Instance { get; private set; }

    [Header("안전 상태")]
    [Tooltip("MSD(서비스 플러그) 차단 여부")]
    public bool isMSDRemoved = false;

    [Tooltip("절연 장갑 착용 여부")]
    public bool isGlovesEquipped = false;

    [Header("테스트 편의")]
    [Tooltip("true 로 설정하면 안전 검사를 완전히 건너뜀 (테스트 전용)")]
    public bool bypassSafetyForTesting = false;

    private void Awake()
    {
        if(Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// 현재 작업이 안전한지 반환.
    /// InteractablePart 와 TrainingFlowManager 에서 호출.
    /// </summary>
    public bool IsSafeToWork()
    {
        if(bypassSafetyForTesting) return true;
        return isMSDRemoved && isGlovesEquipped;
    }

    /// <summary>안전 상태를 초기값(위험)으로 리셋</summary>
    public void ResetSafetyState()
    {
        isMSDRemoved = false;
        isGlovesEquipped = false;
    }

    /// <summary>MSD 차단 완료 — MSDPart 탈거 시 호출</summary>
    public void OnMSDRemoved()
    {
        isMSDRemoved = true;
        Debug.Log("[Safety] MSD 차단 완료");
    }

    /// <summary>절연 장갑 착용 완료 — GloveItem 착용 시 호출</summary>
    public void OnGlovesEquipped()
    {
        isGlovesEquipped = true;
        Debug.Log("[Safety] 절연 장갑 착용 완료");
    }
}