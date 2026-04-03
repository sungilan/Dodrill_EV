using System.Collections; // ★ 코루틴 사용을 위해 필수!
using System.Collections.Generic;
using FishNet;
using UnityEngine;
using static ScenarioRunner;

public class MiniMapGuideManager : MonoBehaviour
{
    [Header("Icon Sprites")]
    public Sprite itemIcon;
    public Sprite targetIcon;

    private List<MiniMapComponent> activeGuides = new List<MiniMapComponent>();

    private void OnEnable()
    {
        if(InstanceFinder.ClientManager != null)
            InstanceFinder.ClientManager.RegisterBroadcast<UpdateMiniMapBroadcast>(OnUpdateMiniMap);
    }

    private void OnDisable()
    {
        if(InstanceFinder.ClientManager != null)
            InstanceFinder.ClientManager.UnregisterBroadcast<UpdateMiniMapBroadcast>(OnUpdateMiniMap);
    }

    private void OnUpdateMiniMap(UpdateMiniMapBroadcast msg, FishNet.Transporting.Channel channel)
    {
        ClearGuides();

        if(msg.requiredItemIds != null)
        {
            foreach(var id in msg.requiredItemIds)
            {
                // ★ 여기서 에러가 났던 함수를 호출합니다.
                StartCoroutine(WaitAndAddGuide(id, itemIcon, new Vector2(25, 25)));
            }
        }

        if(!string.IsNullOrEmpty(msg.targetZoneId))
        {
            // Zone은 보통 씬에 이미 있으므로 바로 찾기 시도
            GameObject zoneObj = GameObject.Find(msg.targetZoneId);
            if(zoneObj != null) AddGuide(zoneObj, targetIcon, new Vector2(35, 35));
            else StartCoroutine(WaitAndAddZone(msg.targetZoneId, targetIcon, new Vector2(35, 35)));
        }
    }

    // ── ★ [추가] 에러 해결을 위한 코루틴 함수 ──
    private IEnumerator WaitAndAddGuide(string id, Sprite icon, Vector2 size)
    {
        GameObject itemObj = null;
        float timeout = 3.0f; // 3초 동안 찾기 시도

        while(itemObj == null && timeout > 0)
        {
            itemObj = FindGameObjectByPrefabId(id);
            if(itemObj == null)
            {
                timeout -= 0.1f;
                yield return new WaitForSeconds(0.1f);
            }
        }

        if(itemObj != null)
            AddGuide(itemObj, icon, size);
    }

    // Zone도 늦게 나타날 수 있으므로 동일하게 처리
    private IEnumerator WaitAndAddZone(string zoneId, Sprite icon, Vector2 size)
    {
        GameObject zoneObj = null;
        float timeout = 3.0f;
        while(zoneObj == null && timeout > 0)
        {
            zoneObj = GameObject.Find(zoneId);
            if(zoneObj == null)
            {
                timeout -= 0.1f;
                yield return new WaitForSeconds(0.1f);
            }
        }
        if(zoneObj != null) AddGuide(zoneObj, icon, size);
    }

    private void AddGuide(GameObject target, Sprite icon, Vector2 size)
    {
        MiniMapComponent mmc = target.GetComponent<MiniMapComponent>();
        if(mmc == null) mmc = target.AddComponent<MiniMapComponent>();

        mmc.icon = icon;
        mmc.size = size;
        mmc.clampIconInBorder = true;
        mmc.clampDistance = 0;

        mmc.enabled = false;
        mmc.enabled = true;

        activeGuides.Add(mmc);
    }

    private void ClearGuides()
    {
        foreach(var mmc in activeGuides)
        {
            if(mmc != null)
            {
                // MiniMapComponent만 파괴하여 미니맵에서 등록 해제
                Destroy(mmc);
            }
        }
        activeGuides.Clear();
    }

    private GameObject FindGameObjectByPrefabId(string id)
    {
        var items = Object.FindObjectsByType<TaskItem>(FindObjectsSortMode.None);
        foreach(var it in items) if(it.prefabId == id) return it.gameObject;
        return null;
    }
}