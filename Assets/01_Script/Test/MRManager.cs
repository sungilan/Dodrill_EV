using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using System.Collections;

public class MRManager : MonoBehaviour
{
    Rigidbody rigid;
    bool VRMode = false;

    public Image icon;
    public Sprite VRIcon;
    public Sprite MRIcon;
    public GameObject vrMapObject;

    private Camera playerCamera;
    private UniversalAdditionalCameraData cameraData;

    // 페이드 효과를 위한 변수
    public float fadeDuration = 0.5f;

    private void Start()
    {
        rigid = GetComponent<Rigidbody>();

        GameObject camObj = GameObject.FindWithTag("MainCamera");
        if(camObj != null)
        {
            playerCamera = camObj.GetComponent<Camera>();
            cameraData = camObj.GetComponent<UniversalAdditionalCameraData>();
        }

        if(playerCamera == null)
        {
            Debug.LogWarning("플레이어 카메라(MainCamera 태그)를 찾을 수 없습니다.");
        }
    }

    public void SwitchView()
    {
        StartCoroutine(SwitchViewWithFade());
    }

    private IEnumerator SwitchViewWithFade()
    {
        // 페이드 아웃 (검은색으로)
        yield return StartCoroutine(FadeScreen(1f, fadeDuration));

        // 모드 전환
        VRMode = !VRMode;

        if(VRMode)
        {
            Debug.Log("VR Mode On");
            vrMapObject.SetActive(true);
            icon.sprite = MRIcon;
            SetPostProcessing(true);
        }
        else
        {
            Debug.Log("VR Mode Off (MR Mode)");
            vrMapObject.SetActive(false);
            icon.sprite = VRIcon;
            SetPostProcessing(false);
        }

        // 페이드 인 (원래대로)
        yield return StartCoroutine(FadeScreen(0f, fadeDuration));
    }

    private IEnumerator FadeScreen(float targetAlpha, float duration)
    {
        float elapsedTime = 0f;
        Color originalColor = playerCamera.backgroundColor;

        while(elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float alpha = Mathf.Lerp(playerCamera.backgroundColor.a, targetAlpha, elapsedTime / duration);

            playerCamera.backgroundColor = new Color(
                originalColor.r,
                originalColor.g,
                originalColor.b,
                alpha
            );

            yield return null;
        }

        playerCamera.backgroundColor = new Color(originalColor.r, originalColor.g, originalColor.b, targetAlpha);
    }

    private void SetPostProcessing(bool enable)
    {
        if(cameraData != null)
        {
            cameraData.renderPostProcessing = enable;
            Debug.Log($"포스트 프로세싱: {(enable ? "활성화" : "비활성화")}");
        }
    }
}
