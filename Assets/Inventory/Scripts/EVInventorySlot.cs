using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization.Components;

// ============================================================
//  EVInventorySlot.cs
//  탈거된 부품 한 종류를 보관하는 인벤토리 슬롯.
//  원본: MikeNspired XRIStarterKit InventorySlot / InventorySlotItemHandler
//  수정: XRBaseInteractable → InteractablePart / SyncGrab
//
//  씬 구조 (슬롯 프리팹):
//    EVInventorySlot  ← 이 스크립트
//      ├─ ItemModelHolder        ← 부품 메시 미리보기 표시
//      │    └─ (메시 클론 동적 생성)
//      ├─ SlotDisplayHasItem     ← 부품 있을 때 표시 (아이콘+이름)
//      │    └─ NameLabel (TMP)
//      └─ SlotDisplayEmpty       ← 비어있을 때 표시
// ============================================================
public class EVInventorySlot : MonoBehaviour
{
    [Header("비주얼")]
    public Transform itemModelHolder;      // 메시 클론 부모
    public GameObject slotDisplayHasItem;   // 부품 있을 때 표시
    public GameObject slotDisplayEmpty;     // 비어있을 때 표시
    public TMPro.TMP_Text nameLabel;          // 부품 이름
    public TMPro.TMP_Text descLabel;          // 부품 설명 (호버 시)
    public BoxCollider inventorySize;        // 슬롯 박스 크기 (스케일 계산용)
    public Image partIcon;

    [Header("오디오")]
    public AudioSource grabAudio;
    public AudioSource storeAudio;

    [Header("애니메이션")]
    public float animateInDuration = 0.15f;  // 부품 슬롯 진입 애니메이션
    public float animateOutDuration = 0.2f;

    // ── 이벤트 ────────────────────────────────────────────
    public event Action<EVInventorySlot> OnSlotUpdated;

    // ── 상태 ───────────────────────────────────────────────
    public InteractablePart StoredPart { get; private set; }
    public PartDataSO StoredData { get; private set; }
    public bool HasItem => StoredPart != null || StoredData != null;

    // 메시 클론
    private Transform _meshClone;
    private Transform _boundCenter;
    private Vector3 _goalScale;
    private bool _isBusy;
    private Coroutine _animCoroutine;

    // ── 초기화 ─────────────────────────────────────────────

    // ── 외부 API ───────────────────────────────────────────

    /// <summary>
    /// FreeLookController / FreeModeBootstrapper에서 부품을 인벤토리에 넣을 때 호출.
    /// part가 null이면 PartDataSO만으로 슬롯을 채움(스폰 전 데이터 보관).
    /// </summary>
    public void StorePart(InteractablePart part, PartDataSO data = null)
    {
        if(_isBusy) return;
        _isBusy = true;

        StoredPart = part;
        StoredData = data ?? part?.GetComponent<FreeModePartAttachment>()?.partData;

        if(part != null) SetupMeshClone(part.gameObject);
        if(part != null) part.gameObject.SetActive(false);

        storeAudio?.Play();

        // ★ 핵심: 패널이 꺼져 있어도 내부 UI 활성화 상태를 강제로 갱신합니다.
        RefreshDisplay();

        // 애니메이션 처리
        if(gameObject.activeInHierarchy)
        {
            if(_animCoroutine != null) StopCoroutine(_animCoroutine);
            _animCoroutine = StartCoroutine(AnimateModelScale(true, animateInDuration));
        }
        else
        {
            if(_boundCenter != null) _boundCenter.localScale = _goalScale;
            _isBusy = false;
        }

        OnSlotUpdated?.Invoke(this);
    }

    /// <summary>
    /// 인벤토리 슬롯 클릭 시 부품을 월드에 스폰.
    /// FreeModeSpawner.SpawnPart()에 위임하거나 직접 부품을 활성화.
    /// </summary>
    public void RetrievePart()
    {
        if(!HasItem || _isBusy) return;
        _isBusy = true;

        grabAudio?.Play();

        // 메시 클론 제거
        DestroyMeshClone();

        if(StoredPart != null)
        {
            // 1. 오브젝트 활성화 (스크립트들이 동작할 수 있도록 먼저 켭니다)
            StoredPart.gameObject.SetActive(true);

            // 2. 잡기 상태 완벽 초기화
            var syncGrab = StoredPart.GetComponent<SyncGrab>();
            if(syncGrab != null)
            {
                syncGrab.StopPCHold();      // PC 홀드 위치 추적 즉시 강제 중지
                syncGrab.RequestRelease();  // 서버에 소유권 및 잡기 상태 반납
            }
            StoredPart.OnGrabEnd(); // InteractablePart 자체의 PC 홀드 상태 해제

            // 3. 내부 상태를 분리(Detached) 모드로 갱신
            StoredPart.ForceDetach();

            // 4. 플레이어 앞 스폰 위치로 강제 이동 
            var spawnPos = GetSpawnPosition();
            StoredPart.transform.position = spawnPos;

            // 5. ★ [핵심 수정] 물리 상태 리셋 (충돌/떨림 방지)
            var rb = StoredPart.GetComponent<Rigidbody>();
            if(rb != null)
            {
                // 인벤토리에서 꺼내는 순간에는 물리 연산을 완벽히 끕니다.
                // 그래야 PC의 DOTween이나 LNT가 위치를 잡을 때 중력과 싸우지 않습니다.
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // 6. ★ [수정] PC/모바일 환경이면 FreeLookController를 통해 쥐어줍니다.
            if(syncGrab != null && !GameScenePlatformManager.IsVR)
            {
                var freeLook = UnityEngine.Object.FindFirstObjectByType<FreeLookController>();
                if(freeLook != null)
                {
                    // FreeLookController에게 "이거 네가 잡은 걸로 처리해!" 라고 넘김
                    freeLook.ForceGrabFromInventory(syncGrab);
                }
                else
                {
                    // 혹시라도 컨트롤러를 못 찾으면 폴백(기존 방식)
                    syncGrab.OnPCClick();
                }
            }
        }
        else if(StoredData != null)
        {
            // 데이터만 있는 경우 FreeModeSpawner에 위임
            //FreeModeSpawner.Instance?.SpawnPart(StoredData.partId);
        }

        StoredPart = null;
        StoredData = null;

        RefreshDisplay();
        if(_animCoroutine != null) StopCoroutine(_animCoroutine);
        _animCoroutine = StartCoroutine(AnimateModelScale(false, animateOutDuration));

        OnSlotUpdated?.Invoke(this);
    }

    // ── 내부: 디스플레이 ───────────────────────────────────

    private void RefreshDisplay()
    {
        SetDisplay(HasItem);

        if(HasItem)
        {
            if(nameLabel != null)
            {
                // ★ [수정] 코드로 직접 텍스트를 넣지 않고, 로컬라이즈 이벤트의 Key를 변경합니다.
                var localizeEvent = nameLabel.GetComponent<LocalizeStringEvent>();

                if(localizeEvent != null && StoredData != null)
                {
                    // PartDataSO의 partId (예: "SM_Chassis")를 String Table의 Entry Name(Key)으로 사용
                    localizeEvent.StringReference.TableEntryReference = StoredData.partId;
                    localizeEvent.RefreshString(); // 바뀐 Key로 즉시 번역본 불러오기
                }
                else
                {
                    // 로컬라이즈 컴포넌트가 없거나 오류 시 기존 방식(폴백) 사용
                    nameLabel.text = StoredData?.displayName ?? StoredPart?.name ?? "부품";
                }
            }

            // 아이콘 적용 로직 (기존과 동일)
            if(partIcon != null)
            {
                Sprite targetSprite = StoredData?.icon ?? (EVInventoryUI.Instance != null ? EVInventoryUI.Instance.defaultPartIcon : null);
                if(targetSprite != null)
                {
                    partIcon.sprite = targetSprite;
                    partIcon.enabled = true;
                    Color c = partIcon.color; c.a = 1f; partIcon.color = c;
                }
                else { partIcon.enabled = false; }
            }
        }
        else
        {
            if(partIcon != null) partIcon.enabled = false;
        }

        _isBusy = false;
    }

    private void SetDisplay(bool hasItem)
    {
        if(slotDisplayHasItem != null) slotDisplayHasItem.SetActive(hasItem);
        if(slotDisplayEmpty != null) slotDisplayEmpty.SetActive(!hasItem);
    }

    // ── 내부: 메시 클론 (원본 GameObjectCloner 방식 채용) ──

    private void SetupMeshClone(GameObject source)
    {
        DestroyMeshClone();
        if(itemModelHolder == null) return;

        // 1. 비주얼만 복제 (물리/컴포넌트 제거)
        _meshClone = CreateVisualClone(source).transform;
        _meshClone.SetParent(itemModelHolder, false);

        // 2. 바운드 계산
        var bounds = GetBounds(_meshClone);
        if(bounds.size == Vector3.zero)
        {
            _meshClone.localPosition = Vector3.zero;
            return;
        }

        // 3. 중심 피벗 생성
        if(_boundCenter != null) Destroy(_boundCenter.gameObject);
        _boundCenter = new GameObject("BoundCenter").transform;
        _boundCenter.SetParent(itemModelHolder, false);
        _boundCenter.position = bounds.center;
        _meshClone.SetParent(_boundCenter, true);

        // 4. 슬롯 크기에 맞게 스케일 계산
        if(inventorySize != null)
        {
            inventorySize.enabled = true;
            var slotSize = inventorySize.bounds.size;
            inventorySize.enabled = false;

            float ratio = Mathf.Min(
                slotSize.x / bounds.size.x,
                slotSize.y / bounds.size.y,
                slotSize.z / bounds.size.z);
            ratio = Mathf.Clamp(ratio, 0.01f, 1f);
            _goalScale = Vector3.one * ratio;
        }
        else
        {
            _goalScale = Vector3.one * 0.15f; // 기본 크기
        }

        _boundCenter.localScale = Vector3.zero; // 애니메이션 시작값
        _boundCenter.localPosition = Vector3.zero;
        _boundCenter.localRotation = Quaternion.Euler(0, 90, 0);
    }

    private void DestroyMeshClone()
    {
        if(_meshClone != null) { Destroy(_meshClone.gameObject); _meshClone = null; }
        if(_boundCenter != null) { Destroy(_boundCenter.gameObject); _boundCenter = null; }
    }

    // 물리/스크립트 없는 비주얼 복제
    private static GameObject CreateVisualClone(GameObject source)
    {
        var clone = new GameObject(source.name + "_Preview");

        foreach(var mf in source.GetComponentsInChildren<MeshFilter>(true))
        {
            if(mf.sharedMesh == null) continue;
            var child = new GameObject(mf.name);
            child.transform.SetParent(clone.transform, false);
            child.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
            child.transform.localScale = mf.transform.lossyScale;

            var newMF = child.AddComponent<MeshFilter>();
            newMF.mesh = mf.sharedMesh;

            var srcMR = mf.GetComponent<MeshRenderer>();
            if(srcMR != null)
            {
                var newMR = child.AddComponent<MeshRenderer>();
                newMR.sharedMaterials = srcMR.sharedMaterials;
            }
        }
        return clone;
    }

    private static Bounds GetBounds(Transform root)
    {
        var bounds = new Bounds();
        foreach(var r in root.GetComponentsInChildren<Renderer>())
        {
            if(bounds.extents == Vector3.zero) bounds = r.bounds;
            else bounds.Encapsulate(r.bounds);
        }
        return bounds;
    }

    // ── 애니메이션 ────────────────────────────────────────

    private IEnumerator AnimateModelScale(bool toOne, float duration)
    {
        if(_boundCenter == null) { _isBusy = false; yield break; }

        float t = 0;
        Vector3 from = toOne ? Vector3.zero : _goalScale;
        Vector3 to = toOne ? _goalScale : Vector3.zero;

        while(t < duration)
        {
            t += Time.deltaTime;
            _boundCenter.localScale = Vector3.Lerp(from, to, t / duration);
            yield return null;
        }
        _boundCenter.localScale = to;
        _isBusy = false;
    }

    // ── 스폰 위치 ─────────────────────────────────────────

    private Vector3 GetSpawnPosition()
    {
        if(Camera.main != null)
            return Camera.main.transform.position
                 + Camera.main.transform.forward * 1.5f
                 + Vector3.down * 0.3f;
        return transform.position + Vector3.up * 0.5f;
    }
}