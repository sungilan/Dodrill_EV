using System;
using UnityEngine;

// ============================================================
//  GrabHooks.cs — 인터랙션 코어(SyncGrab 등)의 "확장 지점" (모듈화 2단계, 2026-11-20)
//
//  집기(SyncGrab)는 EV 안내·UI(GuideSystem·ItemNameTag·MultimeterMaster·차량 부품)를 직접 알지 못한다.
//  콘텐츠(EV)가 시작할 때(Test/EvInteractionBinding.cs) 여기에 자기 기능을 등록한다.
//  등록이 없으면 기본 동작(알리지 않음 / 이름표 없음 / 모두 들고 다닐 수 있는 물건)으로 안전하게 동작한다.
// ============================================================
public static class GrabHooks
{
    /// <summary>이 클라이언트에서 아이템을 집었을 때(prefabId) 안내 시스템 등에 알린다</summary>
    public static Action<string> ItemGrabbed;
    public static void RaiseItemGrabbed(string prefabId) => ItemGrabbed?.Invoke(prefabId ?? "");

    /// <summary>안내 마커(아웃라인+라벨)가 이미 붙은 도구인가 — 그러면 이름표를 또 띄우지 않는다</summary>
    public static Func<string, bool> HasGuideMarker;
    public static bool GuideMarkerShown(string label) => HasGuideMarker != null && HasGuideMarker(label);

    /// <summary>가리킨 물건의 이름표 보이기/숨기기</summary>
    public static Action<Transform, string> ShowNameTag;
    public static Action<Transform> HideNameTag;
    public static void NameTagShow(Transform t, string label) => ShowNameTag?.Invoke(t, label);
    public static void NameTagHide(Transform t) => HideNameTag?.Invoke(t);

    /// <summary>계측기(멀티미터 등)인가 — VR 에서 맨 앞에 그리는 대상</summary>
    public static Func<GameObject, bool> IsMeter;
    public static bool MeterHeld(GameObject go) => IsMeter != null && IsMeter(go);

    /// <summary>손에 들고 다니는 물건이 아닌가(바닥에 끄는 장비·차량 부품 등) — 레이저로 당겨 오지 않는다</summary>
    public static Func<Component, bool> IsNotHandheld;
    public static bool NotHandheld(Component c) => IsNotHandheld != null && IsNotHandheld(c);
}
