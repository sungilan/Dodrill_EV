using UnityEngine;
using TMPro;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal; // URP 기준 (Built-in이면 포스트 프로세싱 스택 사용)
using System.Collections;

public class SafetyUIHandler : MonoBehaviour
{
    public static SafetyUIHandler Instance;

    [Header("UI Elements")]
    public GameObject warningPanel;      // 경고창 오브젝트
    public TextMeshProUGUI warningText;  // 경고 문구

    [Header("Post Processing")]
    public Volume globalVolume;          // URP Volume
    private Vignette vignette;

    private Coroutine warningCoroutine;

    void Awake()
    {
        Instance = this;
        if(globalVolume.profile.TryGet(out vignette))
        {
            vignette.active = false; // 시작 시 꺼둠
        }
        warningPanel.SetActive(false);
    }

    public void TriggerWarning(string message)
    {
        if(warningCoroutine != null) StopCoroutine(warningCoroutine);
        warningCoroutine = StartCoroutine(ShowWarningRoutine(message));
    }

    private IEnumerator ShowWarningRoutine(string message)
    {
        // 1. 세팅
        warningText.text = message;
        warningPanel.SetActive(true);
        if(vignette != null)
        {
            vignette.active = true;
            vignette.color.Override(Color.red); // 빨간색 강조
            vignette.intensity.Override(0.5f);  // 강도 조절
        }

        // 2. [사운드] 경고음 재생
        // AudioSource.PlayClipAtPoint(warningSound, Camera.main.transform.position);

        // 3. 잠시 대기 (3초)
        yield return new WaitForSeconds(3f);

        // 4. 원복
        warningPanel.SetActive(false);
        if(vignette != null) vignette.active = false;
    }
}