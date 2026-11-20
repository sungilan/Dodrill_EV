using UnityEngine;

public class MultimeterDial : MonoBehaviour
{
    [Header("연결된 본체")]
    public MultimeterMaster master;

    [Header("모드 구간 (Z축 localEulerAngles 기준)")]
    public float offAngle         = 0f;
    public float dcVoltageAngle   = 45f;
    public float acVoltageAngle   = 90f;
    public float resistanceAngle  = 135f;
    public float continuityAngle  = 180f;

    [Tooltip("다이얼 단계 사이 각도 허용 오차")]
    public float tolerance = 15f;

    private MultimeterMode _lastMode = MultimeterMode.OFF;
    private AudioSource _clickAudio;

    private void Awake()
    {
        _clickAudio = GetComponent<AudioSource>();
    }

    private void Update()
    {
        float angle = transform.localEulerAngles.z;
        // 360→0 정규화
        if (angle > 180f) angle -= 360f;

        MultimeterMode mode = AngleToMode(angle);

        if (mode != _lastMode)
        {
            _lastMode = mode;
            _clickAudio?.Play();
            master?.SetMode(mode);
        }
    }

    private MultimeterMode AngleToMode(float angle)
    {
        if (Mathf.Abs(angle - offAngle)        < tolerance) return MultimeterMode.OFF;
        if (Mathf.Abs(angle - dcVoltageAngle)  < tolerance) return MultimeterMode.DC_Voltage;
        if (Mathf.Abs(angle - acVoltageAngle)  < tolerance) return MultimeterMode.AC_Voltage;
        if (Mathf.Abs(angle - resistanceAngle) < tolerance) return MultimeterMode.Resistance;
        if (Mathf.Abs(angle - continuityAngle) < tolerance) return MultimeterMode.Continuity;
        return _lastMode; // 중간값은 마지막 모드 유지
    }

    // PC/Mobile 테스트용 — 버튼에 연결
    public void CycleMode()
    {
        int next = ((int)_lastMode + 1) % System.Enum.GetValues(typeof(MultimeterMode)).Length;
        master?.SetMode((MultimeterMode)next);
    }
}

// ============================================================
//  MeasurementPoint.cs
//  측정 단자 오브젝트에 부착 — 전압/저항 값 제공
//
//  씬 배치:
//    HV_Cable_Term, Battery_Case, PRA_Coil_Pin 등 각 단자에 부착
//    + Trigger Collider (Is Trigger = true)
// ============================================================
