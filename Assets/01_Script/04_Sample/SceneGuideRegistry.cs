using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  SceneGuideRegistry.cs
//  씬에 미리 배치된 가이드 오브젝트를 id로 관리
//
//  씬 세팅:
//    GuideObjects (빈 GameObject)
//    └── SceneGuideRegistry 컴포넌트
//        └── Guides 목록에 id + GameObject 등록
//
//  가이드 오브젝트는 씬 시작 시 전부 비활성화
//  Task 시작 시 ScenarioSpawner가 해당 id만 활성화
//  Task 종료 시 다시 비활성화
// ============================================================

public class SceneGuideRegistry : MonoBehaviour
{
    [System.Serializable]
    public struct GuideEntry
    {
        public string     guideId;
        public GameObject guideObject;
    }

    [SerializeField] private List<GuideEntry> _guides = new();

    private void Awake()
    {
        // 씬 시작 시 전부 비활성화
        foreach (var entry in _guides)
            if (entry.guideObject != null)
                entry.guideObject.SetActive(false);
    }

    public void Show(string guideId)
    {
        var obj = Find(guideId);
        if (obj != null)
        {
            obj.SetActive(true);
            Debug.Log($"[GuideRegistry] 활성화: {guideId}");
        }
        else
        {
            Debug.LogWarning($"[GuideRegistry] guideId '{guideId}' 없음");
        }
    }

    public void Hide(string guideId)
    {
        var obj = Find(guideId);
        if (obj != null) obj.SetActive(false);
    }

    public void HideAll()
    {
        foreach (var entry in _guides)
            if (entry.guideObject != null)
                entry.guideObject.SetActive(false);
    }

    private GameObject Find(string guideId)
    {
        foreach (var entry in _guides)
            if (entry.guideId == guideId) return entry.guideObject;
        return null;
    }
}
