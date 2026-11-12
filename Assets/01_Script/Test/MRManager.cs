using UnityEngine;
using UnityEngine.Rendering;
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
    private bool _switching;

    private void Start()
    {
        rigid = GetComponent<Rigidbody>();

        // VR: 손가락이 버튼 안에 잠깐 머물러야 전환되게 한다.
        // 메뉴 바가 몸을 따라 아래쪽에 있어서, 바닥에 떨어진 드릴을 집으려고 손을 내리면 손가락이 MR 버튼을 스치고 지나가 MR 이 계속 바뀌었다.
        foreach(var d in GetComponentsInChildren<VrFingerUiButtonDriver>(true)) d.MinHoldSeconds = 0.35f;

        // Start에서는 카메라 찾기를 하지 않음
        Debug.Log("MRManager 초기화 완료. 버튼 클릭 시 카메라를 찾습니다.");
    }

    public void SwitchView()
    {
        if(_switching) return;   // 전환 연출 중 연타 무시

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
        _switching = true;
        yield return StartCoroutine(FadeScreen(1f, fadeDuration));

        VRMode = !VRMode;

        if(VRMode)
        {
            Debug.Log("VR Mode On");
            SetMapVisibleInMR(false);
            if(icon != null && MRIcon != null)
                icon.sprite = MRIcon;
            SetPostProcessing(true);
        }
        else
        {
            Debug.Log("VR Mode Off (MR Mode)");
            SetMapVisibleInMR(true);
            if(icon != null && VRIcon != null)
                icon.sprite = VRIcon;
            SetPostProcessing(false);
        }

        yield return StartCoroutine(FadeScreen(0f, fadeDuration));
        _switching = false;
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

    // ── MR 모드에서 작업장 모델 처리 ─────────────────────────────
    // 예전에는 MR 로 바꾸면 작업장 모델(MR_Object)을 통째로 꺼서, 같은 모델을 위에서 찍던 미니맵이 하얗게(빈 화면) 됐다.
    // 이제 모델은 켜 둔 채 렌더러만 끄고, 미니맵 카메라가 그릴 때만 바닥·벽·리프트를 잠깐 켜서 미니맵 배경은 그대로 보이게 한다.
    [Tooltip("MR 모드에서도 미니맵에는 보이게 할 자식 이름(MR_Object 바로 아래)")]
    public string[] keepOnMiniMap = { "Floor", "Wall", "Preb_Rig_CarLift_X" };

    private Renderer[] _mapRenderers;
    private Collider[] _mapColliders;
    private readonly System.Collections.Generic.List<Renderer> _miniMapRenderers = new System.Collections.Generic.List<Renderer>();
    private bool _mrHidden;

    private void SetMapVisibleInMR(bool mr)
    {
        if(vrMapObject == null) return;
        if(!vrMapObject.activeSelf) vrMapObject.SetActive(true);
        if(_mapRenderers == null)
        {
            _mapRenderers = vrMapObject.GetComponentsInChildren<Renderer>(true);
            _mapColliders = vrMapObject.GetComponentsInChildren<Collider>(true);
            _miniMapRenderers.Clear();
            foreach(Transform child in vrMapObject.transform)
                if(System.Array.IndexOf(keepOnMiniMap, child.name) >= 0)
                    _miniMapRenderers.AddRange(child.GetComponentsInChildren<Renderer>(true));
        }
        foreach(var r in _mapRenderers) if(r != null) r.enabled = !mr;
        foreach(var c in _mapColliders) if(c != null) c.enabled = !mr;

        if(mr == _mrHidden) return;
        _mrHidden = mr;
        if(mr)
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }
        else
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
        }
    }

    private static bool IsMiniMapCamera(Camera cam) => cam != null && cam.name == "MiniCam";

    private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if(!IsMiniMapCamera(cam)) return;
        foreach(var r in _miniMapRenderers) if(r != null) r.enabled = true;
    }

    private void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if(!IsMiniMapCamera(cam)) return;
        foreach(var r in _miniMapRenderers) if(r != null) r.enabled = false;
    }

    private void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
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
