using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class GloveItem : XRGrabInteractable // XRI 그랩 상속
{
    [Header("Glove Settings")]
    public float equipDistance = 0.15f; // 손과의 거리 (15cm)
    private bool isBeingHeld = false;
    private Transform handTransform; // 잡고 있는 손

    protected override void Awake()
    {
        base.Awake();
        // 장갑 아이템의 피벗(Pivot)을 손목 부분에 맞추는 것이 자연스럽습니다.
    }

    // [XRI 이벤트] 유저가 장갑을 잡았을 때 호출
    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);
        isBeingHeld = true;

        // 잡은 손(Interactor)의 위치를 저장
        handTransform = args.interactorObject.transform;
    }

    // [XRI 이벤트] 유저가 장갑을 놓았을 때 호출
    protected override void OnSelectExited(SelectExitEventArgs args)
    {
        base.OnSelectExited(args);
        isBeingHeld = false;
        handTransform = null;
    }

    void Update()
    {
        if(isBeingHeld && handTransform != null)
        {
            CheckEquipDistance();
        }
    }

    private void CheckEquipDistance()
    {
        // 장갑과 실제 손(컨트롤러) 위치 사이의 거리 계산
        float distance = Vector3.Distance(transform.position, handTransform.position);

        if(distance < equipDistance)
        {
            EquipGloves();
        }
    }

    private void EquipGloves()
    {
        // 1. 안전 관리자 상태 업데이트
        ElectricSafetyManager.Instance.isGlovesEquipped = true;
        Debug.Log("절연 장갑을 착용했습니다. 고전압 부품을 만질 수 있습니다.");

        // 2. [사운드] 장갑 끼는 '슥' 하는 소리 재생

        // 3. 손 모델 변경 로직 (아래 2번 참고)
        //HandVisualManager.Instance.ChangeToGloveVisual();

        // 4. 아이템 제거 (착용했으므로 월드에서 사라짐)
        gameObject.SetActive(false);
    }
}