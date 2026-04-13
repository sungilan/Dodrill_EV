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

    [Header("결함 상태 연출 (추가)")]
    [Tooltip("결함 발생 시 활성화될 빨간색 배경 오브젝트")]
    public GameObject faultyLcdObject;

    [Tooltip("기기 자체의 결함 여부 (외부 시나리오에서 제어 가능)")]
    public bool isFaulty = false;

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
        if(lcdDisplay == null) return;

        // 1. 프로브 접촉 상태 확인
        string red = redProbe != null ? redProbe.currentTerminalId : "";
        string black = blackProbe != null ? blackProbe.currentTerminalId : "";

        // 접촉 안 됨 -> 모든 디스플레이 초기화
        if(string.IsNullOrEmpty(red) || string.IsNullOrEmpty(black))
        {
            StopFluctuation();
            ResetDisplay();
            return;
        }

        // 2. 결함 상태(isFaulty) 체크 (핵심 로직)
        if(isFaulty)
        {
            ShowFaultyUI(true);
            return;
        }

        // 3. 정상 측정 로직
        ShowFaultyUI(false); // 정상일 땐 빨간 화면 끄기

        if(!bypassModeCheck && currentMode == MultimeterMode.OFF)
        {
            StopFluctuation();
            lcdDisplay.text = "OFF";
            return;
        }

        float baseValue = MeasurementPoint.GetValue(red, black, currentMode);
        _lastMeasuredValue = baseValue;

        DisplayValue(baseValue);
        StartFluctuation(baseValue);

        // 시나리오 이벤트 발신
        string primaryTarget = red;
        InteractionEvents.FireZoneActivated(primaryTarget, gameObject.name);
        InteractionEvents.FireValueMeasured(primaryTarget, baseValue);
    }

    /// <summary>결함 UI(빨간 화면)와 일반 LCD 텍스트 교체</summary>
    private void ShowFaultyUI(bool show)
    {
        if(faultyLcdObject != null)
            faultyLcdObject.SetActive(show);

        //if(lcdDisplay != null)
        //    lcdDisplay.gameObject.SetActive(!show);

        if(show)
        {
            StopFluctuation();
            Debug.Log("<color=red>[Multimeter]</color> 결함 상태 감지됨!");
        }
    }

    private void ResetDisplay()
    {
        ShowFaultyUI(false);
        if(lcdDisplay != null) lcdDisplay.text = "----";
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