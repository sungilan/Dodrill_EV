using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // 텍스트 표시를 위해 추가

public class MapObject : MonoBehaviour
{
    public MiniMapEntity linkedMiniMapEntity;
    public MiniMapController mmc;
    public GameObject owner;
    public Camera mapCamera;
    public Image spr;
    public GameObject panelGO;

    // --- [추가] 텍스트 관련 변수 ---
    public TextMeshProUGUI labelText;
    public RectTransform labelRect;

    Vector3 viewPortPos;
    RectTransform rt;
    Vector3[] cornerss;

    RectTransform sprRect;
    Vector2 screenPos;
    Transform miniMapTarget;

    void FixedUpdate()
    {
        if(owner == null)
        {
            Destroy(this.gameObject);
            return;
        }

        if(miniMapTarget == null && mmc != null && mmc.target != null)
            miniMapTarget = mmc.target;

        if(miniMapTarget == null) return;

        SetPositionAndRotation();

        // --- [수정] 텍스트 및 아이콘 정방향 유지 보강 ---
        // 부모인 sprRect가 회전하더라도 텍스트는 항상 월드 기준 0도를 유지합니다.
        KeepLabelUpright();
    }

    // 이름표가 '플레이어 방향 따라 빙글 도는' 문제(특히 VR 의 기울어진 월드 스페이스 미니맵): 월드 회전 0 으로 두면 평면과 어긋나고,
    // 회전하는 아이콘의 자식이라 위치도 아이콘 둘레를 돈다 → 아이콘 회전을 매 프레임 거꾸로 걸어 평면에 고정한다.
    private bool _labelBaseSet;
    private Vector3 _labelBasePos;

    void LateUpdate()
    {
        KeepLabelUpright();
    }

    void KeepLabelUpright()
    {
        if(labelRect == null || sprRect == null) return;
        if(!_labelBaseSet)
        {
            _labelBaseSet = true;
            _labelBasePos = labelRect.localPosition;
        }
        float z = -sprRect.localEulerAngles.z;
        var counter = Quaternion.Euler(0f, 0f, z);
        labelRect.localRotation = counter;
        labelRect.localPosition = counter * _labelBasePos;
    }

    public void SetMiniMapEntityValues(MiniMapController controller, MiniMapEntity mme, GameObject attachedGO, Camera renderCamera, GameObject parentPanelGO)
    {
        linkedMiniMapEntity = mme;
        owner = attachedGO;
        mapCamera = renderCamera;
        panelGO = parentPanelGO;
        spr = gameObject.GetComponent<Image>();
        spr.sprite = mme.icon;
        sprRect = spr.gameObject.GetComponent<RectTransform>();
        sprRect.sizeDelta = mme.size;
        rt = panelGO.GetComponent<RectTransform>();
        mmc = controller;

        // --- [수정] 텍스트 초기화 및 색상 적용 ---
        labelText = GetComponentInChildren<TextMeshProUGUI>();
        if(labelText != null)
        {
            labelText.text = mme.objectName;
            labelRect = labelText.GetComponent<RectTransform>();

            // 플레이어인지 확인 (Tag나 이름을 통해 판단)
            if(owner.CompareTag("Player") || owner.name.Contains("Player"))
            {
                labelText.color = Color.green; // 플레이어 이름은 초록색
                labelText.fontStyle = FontStyles.Bold; // 강조를 위해 굵게
            }
            else
            {
                labelText.color = Color.white; // 일반 오브젝트는 흰색
            }

            // 시인성을 위해 외곽선(Outline) 컴포넌트가 없다면 추가 (검은 테두리)
            if(labelText.gameObject.GetComponent<Shadow>() == null)
            {
                var shadow = labelText.gameObject.AddComponent<Shadow>();
                shadow.effectColor = Color.black;
                shadow.effectDistance = new Vector2(1.5f, -1.5f);
            }
        }

        miniMapTarget = mmc.target;
        if(miniMapTarget != null) SetPositionAndRotation();
    }

    void SetPositionAndRotation()
    {
        transform.SetParent(panelGO.transform, false);
        SetPosition();
        SetRotation();
    }

    // VR 플레이어는 루트 오브젝트를 네트워크로 동기화하지 않고(머리·손 목표만 보냄) 루트가 스폰 지점에 그대로 있다.
    // 그래서 미니맵 아이콘이 시작 위치에 고정됐다 → VR 이면 머리(내 것은 HMD, 남의 것은 아바타 머리 목표)를 기준으로 삼는다.
    private VRPlayer _vrPlayer;
    private bool _vrChecked;

    private Transform TrackPoint
    {
        get
        {
            if(owner == null) return null;
            if(!_vrChecked)
            {
                _vrChecked = true;
                _vrPlayer = owner.GetComponent<VRPlayer>();
                if(_vrPlayer == null) _vrPlayer = owner.GetComponentInParent<VRPlayer>();
            }
            if(_vrPlayer != null && _vrPlayer.trackingSetup != null)
            {
                var ts = _vrPlayer.trackingSetup;
                Transform head = _vrPlayer.IsOwner ? ts.trackingHead : ts.applyHead;
                if(head != null) return head;
            }
            return owner.transform;
        }
    }

    private float OwnerYaw()
    {
        var tp = TrackPoint;
        if(tp == null || tp == owner.transform) return owner.transform.localEulerAngles.y;
        return tp.eulerAngles.y;
    }

    void SetPosition()
    {
        if(mapCamera == null || owner == null || miniMapTarget == null) return;

        cornerss = new Vector3[4];
        rt.GetWorldCorners(cornerss);
        Vector3 ownerPos = TrackPoint.position;
        screenPos = RectTransformUtility.WorldToScreenPoint(mapCamera, ownerPos);

        if(linkedMiniMapEntity.clampInBorder && mmc.target != null && Mathf.Abs(Vector3.Distance(ownerPos, mmc.target.transform.position)) < linkedMiniMapEntity.clampDist)
        {
            ClampIconColliderWise();
            SetInMapRange(true);
        }
        else
        {
            sprRect.anchoredPosition = screenPos - rt.sizeDelta / 2f;
            // 미니맵이 찍는 범위 밖이면 아이콘이 미니맵 밖에 떠 보이지 않게 숨긴다
            Vector3 sp = mapCamera.WorldToScreenPoint(ownerPos);
            Vector2 size = rt.rect.size;
            SetInMapRange(sp.z > 0f && screenPos.x >= 0f && screenPos.x <= size.x && screenPos.y >= 0f && screenPos.y <= size.y);
        }
    }

    // 가시성은 MiniMapComponent 가 spr.gameObject.SetActive 로 따로 관리한다 → 여기서는 그림(Graphic)만 껐다 켠다(서로 안 부딪힘)
    private bool _inMapRange = true;
    private void SetInMapRange(bool inRange)
    {
        if(_inMapRange == inRange) return;
        _inMapRange = inRange;
        if(spr != null) spr.enabled = inRange;
        if(labelText != null) labelText.enabled = inRange;
    }

    void ClampIconColliderWise()
    {
        if(mmc.shapeColliderGO == null)
        {
            sprRect.anchoredPosition = screenPos - rt.sizeDelta / 2f;
            return;
        }

        sprRect.anchoredPosition = screenPos - rt.sizeDelta / 2f;
        Vector2 diff = (rt.position - sprRect.position);
        RaycastHit2D[] hits = Physics2D.RaycastAll(sprRect.position, diff);
        if(hits.Length > 0)
        {
            for(int i = 0; i < hits.Length; i++)
            {
                if(hits[i].transform.name == mmc.shapeColliderGO.name)
                {
                    sprRect.position = hits[i].point;
                    break;
                }
            }
        }
    }

    void SetRotation()
    {
        if(miniMapTarget == null) return;

        if(linkedMiniMapEntity.rotateWithObject)
        {
            float finalZ = 0;
            if(Mathf.Abs(linkedMiniMapEntity.upAxis.y) == 1)
            {
                finalZ = mmc.rotateWithTarget
                    ? linkedMiniMapEntity.upAxis.y * (miniMapTarget.localEulerAngles.y - mmc.rotationOfCam.z - OwnerYaw()) + linkedMiniMapEntity.rotation
                    : -linkedMiniMapEntity.upAxis.y * (OwnerYaw()) + linkedMiniMapEntity.rotation;
            }
            // ... (x, z축 회전 로직은 생략 혹은 위와 동일하게 finalZ 계산)

            sprRect.localEulerAngles = new Vector3(0, 0, finalZ);
        }
        else
        {
            sprRect.localEulerAngles = new Vector3(0, 0, linkedMiniMapEntity.rotation);
        }
    }
}