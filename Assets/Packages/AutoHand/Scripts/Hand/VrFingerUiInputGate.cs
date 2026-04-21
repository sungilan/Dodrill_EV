using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// VR UI 손가락 입력: 해당 손 XR 컨트롤러가 유효하고 추적 중이면 손가락 UI를 막아
/// UIPointer(레이)와 이중 입력·간섭을 줄입니다.
/// Quest 등에서 손 추적과 컨트롤러가 동시에 살아 있는 경우가 많아,
/// "손 추적이면 막지 않음" 예외를 두면 컨트롤러로 UI 이동/레이 입력이 손가락 overlap과 충돌합니다.
/// 순수 손만 쓸 때 검지 UI는 FingerTriggerAreaEvents의 useAllActiveHandsForFingerOverlap 등으로 보완합니다.
/// </summary>
public static class VrFingerUiInputGate
{
    static readonly List<InputDevice> s_deviceBuffer = new List<InputDevice>();

    /// <summary>
    /// true면 손가락 UI 입력을 처리하지 않아야 합니다.
    /// </summary>
    public static bool BlockFingerUiWhileControllerTracked(Autohand.Hand hand)
    {
        if (hand == null)
            return false;

        var link = hand.GetComponentInParent<Autohand.HandControllerLink>(true);
        if (link == null)
            link = hand.GetComponentInChildren<Autohand.HandControllerLink>(true);
        if (link == null)
            return false;

        XRNode node = hand.left ? XRNode.LeftHand : XRNode.RightHand;
        s_deviceBuffer.Clear();
        InputDevices.GetDevicesAtXRNode(node, s_deviceBuffer);
        if (s_deviceBuffer.Count == 0)
            return false;

        InputDevice device = s_deviceBuffer[0];
        if (!device.isValid)
            return false;

        if (device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked)
            return true;

        return false;
    }
}
