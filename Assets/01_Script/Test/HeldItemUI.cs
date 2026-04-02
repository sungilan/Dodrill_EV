using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

public class HeldItemUI : MonoBehaviour
{
    public static HeldItemUI Instance { get; private set; }

    [Header("UI 요소 연결")]
    [Tooltip("아무것도 안 들고 있을 때 숨길 전체 UI 패널")]
    public GameObject uiContainer;
    public Image itemIcon;         // 중앙의 부품 이미지
    public TMP_Text itemNameText;  // 하단의 부품 이름 텍스트

    [Header("로컬라이징 설정")]
    [Tooltip("아이템 이름이 저장된 로컬라이제이션 테이블 이름 (예: ItemTable)")]
    public string tableName = "ItemTable";

    [Header("보관 버튼")]
    [Tooltip("PC 마우스 클릭 또는 VR 반대 손 레이저로 클릭할 보관 버튼")]
    public Button storeButton;

    // 현재 들고 있는 오브젝트를 임시로 추적하기 위한 변수
    private GameObject _currentHeldObj;

    private void Awake()
    {
        Instance = this;
        ClearUI(); // 처음엔 빈손이므로 UI 숨김

        // 버튼에 인벤토리 보관 함수 연결
        if(storeButton != null)
        {
            storeButton.onClick.AddListener(StoreCurrentItemToInventory);
        }
    }

    /// <summary>
    /// 아이템을 잡았을 때 호출되어 UI를 갱신합니다.
    /// </summary>
    public void UpdateUI(GameObject heldObj)
    {
        if(heldObj == null)
        {
            ClearUI();
            return;
        }

        _currentHeldObj = heldObj;
        uiContainer.SetActive(true);

        string localizedName = "";
        Sprite icon = null;

        // 1. PartDataSO에서 데이터 가져오기 시도
        var attachment = heldObj.GetComponent<FreeModePartAttachment>();
        if(attachment != null && attachment.partData != null)
        {
            // [수정] PartData의 ID를 키로 사용하여 로컬라이징 텍스트를 가져옵니다.
            localizedName = GetLocalizedText(attachment.partData.prefabId);
            icon = attachment.partData.icon;
        }
        else
        {
            // 2. TaskItem인 경우의 폴백
            var taskItem = heldObj.GetComponent<TaskItem>();
            if(taskItem != null)
            {
                localizedName = GetLocalizedText(taskItem.prefabId);
            }
        }

        // 만약 로컬라이징에 실패하면 오브젝트 이름을 그대로 사용 (방어 코드)
        if(string.IsNullOrEmpty(localizedName)) localizedName = heldObj.name;

        // UI 텍스트 및 이미지 적용
        if(itemNameText != null) itemNameText.text = localizedName;

        if(itemIcon != null)
        {
            if(icon != null)
            {
                itemIcon.sprite = icon;
                itemIcon.enabled = true;
            }
            else
            {
                itemIcon.enabled = false;
            }
        }
    }

    /// <summary>
    /// 로컬라이제이션 테이블에서 키에 맞는 번역 문구를 가져옵니다.
    /// </summary>
    private string GetLocalizedText(string key)
    {
        if(string.IsNullOrEmpty(key)) return null;

        // 동기 방식으로 테이블에서 문구를 가져옵니다.
        // LocalizationSettings.StringDatabase를 통해 설정된 언어에 맞는 값을 반환합니다.
        return LocalizationSettings.StringDatabase.GetLocalizedString(tableName, key);
    }

    /// <summary>
    /// 아이템을 내려놓았을 때 UI를 숨깁니다.
    /// </summary>
    public void ClearUI()
    {
        _currentHeldObj = null;
        if(uiContainer != null) uiContainer.SetActive(false);
    }

    /// <summary>
    /// [보관] 버튼을 눌렀을 때 실행되는 핵심 함수 (VR / PC 공용)
    /// </summary>
    public void StoreCurrentItemToInventory()
    {
        if(_currentHeldObj == null) return;

        // ★ [핵심 1] 이벤트 연쇄로 인해 _currentHeldObj가 쥐도 새도 모르게 null이 될 수 있으므로, 
        // 안전하게 지역 변수(targetObj)에 백업해 둡니다.
        GameObject targetObj = _currentHeldObj;

        var part = targetObj.GetComponent<InteractablePart>();
        var data = targetObj.GetComponent<FreeModePartAttachment>()?.partData;

        if(part != null && EVInventoryUI.Instance != null)
        {
            // ★ [핵심 2] 놓는 순간(RequestRelease) 스케일이 꼬일 수 있으므로 
            // 소유권을 반납하기 전에 스케일 원복을 '가장 먼저' 실행합니다.
            targetObj.transform.localScale = Vector3.one;

            // 1. [VR 전용] AutoHand 강제 놓기 (VR 손에서 물건 분리)
            var grabbable = targetObj.GetComponent<Autohand.Grabbable>();
            if(grabbable != null) grabbable.ForceHandsRelease();

            // 2. [PC 전용] PC 홀드 추적 중지 및 서버 소유권 해제
            var syncGrab = targetObj.GetComponent<SyncGrab>();
            if(syncGrab != null)
            {
                syncGrab.StopPCHold();
                // 이 함수가 실행되면서 연쇄적으로 ClearUI()가 불려도 targetObj는 무사합니다.
                syncGrab.RequestRelease();
            }

            // 3. 바닥에 떨어지기 전에 인벤토리 패널로 안전하게 이동
            EVInventoryUI.Instance.AddPart(part, data);

            // 4. 작업이 끝났으므로 우측 UI 패널 확실하게 닫기
            ClearUI();

            Debug.Log("[HeldItemUI] 버튼 클릭으로 인벤토리에 보관 완료");
        }
    }
}