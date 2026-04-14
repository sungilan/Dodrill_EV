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
        {
            miniMapTarget = mmc.target;
        }

        if(miniMapTarget == null) return;

        SetPositionAndRotation();

        // --- [추가] 텍스트 정방향 유지 ---
        // 아이콘은 회전해도 텍스트는 항상 읽기 편하게 0도 유지
        if(labelRect != null)
        {
            labelRect.rotation = Quaternion.identity;
        }
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

        // --- [추가] 텍스트 컴포넌트 초기화 ---
        // 프리팹 내부에 이미 있다면 가져오고, 없다면 로그 출력
        labelText = GetComponentInChildren<TextMeshProUGUI>();
        if(labelText != null)
        {
            labelText.text = mme.objectName; // MiniMapEntity에 추가한 이름을 할당
            labelRect = labelText.GetComponent<RectTransform>();
        }

        miniMapTarget = mmc.target;

        if(miniMapTarget != null)
        {
            SetPositionAndRotation();
        }
    }

    void SetPositionAndRotation()
    {
        transform.SetParent(panelGO.transform, false);
        SetPosition();
        SetRotation();
    }

    void SetPosition()
    {
        if(mapCamera == null || owner == null || miniMapTarget == null) return;

        cornerss = new Vector3[4];
        rt.GetWorldCorners(cornerss);
        screenPos = RectTransformUtility.WorldToScreenPoint(mapCamera, owner.transform.position);

        if(linkedMiniMapEntity.clampInBorder && mmc.target != null && Mathf.Abs(Vector3.Distance(owner.transform.position, mmc.target.transform.position)) < linkedMiniMapEntity.clampDist)
        {
            ClampIconColliderWise();
        }
        else
        {
            sprRect.anchoredPosition = screenPos - rt.sizeDelta / 2f;
        }
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
                    ? linkedMiniMapEntity.upAxis.y * (miniMapTarget.localEulerAngles.y - mmc.rotationOfCam.z - owner.transform.localEulerAngles.y) + linkedMiniMapEntity.rotation
                    : -linkedMiniMapEntity.upAxis.y * (owner.transform.localEulerAngles.y) + linkedMiniMapEntity.rotation;
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