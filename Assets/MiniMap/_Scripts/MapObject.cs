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