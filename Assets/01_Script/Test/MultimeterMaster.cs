using DG.Tweening;
using System.Collections;
using TMPro;
using UnityEngine;

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

    [Header("결함 상태 연출")]
    [Tooltip("결함 발생 시 활성화될 빨간색 배경 오브젝트")]
    [SerializeField] private CanvasGroup _faultyCanvasGroup; // 결함용 빨간 화면 캔버스 그룹
    [SerializeField] private float _fadeDuration = 0.3f;

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

        if(string.IsNullOrEmpty(red) || string.IsNullOrEmpty(black))
        {
            StopFluctuation();
            ResetDisplay();
            return;
        }

        // 2. 데이터 가져오기 (실제 수치 계산)
        float baseValue = MeasurementPoint.GetValue(red, black, currentMode);
        _lastMeasuredValue = baseValue;

        // 3. 결함 여부 판단
        bool isPointFaulty = false;
        var targetGo = GameObject.Find(red);
        if(targetGo != null)
        {
            var pt = targetGo.GetComponent<MeasurementPoint>();
            if(pt != null && pt.isFaulty) isPointFaulty = true;
        }

        // ─── [A] 결함 상태 연출 (UI만 처리하고 로직은 계속 진행) ───
        bool isAnyFaulty = this.isFaulty || isPointFaulty;
        ShowFaultyUI(isAnyFaulty);

        // OFF 모드 체크 (이 경우에만 신호 발신 없이 중단)
        if(!bypassModeCheck && currentMode == MultimeterMode.OFF)
        {
            StopFluctuation();
            lcdDisplay.text = "OFF";
            return;
        }

        // ─── [B] 수치 표시 및 신호 발신 ───

        // 결함 상태일 때는 흔들림 효과 없이 고정 수치만 표시
        if(isAnyFaulty)
        {
            StopFluctuation();
            DisplayValue(baseValue);
        }
        else
        {
            DisplayValue(baseValue);
            StartFluctuation(baseValue);
        }

        // ★ 핵심: 결함 여부와 상관없이 서버로 측정 신호를 보냅니다.
        // 이를 통해 서버(ScenarioRunner)는 "잘못된 값"이 들어온 것을 인지하고 단계를 넘기지 않거나 실패 처리를 할 수 있습니다.
        //InteractionEvents.FireZoneActivated(red, gameObject.name);
        InteractionEvents.FireValueMeasured(red, baseValue);

        Debug.Log($"[Multimeter] 신호 발신: 단자={red}, 값={baseValue}, 결함여부={isAnyFaulty}");
    }

    // 결함 UI 연출 (수정된 버전)
    private void ShowFaultyUI(bool show)
    {
        if(_faultyCanvasGroup == null) return;

        _faultyCanvasGroup.DOKill(); // 기존 트윈 제거

        if(show)
        {
            // 1. 이미 결함 화면이 떠 있는 상태라면 중복 실행 방지
            if(_faultyCanvasGroup.alpha > 0.9f) return;

            // 2. 빨간 화면 Fade In
            _faultyCanvasGroup.DOFade(1f, _fadeDuration).SetEase(Ease.OutCubic);

            // 3. 삐 소리 재생 (서버에 브로드캐스트 요청)
            // ScenarioRunner에 작성하신 소리 재생 시스템을 활용합니다.
            var runner = FindFirstObjectByType<ScenarioRunner>();
            if(runner != null)
            {
                // "Beep_Error" 소리는 클라이언트 사운드 매니저에 등록되어 있어야 함
                runner.BroadcastSound("Beep_Error", 1.0f);
            }

            StopFluctuation();
            Debug.Log("<color=red>[Multimeter]</color> 결함 감지: 삐- 소리와 함께 연출 실행");
        }
        else
        {
            // 결함 해제 시 Fade Out
            _faultyCanvasGroup.DOFade(0f, _fadeDuration).SetEase(Ease.InSine);
        }
    }

    // ── 내부 유틸 ────────────────────────────

    private void DisplayValue(float value)
    {
        if(lcdDisplay == null) return;

        string unit = currentMode switch
        {
            MultimeterMode.DC_Voltage => " V",
            MultimeterMode.AC_Voltage => " V~",
            MultimeterMode.Resistance => value > 900000f ? " O.L" : " Ω",
            MultimeterMode.Continuity => value < 10f ? " ))))" : " O.L",
            _ => ""
        };

        // 1. 텍스트 갱신
        string resultText = (currentMode == MultimeterMode.Resistance && value > 900000f) ? "O.L" :
                            (currentMode == MultimeterMode.Continuity && value < 10f) ? "))))" :
                            $"{value:F1}{unit}";

        lcdDisplay.text = resultText;

        // 2. [추가] 부드럽게 나타나는 연출 (Fade In)
        // 이미 텍스트가 보이고 있는 상태라면 굳이 다시 트윈을 돌릴 필요가 없으므로 체크
        if(lcdDisplay.alpha < 0.1f)
        {
            lcdDisplay.DOKill(); // 기존 트윈 중단
            lcdDisplay.alpha = 0f; // 시작점 강제 설정
            lcdDisplay.DOFade(1f, _fadeDuration).SetEase(Ease.OutCubic);
        }
    }

    // ResetDisplay도 함께 수정해줘야 자연스럽습니다.
    private void ResetDisplay()
    {
        ShowFaultyUI(false);

        if(lcdDisplay != null)
        {
            // 사라질 때도 스르륵 사라지게 하고 싶다면:
            lcdDisplay.DOKill();
            lcdDisplay.DOFade(0f, _fadeDuration).OnComplete(() => {
                lcdDisplay.text = "----";
            });
        }
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