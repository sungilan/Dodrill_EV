using UnityEngine;
using System.Collections;

public class EmergencyEventManager : MonoBehaviour
{
    public static EmergencyEventManager Instance;

    [Header("VFX & SFX")]
    public GameObject smokeEffect;      // 하얀 연기 (VFX)
    public GameObject fireEffect;       // 제트 불꽃 (VFX)
    public AudioSource alarmSound;      // 사이렌 소리
    public AudioSource explosionSound;  // 폭발음

    [Header("UI & Screen")]
    public GameObject deathCanvas;      // "미션 실패: 열 폭주 발생" UI
    public Animation cameraShake;       // 화면 흔들림 애니메이션

    private bool isEventTriggered = false;

    void Awake() => Instance = this;

    // 열 폭주 트리거 함수
    public void TriggerThermalRunaway(string reason)
    {
        if(isEventTriggered) return;
        isEventTriggered = true;

        StartCoroutine(ThermalRunawayRoutine(reason));
    }

    private IEnumerator ThermalRunawayRoutine(string reason)
    {
        Debug.LogError($"[사고 발생] {reason}");

        // 1단계: 하얀 연기 발생 및 경고음
        smokeEffect.SetActive(true);
        alarmSound.Play();
        // 화면에 짧은 경고 텍스트 띄우기
        SafetyUIHandler.Instance.TriggerWarning("경고: 배터리 내압 상승 감지! 대피하십시오!");

        yield return new WaitForSeconds(3.0f);

        // 2단계: 폭발 및 불꽃 발생
        explosionSound.Play();
        fireEffect.SetActive(true);
        if(cameraShake != null) cameraShake.Play();

        yield return new WaitForSeconds(2.0f);

        // 3단계: 시나리오 종료 및 결과 리포트
        ShowFailureUI(reason);
    }

    private void ShowFailureUI(string reason)
    {
        deathCanvas.SetActive(true);
        // 여기서 "이유: " + reason 을 UI에 표시
    }
}