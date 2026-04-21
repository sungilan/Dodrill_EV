using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using System.Collections;

public class MRManager : MonoBehaviour
{
    Rigidbody rigid;
    bool VRMode = true;

    public Image icon;
    public Sprite VRIcon;
    public Sprite MRIcon;
    public GameObject vrMapObject;

    private Camera playerCamera;
    private UniversalAdditionalCameraData cameraData;

    public float fadeDuration = 0.5f;

    private void Start()
    {
        rigid = GetComponent<Rigidbody>();

        // Start에서는 카메라 찾기를 하지 않음
        Debug.Log("MRManager 초기화 완료. 버튼 클릭 시 카메라를 찾습니다.");
    }

    public void SwitchView()
    {
        // 버튼 클릭 시마다 카메라를 새로 찾기
        if(playerCamera == null)
        {
            FindCamera();
        }

        if(playerCamera == null)
        {
            Debug.LogError("✗ 카메라를 찾을 수 없습니다!");
            return;
        }

        StartCoroutine(SwitchViewWithFade());
    }

    private void FindCamera()
    {
        Debug.Log("카메라를 찾는 중...");

        // 방법 1: MainCamera 태그로 찾기
        GameObject camObj = GameObject.FindWithTag("MainCamera");

        if(camObj != null)
        {
            playerCamera = camObj.GetComponent<Camera>();
            if(playerCamera != null)
            {
                Debug.Log($"✓ MainCamera 태그로 카메라 찾음: {camObj.name}");
            }
        }

        // 방법 2: 실패 시 Camera.main 사용
        if(playerCamera == null)
        {
            playerCamera = Camera.main;
            if(playerCamera != null)
            {
                Debug.Log($"✓ Camera.main으로 카메라 찾음: {playerCamera.name}");
            }
        }

        // 방법 3: 여전히 못 찾으면 현재 게임오브젝트의 자식에서 찾기
        if(playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>();
            if(playerCamera != null)
            {
                Debug.Log($"✓ 자식 오브젝트에서 카메라 찾음: {playerCamera.name}");
            }
        }

        // 방법 4: 모든 활성화된 카메라 중에서 찾기
        if(playerCamera == null)
        {
            Camera[] allCameras = FindObjectsOfType<Camera>();
            Debug.Log($"Scene의 모든 카메라: {allCameras.Length}개");

            foreach(Camera cam in allCameras)
            {
                Debug.Log($"  - {cam.gameObject.name} (태그: {cam.tag}, 활성: {cam.enabled})");

                // 첫 번째 활성화된 카메라 사용
                if(cam.enabled && playerCamera == null)
                {
                    playerCamera = cam;
                    Debug.Log($"✓ 활성화된 첫 카메라 선택: {cam.name}");
                    break;
                }
            }
        }

        // 카메라를 찾았으면 UniversalAdditionalCameraData 찾기
        if(playerCamera != null)
        {
            cameraData = playerCamera.GetComponent<UniversalAdditionalCameraData>();

            if(cameraData == null)
            {
                Debug.LogWarning($"⚠ {playerCamera.name}에 UniversalAdditionalCameraData가 없습니다.");
            }
            else
            {
                Debug.Log("✓ UniversalAdditionalCameraData 찾음");
            }
        }
    }

    private IEnumerator SwitchViewWithFade()
    {
        yield return StartCoroutine(FadeScreen(1f, fadeDuration));

        VRMode = !VRMode;

        if(VRMode)
        {
            Debug.Log("VR Mode On");
            if(vrMapObject != null)
                vrMapObject.SetActive(true);
            if(icon != null && MRIcon != null)
                icon.sprite = MRIcon;
            SetPostProcessing(true);
        }
        else
        {
            Debug.Log("VR Mode Off (MR Mode)");
            if(vrMapObject != null)
                vrMapObject.SetActive(false);
            if(icon != null && VRIcon != null)
                icon.sprite = VRIcon;
            SetPostProcessing(false);
        }

        yield return StartCoroutine(FadeScreen(0f, fadeDuration));
    }

    private IEnumerator FadeScreen(float targetAlpha, float duration)
    {
        if(playerCamera == null)
        {
            Debug.LogError("FadeScreen: playerCamera가 null입니다!");
            yield break;
        }

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
            Debug.Log($"Post Processing: {(enable ? "✓ ON" : "✗ OFF")}");
        }
        else
        {
            Debug.LogWarning("UniversalAdditionalCameraData가 없어 포스트 프로세싱을 적용할 수 없습니다.");
        }
    }
}
