using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class EmergencyEventManager : MonoBehaviour
{
    public static EmergencyEventManager Instance;

    public enum AccidentType { ThermalRunaway, ElectricShock }

    [Header("VFX Settings")]
    public GameObject smokeEffect;
    public GameObject fireEffect;
    public GameObject sparkEffect;

    [Header("Sound Names (Resources/Sounds/)")]
    public string alarmSoundName = "Siren_Loop";
    public string explosionSoundName = "Explosion_Large";
    public string electricShockSoundName = "Electric_Shock_Spark";
    public string failureBgmName = "Sad_Theme"; // 실패 시 흐를 배경음

    [Header("UI & Screen Effects")]
    public GameObject deathCanvas;
    public TMPro.TextMeshProUGUI failureReasonText;
    public Animation cameraShake;

    [Header("Flash & Fade Settings")]
    public CanvasGroup flashCanvasGroup;
    public float flashSpeed = 5f;
    public float fadeDuration = 2.0f;

    private bool isEventTriggered = false;

    /// <summary>이 기기에서 사고가 났다 (사고 팝업이 이 사람의 종료 팝업 역할을 한다)</summary>
    public bool HasAccident => isEventTriggered;

    /// <summary>사고 연출(번쩍임·폭발)이 끝났다 — 평가 결과 화면은 이때부터 띄운다</summary>
    public bool EffectsFinished { get; private set; }

    void Awake() => Instance = this;

    public void TriggerAccident(AccidentType type, string reason)
    {
        if (isEventTriggered) return;
        isEventTriggered = true;

        // 1. 기존 모든 사운드(BGM 포함) 즉시 정지
        Managers.Sound.StopAll();

        if (type == AccidentType.ThermalRunaway)
            StartCoroutine(ThermalRunawayRoutine(reason));
        else
            StartCoroutine(ElectricShockRoutine(reason));
    }

    // ⚡ 감전 사고 루틴
    private IEnumerator ElectricShockRoutine(string reason)
    {
        Debug.LogError($"[감전 사고] {reason}");

        // Managers.Sound 활용: 감전 효과음 재생
        Managers.Sound.Play(electricShockSoundName, Define.Sound.Effect);

        if (cameraShake != null) cameraShake.Play();
        if (sparkEffect != null) sparkEffect.SetActive(true);

        yield return StartCoroutine(FlashScreenRoutine(Color.red, 3));
        Managers.Sound.Play(alarmSoundName, Define.Sound.Effect);
        yield return new WaitForSeconds(1.0f);
        ShowFailureUI("감전 사고 발생!" + reason);
    }

    // 🔥 열폭주(화재) 루틴
    private IEnumerator ThermalRunawayRoutine(string reason)
    {
        if (smokeEffect != null) smokeEffect.SetActive(true);   // Game 씬에서 연결이 비어 있어 예전에는 여기서 멈췄다

        // Managers.Sound 활용: 경고 사이렌 재생 (BGM 타입으로 재생하면 자동 루프 가능)
        Managers.Sound.Play(alarmSoundName, Define.Sound.Effect);

        SafetyUIHandler.Instance?.TriggerWarning("경고: 배터리 내압 상승! 대피하십시오!");

        StartCoroutine(FlashScreenRoutine(new Color(1f, 0.5f, 0f), 1));

        yield return new WaitForSeconds(2.5f);

        // Managers.Sound 활용: 폭발음 재생
        Managers.Sound.Play(explosionSoundName, Define.Sound.Effect);

        if (fireEffect != null) fireEffect.SetActive(true);
        if (cameraShake != null) cameraShake.Play();

        yield return StartCoroutine(FlashScreenRoutine(Color.red, 5));

        yield return new WaitForSeconds(1.0f);
        ShowFailureUI("배터리 화재 발생!\n" + reason);
    }

    private void ShowFailureUI(string reason)
    {
        if (failureReasonText != null)
            failureReasonText.text = reason;

        // 실패 전용 BGM 재생 (슬픈 음악 등)
        Managers.Sound.Play(failureBgmName, Define.Sound.Bgm);

        EffectsFinished = true;
        StartCoroutine(ShowDeathCanvasWhenReady());
    }

    /// <summary>
    /// 평가 모드: 서버가 치명 판정을 하면 결과 화면(EvaluationClientUI)이 먼저 뜨고, 그걸 닫은 뒤 사고 팝업(확인 → 타이틀)을 띄운다.
    /// 결과가 안 오면(서버가 안전 상태로 판정해 기록하지 않은 경우 등) 잠깐 기다렸다가 사고 팝업을 그냥 띄운다.
    /// </summary>
    private IEnumerator ShowDeathCanvasWhenReady()
    {
        if (EvaluationMode.ClientIsEvaluation)
        {
            for (float t = 0f; t < 3f && !EvaluationClientUI.IsResultShowing; t += Time.unscaledDeltaTime) yield return null;
            while (EvaluationClientUI.IsResultShowing) yield return null;
        }
        yield return StartCoroutine(FadeInDeathCanvas());
    }

    // ✨ 화면 번쩍임 로직 (기존과 동일)
    private IEnumerator FlashScreenRoutine(Color flashColor, int repeatCount)
    {
        if (flashCanvasGroup == null) yield break;
        Image img = flashCanvasGroup.GetComponent<Image>();
        if (img != null) img.color = flashColor;

        for (int i = 0; i < repeatCount; i++)
        {
            while (flashCanvasGroup.alpha < 0.8f)
            {
                flashCanvasGroup.alpha += Time.deltaTime * flashSpeed;
                yield return null;
            }
            while (flashCanvasGroup.alpha > 0f)
            {
                flashCanvasGroup.alpha -= Time.deltaTime * flashSpeed;
                yield return null;
            }
        }
    }

    // 💀 데스 캔버스 페이드 인 (기존과 동일)
    private IEnumerator FadeInDeathCanvas()
    {
        if (deathCanvas == null) yield break;
        deathCanvas.SetActive(true);

        CanvasGroup cg = deathCanvas.GetComponent<CanvasGroup>();
        if (cg == null) cg = deathCanvas.AddComponent<CanvasGroup>();

        cg.alpha = 0f;
        cg.interactable = false;
        cg.blocksRaycasts = false;

        float elapsedTime = 0f;
        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            cg.alpha = Mathf.Lerp(0f, 1f, elapsedTime / fadeDuration);
            yield return null;
        }

        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }
}