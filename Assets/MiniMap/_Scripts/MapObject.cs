using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MapObject : MonoBehaviour
{
    MiniMapEntity linkedMiniMapEntity;
    MiniMapController mmc;
    GameObject owner;
    Camera mapCamera;
    Image spr;
    GameObject panelGO;

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

        // --- [추가] 타겟 재확인 로직 ---
        // Controller의 타겟이 할당되었는데 내 변수가 비어있다면 동기화
        if(miniMapTarget == null && mmc != null && mmc.target != null)
        {
            miniMapTarget = mmc.target;
        }

        // 타겟이 여전히 없다면 연산을 수행하지 않음 (에러 방지)
        if(miniMapTarget == null) return;

        SetPositionAndRotation();
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

        // 여기서 바로 할당을 시도하지만, 아직 Controller가 타겟을 못 찾았을 수 있음
        miniMapTarget = mmc.target;

        // 타겟이 있을 때만 초기 위치 설정
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
        // 에러 방지용 최종 체크
        if(mapCamera == null || owner == null || miniMapTarget == null) return;

        cornerss = new Vector3[4];
        rt.GetWorldCorners(cornerss);
        screenPos = RectTransformUtility.WorldToScreenPoint(mapCamera, owner.transform.position);

        // mmc.target 참조 시 null 체크 추가
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
        // mmc.shapeColliderGO가 할당되지 않았을 경우를 대비한 안전 장치
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
        // 회전 연산 시에도 타겟 확인
        if(miniMapTarget == null) return;

        if(linkedMiniMapEntity.rotateWithObject)
        {
            if(Mathf.Abs(linkedMiniMapEntity.upAxis.y) == 1)
            {
                if(mmc.rotateWithTarget)
                    sprRect.localEulerAngles = new Vector3(0, 0, linkedMiniMapEntity.upAxis.y * (miniMapTarget.localEulerAngles.y - mmc.rotationOfCam.z - owner.transform.localEulerAngles.y) + linkedMiniMapEntity.rotation);
                else
                    sprRect.localEulerAngles = new Vector3(0, 0, -linkedMiniMapEntity.upAxis.y * (owner.transform.localEulerAngles.y) + linkedMiniMapEntity.rotation);

            }
            else if(Mathf.Abs(linkedMiniMapEntity.upAxis.z) == 1)
            {
                if(mmc.rotateWithTarget)
                    sprRect.localEulerAngles = new Vector3(0, 0, linkedMiniMapEntity.upAxis.z * (miniMapTarget.localEulerAngles.z - mmc.rotationOfCam.z - owner.transform.localEulerAngles.z) + linkedMiniMapEntity.rotation);
                else
                    sprRect.localEulerAngles = new Vector3(0, 0, -linkedMiniMapEntity.upAxis.z * (owner.transform.localEulerAngles.z) + linkedMiniMapEntity.rotation);
            }
            else if(Mathf.Abs(linkedMiniMapEntity.upAxis.x) == 1)
            {
                if(mmc.rotateWithTarget)
                    sprRect.localEulerAngles = new Vector3(0, 0, linkedMiniMapEntity.upAxis.x * (miniMapTarget.localEulerAngles.x - mmc.rotationOfCam.z - owner.transform.localEulerAngles.x) + linkedMiniMapEntity.rotation);
                else
                    sprRect.localEulerAngles = new Vector3(0, 0, -linkedMiniMapEntity.upAxis.x * (owner.transform.localEulerAngles.x) + linkedMiniMapEntity.rotation);
            }
        }
        else
        {
            // ... (기타 회전 로직은 동일)
            if(Mathf.Abs(linkedMiniMapEntity.upAxis.y) == 1)
            {
                sprRect.localEulerAngles = new Vector3(0, 0, sprRect.localEulerAngles.z + linkedMiniMapEntity.rotation);
            }
            else if(Mathf.Abs(linkedMiniMapEntity.upAxis.z) == 1)
            {
                sprRect.localEulerAngles = new Vector3(0, 0, sprRect.localEulerAngles.z + linkedMiniMapEntity.rotation);
            }
            else if(Mathf.Abs(linkedMiniMapEntity.upAxis.x) == 1)
            {
                sprRect.localEulerAngles = new Vector3(0, 0, sprRect.localEulerAngles.z + linkedMiniMapEntity.rotation);
            }
        }
    }
}