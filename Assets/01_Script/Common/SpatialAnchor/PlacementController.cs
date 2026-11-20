using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlacementController_NewInput : MonoBehaviour
{
    [Header("Input Actions")]
    public InputActionProperty rightAButton;  // 오른손 A 버튼
    public InputActionProperty leftXButton;   // 왼손 X 버튼
    public InputActionProperty leftYButton;   // 왼손 Y 버튼

    [Header("Placement Settings")]
    public Transform rayOriginTransform;      // 레이 시작 기준 (예: 오른손 컨트롤러)
    public GameObject previewPrefab;
    public LayerMask placementLayerMask;
    public float maxRayDistance = 10f;
    public float rotateSpeed = 30f;
    public AnchorManager anchorManager;
    public GameObject applyButton;
    public TextMeshProUGUI logPopup;

    private GameObject previewInstance;
    private LineRenderer lineRenderer;
    private bool isPlacing = false;
    private bool isAnchorPlaceMode = false;
    private Pose placementPose;

    private void Start()
    {
        // Preview object
        previewInstance = Instantiate(previewPrefab);
        previewInstance.SetActive(false);

        // Line renderer
        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 2;
        lineRenderer.widthMultiplier = 0.01f;
        lineRenderer.material = new Material(Shader.Find("Sprites/Default")) { color = Color.green };

        // Enable inputs
        rightAButton.action.Enable();
        leftXButton.action.Enable();
        leftYButton.action.Enable(); // Y 버튼 입력 활성화
    }

    public void StartAnchorPlacement()
    {
        isAnchorPlaceMode = true;
        StartCoroutine(ShowPopupMessage("오른쪽 컨트롤러 A 버튼을 꾹 눌러 앵커 배치를 시작하세요."));

    }

    private void Update()
    {
        if (isAnchorPlaceMode == false)
            return;
        bool aPressed = rightAButton.action.IsPressed();
        bool xPressed = leftXButton.action.IsPressed();
        bool yPressed = leftYButton.action.IsPressed();

        if (aPressed)
        {
            if (!isPlacing) 
                isPlacing = true;
            logPopup.gameObject.SetActive(false);
            applyButton.SetActive(true);
            UpdatePlacementPose();
            previewInstance.SetActive(true);
            AnchorPreview preview = previewInstance.GetComponent<AnchorPreview>();
            UpdateLineRenderer();
        }
        else
        {
            if (isPlacing) isPlacing = false;
            lineRenderer.enabled = false;
        }

        if (previewInstance.activeSelf)
        {
            if (xPressed && yPressed)
            {
                // 중복 입력인 경우 회전하지 않음
            }
            else if (xPressed)
            {
                // X 버튼: 시계 방향 회전
                previewInstance.transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
            }
            else if (yPressed)
            {
                // Y 버튼: X의 반대 방향(반시계 방향) 회전
                previewInstance.transform.Rotate(Vector3.up, -rotateSpeed * Time.deltaTime, Space.World);
            }
        }
    }

    private void UpdatePlacementPose()
    {
        if (rayOriginTransform == null)
        {
            Debug.LogWarning("Ray origin transform이 설정되지 않음!");
            return;
        }

        Vector3 origin = rayOriginTransform.position;
        Vector3 direction = rayOriginTransform.forward;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, maxRayDistance, placementLayerMask))
        {
            placementPose.position = hit.point;
            // 회전은 여기서 고정하지 않음
            // placementPose.rotation = Quaternion.LookRotation(Vector3.forward);

            previewInstance.transform.position = placementPose.position;

            lineRenderer.enabled = true;
            lineRenderer.SetPosition(0, origin);
            lineRenderer.SetPosition(1, hit.point);
        }
    }

    private void UpdateLineRenderer()
    {
        lineRenderer.enabled = true;
    }


    //  UI 버튼 onClick에 연결하면 됨
    public void ConfirmPlacement()
    {
        if (previewInstance.activeSelf)
        {
            applyButton.SetActive(false);
            Debug.Log($"Placement Confirmed at: {previewInstance.transform.position}");
            // 여기서 네가 따로 AnchorManager나 네트워크 연동하면 됨
            anchorManager.CreateStartupAnchor(new Pose(previewInstance.transform.position, previewInstance.transform.rotation), (message) =>
            {
                StartCoroutine(ShowPopupMessage(message));
                isAnchorPlaceMode = false;
                previewInstance.SetActive(false);
            });
        }
    }

    IEnumerator ShowPopupMessage(string message)
    {
        logPopup.gameObject.SetActive(true);
        logPopup.text = message;
        yield return new WaitForSeconds(3f);
        logPopup.gameObject.SetActive(false);
    }
}