using UnityEngine;

// ============================================================
//  EvInteractionBinding.cs — EV 콘텐츠가 인터랙션 코어(GrabHooks)에 자기 기능을 등록 (모듈화 2단계, 2026-11-20)
//
//  SyncGrab 이 EV 클래스(GuideSystem·ItemNameTag·MultimeterMaster·FloorDragProp·VehiclePartsManager)를 직접 부르던 것을 이 파일로 모았다.
//  씬이 로드되기 전에 한 번 실행된다.
// ============================================================
public static class EvInteractionBinding
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        GrabHooks.ItemGrabbed = id => GuideSystem.Instance?.OnItemGrabbed(id);
        GrabHooks.HasGuideMarker = label => GuideSystem.Instance != null && GuideSystem.Instance.HasSpawnMarker(label);
        GrabHooks.ShowNameTag = (t, label) => ItemNameTag.Show(t, label);
        GrabHooks.HideNameTag = t => ItemNameTag.Hide(t);
        GrabHooks.IsMeter = go => go.GetComponentInChildren<MultimeterMaster>(true) != null;
        GrabHooks.IsNotHandheld = c => c.GetComponent<FloorDragProp>() != null || c.GetComponentInParent<VehiclePartsManager>() != null;
    }
}
