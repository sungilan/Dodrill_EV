using UnityEngine;

public class MeasurementPoint : MonoBehaviour
{
    [Tooltip("NetworkObjectFinder 키 — JSON targetObjName 과 일치")]
    public string terminalId;

    [Header("값 설정")]
    [Tooltip("이 단자의 '정상' 전압값 (V)")]
    public float voltageValue = 0f;

    [Tooltip("이 단자의 저항값 (Ω). 9999999 = O.L(단선)")]
    public float resistanceValue = 9999999f;

    [Tooltip("고장 상태일 때의 전압값 (정상이면 voltageValue 반환)")]
    public float faultyVoltageValue = 380f;

    [Tooltip("현재 고장 상태인지")]
    public bool isFaulty = false;

    // ── 정적 조회 ─────────────────────────────

    /// <summary>두 단자 조합의 측정값 반환 — MultimeterMaster 에서 호출</summary>
    public static float GetValue(string terminalA, string terminalB, MultimeterMode mode)
    {
        // NetworkObjectFinder 로 오브젝트 찾기
        var goA = NetworkObjectFinder.Instance?.Get(terminalA);
        var goB = NetworkObjectFinder.Instance?.Get(terminalB);

        var ptA = goA != null ? goA.GetComponent<MeasurementPoint>() : null;

        if (ptA == null)
        {
            Debug.LogWarning($"[MeasurementPoint] '{terminalA}' 에 MeasurementPoint 없음");
            return 0f;
        }

        return mode switch
        {
            MultimeterMode.DC_Voltage or MultimeterMode.AC_Voltage
                => ptA.isFaulty ? ptA.faultyVoltageValue : ptA.voltageValue,

            MultimeterMode.Resistance or MultimeterMode.Continuity
                => ptA.resistanceValue,

            _ => 0f
        };
    }
}
