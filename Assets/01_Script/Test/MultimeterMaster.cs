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
//    4. InteractionEvents.FireZoneActivated() → ZoneAndMeasureModule 완료
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
    private float _lastMeasuredValue;

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

        string red = redProbe != null ? redProbe.currentTerminalId : "";
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

        // ── 시나리오 시스템 연동 수정 ──
        // 단순 ZoneActivated가 아니라, 측정값을 포함하여 이벤트를 보냅니다.
        string primaryTarget = red; // 빨간 프로브가 닿은 단자 ID

        // InteractionEvents에 측정값(baseValue)을 함께 보낼 수 있는 함수가 있다면 사용
        // 만약 FireZoneActivated만 있다면, 내부적으로 해당 값을 체크하는 로직이 필요함
        //InteractionEvents.FireZoneActivated(primaryTarget, gameObject.name);  // 기존 유지
        InteractionEvents.FireValueMeasured(primaryTarget, baseValue);

        Debug.Log($"[Multimeter] {red}↔{black} | 모드:{currentMode} | 값:{baseValue}");
    }

    // ── 내부 유틸 ────────────────────────────

    private void DisplayValue(float value)
    {
        if (lcdDisplay == null) return;

        string unit = currentMode switch
        {
            MultimeterMode.DC_Voltage => " V",
            MultimeterMode.AC_Voltage => " V~",
            MultimeterMode.Resistance => value > 900000f ? " O.L" : " Ω",
            MultimeterMode.Continuity => value < 10f ? " ))))" : " O.L",
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
        MultimeterMode.DC_Voltage => RepairActionType.MeasureVoltage,
        MultimeterMode.AC_Voltage => RepairActionType.MeasureVoltage,
        MultimeterMode.Resistance => RepairActionType.MeasureResistance,
        MultimeterMode.Continuity => RepairActionType.MeasureResistance,
        _ => RepairActionType.MeasureVoltage,
    };

    private static string ModeLabel(MultimeterMode mode) => mode switch
    {
        MultimeterMode.OFF => "OFF",
        MultimeterMode.DC_Voltage => "V ──",
        MultimeterMode.AC_Voltage => "V ~",
        MultimeterMode.Resistance => "Ω",
        MultimeterMode.Continuity => "))))",
        _ => "?"
    };
}

// ============================================================
//  MultimeterProbe.cs
//  프로브 끝 — 단자 접촉 감지
// ============================================================