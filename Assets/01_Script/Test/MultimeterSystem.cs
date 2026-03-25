using UnityEngine;
using TMPro;
using System.Collections;

// ============================================================
//  MultimeterMaster.cs
//  멀티테스터기 본체.
//
//  씬 배치:
//    멀티테스터기 프리팹 루트에 부착.
//    자식으로 MultimeterProbe(Red), MultimeterProbe(Black) 배치.
//    LCD 텍스트 TMP 연결.
//
//  측정 흐름:
//    1. 유저가 다이얼을 돌려 MultimeterMode 설정
//    2. 양쪽 프로브가 올바른 단자에 닿음
//    3. EvaluateConnection() → 값 계산 → LCD 표시
//    4. TrainingFlowManager.ProcessStepAction() 호출
//
//  기존 시스템 연동:
//    InteractionEvents.FireZoneActivated() 도 함께 발행해서
//    기존 ScenarioRunner 기반 모듈도 동시에 수신 가능.
// ============================================================

public enum MultimeterMode
{
    OFF,
    DC_Voltage,        // 직류 전압 (배터리 측정)
    AC_Voltage,        // 교류 전압
    Resistance,        // 저항 (센서/코일)
    Continuity,        // 도통 시험 (단선 확인)
}

public class MultimeterMaster : MonoBehaviour
{
    public static MultimeterMaster Instance { get; private set; }

    [Header("프로브 참조")]
    public MultimeterProbe redProbe;
    public MultimeterProbe blackProbe;

    [Header("UI")]
    public TextMeshProUGUI lcdDisplay;
    public TextMeshProUGUI modeDisplay;

    [Header("현재 모드")]
    public MultimeterMode currentMode = MultimeterMode.OFF;

    [Header("테스트 편의")]
    [Tooltip("true 면 아무 단자에나 대도 측정 가능 (모드 체크 건너뜀)")]
    public bool bypassModeCheck = false;

    // 내부 상태
    private Coroutine _fluctuateCoroutine;
    private float     _lastMeasuredValue;

    private void Awake()
    {
        // 싱글톤 선택적 — 여러 대 있을 수 있으므로 약한 참조
        if (Instance == null) Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── 다이얼 콜백 ──────────────────────────

    /// <summary>MultimeterDial 이 호출 — 모드 변경</summary>
    public void SetMode(MultimeterMode mode)
    {
        currentMode = mode;
        if (modeDisplay != null)
            modeDisplay.text = ModeLabel(mode);

        // 측정 중이면 재계산
        EvaluateConnection();
        Debug.Log($"[Multimeter] 모드 변경: {mode}");
    }

    // ── 프로브 접촉 콜백 ─────────────────────

    /// <summary>
    /// 프로브가 단자에 닿거나 떨어질 때 호출.
    /// MultimeterProbe.OnTriggerEnter/Exit 에서 자동 호출.
    /// </summary>
    public void EvaluateConnection()
    {
        if (lcdDisplay == null) return;

        string red   = redProbe   != null ? redProbe.currentTerminalId   : "";
        string black = blackProbe != null ? blackProbe.currentTerminalId : "";

        // 양쪽 프로브가 모두 접촉 중이어야 측정
        if (string.IsNullOrEmpty(red) || string.IsNullOrEmpty(black))
        {
            StopFluctuation();
            lcdDisplay.text = "----";
            return;
        }

        // 잘못된 모드 체크
        if (!bypassModeCheck && currentMode == MultimeterMode.OFF)
        {
            StopFluctuation();
            lcdDisplay.text = "OFF";
            return;
        }

        // 단자 조합에서 값 가져오기
        float baseValue = MeasurementPoint.GetValue(red, black, currentMode);
        _lastMeasuredValue = baseValue;

        // 표시 업데이트
        DisplayValue(baseValue);

        // 진동(소수점 흔들림) 시작
        StartFluctuation(baseValue);

        // ── TrainingFlowManager 에 결과 보고 ──
        var actionType = ModeToActionType(currentMode);
        string primaryTarget = red; // 빨간 프로브 단자가 주 측정 대상

        TrainingFlowManager.Instance?.ProcessStepAction(actionType, primaryTarget, baseValue);

        // ── 기존 ScenarioRunner 호환 ──
        InteractionEvents.FireZoneActivated(primaryTarget, gameObject.name);

        Debug.Log($"[Multimeter] {red}↔{black} | 모드:{currentMode} | 값:{baseValue}");
    }

    // ── 내부 유틸 ────────────────────────────

    private void DisplayValue(float value)
    {
        if (lcdDisplay == null) return;

        string unit = currentMode switch
        {
            MultimeterMode.DC_Voltage  => " V",
            MultimeterMode.AC_Voltage  => " V~",
            MultimeterMode.Resistance  => value > 900000f ? " O.L" : " Ω",
            MultimeterMode.Continuity  => value < 10f ? " ))))" : " O.L",
            _ => ""
        };

        if (currentMode == MultimeterMode.Resistance && value > 900000f)
            lcdDisplay.text = "O.L";
        else if (currentMode == MultimeterMode.Continuity && value < 10f)
            lcdDisplay.text = "))))";
        else
            lcdDisplay.text = $"{value:F1}{unit}";
    }

    private void StartFluctuation(float baseValue)
    {
        StopFluctuation();
        if (baseValue > 900000f) return; // O.L 상태는 흔들림 없음
        _fluctuateCoroutine = StartCoroutine(FluctuateRoutine(baseValue));
    }

    private void StopFluctuation()
    {
        if (_fluctuateCoroutine != null)
        {
            StopCoroutine(_fluctuateCoroutine);
            _fluctuateCoroutine = null;
        }
    }

    private IEnumerator FluctuateRoutine(float baseValue)
    {
        while (true)
        {
            float noise = baseValue * Random.Range(-0.003f, 0.003f);
            DisplayValue(baseValue + noise);
            yield return new WaitForSeconds(0.15f);
        }
    }

    private static RepairActionType ModeToActionType(MultimeterMode mode) => mode switch
    {
        MultimeterMode.DC_Voltage  => RepairActionType.MeasureVoltage,
        MultimeterMode.AC_Voltage  => RepairActionType.MeasureVoltage,
        MultimeterMode.Resistance  => RepairActionType.MeasureResistance,
        MultimeterMode.Continuity  => RepairActionType.MeasureResistance,
        _ => RepairActionType.MeasureVoltage,
    };

    private static string ModeLabel(MultimeterMode mode) => mode switch
    {
        MultimeterMode.OFF         => "OFF",
        MultimeterMode.DC_Voltage  => "V ──",
        MultimeterMode.AC_Voltage  => "V ~",
        MultimeterMode.Resistance  => "Ω",
        MultimeterMode.Continuity  => "))))",
        _ => "?"
    };
}

// ============================================================
//  MultimeterProbe.cs
//  프로브 끝 — 단자 접촉 감지
// ============================================================
public class MultimeterProbe : MonoBehaviour
{
    [Header("극성")]
    [Tooltip("true = 빨간(+), false = 검정(-)")]
    public bool isRedProbe = true;

    [Header("연결된 본체")]
    public MultimeterMaster master;

    [Tooltip("현재 접촉 중인 단자 ID (비어있으면 미접촉)")]
    public string currentTerminalId = "";

    private void OnTriggerEnter(Collider other)
    {
        // MeasurementPoint 컴포넌트가 있는 단자만 인식
        var point = other.GetComponent<MeasurementPoint>();
        if (point == null) point = other.GetComponentInParent<MeasurementPoint>();
        if (point == null) return;

        currentTerminalId = point.terminalId;

        // 피드백
        Debug.Log($"[Probe:{(isRedProbe ? "RED" : "BLK")}] 접촉: {currentTerminalId}");

        // 본체에 재계산 요청
        if (master != null) master.EvaluateConnection();
    }

    private void OnTriggerExit(Collider other)
    {
        var point = other.GetComponent<MeasurementPoint>();
        if (point == null) point = other.GetComponentInParent<MeasurementPoint>();
        if (point == null) return;

        if (currentTerminalId == point.terminalId)
        {
            currentTerminalId = "";
            if (master != null) master.EvaluateConnection();
        }
    }
}

// ============================================================
//  MultimeterDial.cs
//  다이얼 회전각 → MultimeterMode 변환
// ============================================================
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
