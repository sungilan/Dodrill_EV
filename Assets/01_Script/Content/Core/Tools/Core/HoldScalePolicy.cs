using UnityEngine;

/// <summary>
/// PC·모바일이 물건을 들 때 '화면의 40% 이하'로 줄이는 홀드 스케일(FreeLookController/FreeLockControllerMobile.ApplyHoldScale)을 적용하지 않을 물건.
///
/// 줄이는 건 드릴·태블릿 같은 작은 도구가 화면을 가리지 않게 하려던 것인데, 크기가 큰 장비(오일 받이·크리퍼·사다리·바스켓·휠 등)까지 잡으면
/// 작아져 보이고, 놓을 때 원래 크기로 돌려놓지 못하는 경우도 있었다. 바닥을 끌거나 크게 쓰는 장비는 원래 크기 그대로 든다.
/// </summary>
public static class HoldScalePolicy
{
    public static bool IsExempt(Transform t)
    {
        if (t == null) return false;
        return t.GetComponentInParent<FloorDragProp>() != null
            || t.GetComponentInParent<CarryBasket>() != null
            || t.GetComponentInParent<LadderProp>() != null
            || t.GetComponentInParent<CreeperRide>() != null
            || t.GetComponentInParent<TireWheel>() != null
            || t.GetComponentInParent<EquipmentBase>() != null;
    }
}
