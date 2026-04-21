using UnityEngine;

/// <summary>
/// World-space UI는 팝업을 숨길 때 오브젝트는 켜 둔 채 <see cref="CanvasGroup"/>만 끄는 경우가 많습니다.
/// BoxCollider는 남아 있어 손가락이 숨겨진 버튼에 닿을 수 있으므로, 조상 CanvasGroup을 검사합니다.
/// </summary>
public static class VrFingerUiCanvasGate
{
    const float MinAlpha = 0.01f;

    public static bool AreAncestorsAllowingFinger(Transform from)
    {
        if (from == null)
            return true;

        var groups = from.GetComponentsInParent<CanvasGroup>(true);
        for (int i = 0; i < groups.Length; i++)
        {
            CanvasGroup cg = groups[i];
            if (cg == null)
                continue;
            if (!cg.interactable || cg.alpha < MinAlpha)
                return false;
        }

        return true;
    }
}
