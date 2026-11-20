using System.Collections.Generic;
using Autohand;
using UnityEngine;

// ============================================================
//  LocalHands.cs
//
//  내 손(다른 사람 손 제외) 목록과 트리거·검지 끝 조회 (EquipFx.cs 에서 분리, 11/20)
// ============================================================

/// <summary>내 손 목록(다른 사람 아바타 손 제외). 2초마다 갱신.</summary>
public static class LocalHands
{
    private static readonly List<Hand> s_hands = new List<Hand>();
    private static float s_next;

    public static List<Hand> All
    {
        get
        {
            if (Time.unscaledTime >= s_next)
            {
                s_next = Time.unscaledTime + 2f;
                s_hands.RemoveAll(h => h == null);
                foreach (var hand in Object.FindObjectsByType<Hand>(FindObjectsSortMode.None))
                {
                    if (hand == null || s_hands.Contains(hand) || hand.name.StartsWith("HighlightHand")) continue;
                    var nob = hand.GetComponentInParent<FishNet.Object.NetworkObject>();
                    if (nob != null && nob.IsSpawned && !nob.IsOwner) continue;
                    s_hands.Add(hand);
                }
            }
            return s_hands;
        }
    }

    /// <summary>
    /// 이 손 컨트롤러의 트리거를 지금 실제로 누르고 있는가(XR 기기 값을 직접 읽음). 기기를 못 읽으면 false 반환 + valid=false.
    /// 11/16 VR 테스트: 드릴 등 도구가 AutoHand 누름/뗌 이벤트(onSqueeze/onUnsqueeze)에만 의존해, '뗌' 이벤트를 놓치면
    /// 키를 떼도 계속 돌고(누름 상태 고착), 다시 잡으면 반대로 안 도는 문제가 있었다 → 매 프레임 실제 입력으로 판정한다.
    /// </summary>
    public static bool TriggerHeld(Hand h, out bool valid)
    {
        valid = false;
        if (h == null) return false;

        // 1순위: AutoHand 가 실제로 쓰는 입력(Input System 액션)의 트리거 축 값.
        // (11/16 2차) XR 기기 직접 읽기(InputDevices)는 이 프로젝트(OpenXR + Input System)에서 값을 못 받아(valid=false)
        // 예전 이벤트 값으로 돌아가 드릴이 계속 돌았다 → 손에 연결된 OpenXRHandControllerLink 의 squeezeAxis 를 읽는다.
        var link = LinkOf(h);
        if (link != null && link.squeezeAxis.action != null)
        {
            if (!link.squeezeAxis.action.enabled) link.squeezeAxis.action.Enable();
            valid = true;
            return link.squeezeAxis.action.ReadValue<float>() > 0.45f;
        }

        var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(h.left ? UnityEngine.XR.XRNode.LeftHand : UnityEngine.XR.XRNode.RightHand);
        if (!dev.isValid) return false;
        if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float f)) { valid = true; return f > 0.45f; }
        if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool b)) { valid = true; return b; }
        return false;
    }

    private static readonly Dictionary<Hand, Autohand.Demo.OpenXRHandControllerLink> s_links = new Dictionary<Hand, Autohand.Demo.OpenXRHandControllerLink>();
    private static float s_nextLinkScan;

    private static Autohand.Demo.OpenXRHandControllerLink LinkOf(Hand h)
    {
        if (s_links.TryGetValue(h, out var l) && l != null && l.isActiveAndEnabled) return l;
        if (Time.unscaledTime < s_nextLinkScan) return null;
        s_nextLinkScan = Time.unscaledTime + 1f;
        s_links.Clear();
        foreach (var x in Object.FindObjectsByType<Autohand.Demo.OpenXRHandControllerLink>(FindObjectsSortMode.None))
            if (x != null && x.hand != null && x.isActiveAndEnabled) s_links[x.hand] = x;
        return s_links.TryGetValue(h, out l) ? l : null;
    }

    /// <summary>이 손 컨트롤러의 트리거 값을 읽을 수 있는가.</summary>
    public static bool TriggerHeldValid(Hand h) { TriggerHeld(h, out bool v); return v; }

    /// <summary>이 물체(또는 그 부모·자식)를 들고 있는 내 VR 손. 없으면 null.</summary>
    public static Hand HoldingHand(Transform t)
    {
        if (t == null) return null;
        foreach (var h in All)
        {
            if (h == null || h.holdingObj == null) continue;
            var ht = h.holdingObj.transform;
            if (ht == t || t.IsChildOf(ht) || ht.IsChildOf(t)) return h;
        }
        return null;
    }

    public static Transform IndexTip(Hand h)
    {
        if (h == null || h.fingers == null) return null;
        foreach (var f in h.fingers)
            if (f != null && f.fingerType == FingerEnum.index && f.tip != null) return f.tip;
        return null;
    }
}
