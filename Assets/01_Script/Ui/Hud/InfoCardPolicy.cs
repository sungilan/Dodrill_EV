// ============================================================
//  InfoCardPolicy.cs — 장비 설명 카드(WarningPopup "equip")를 보일지 (VrPalmMenu 에서 분리, 11/20)
//  VR 에서는 손바닥 메뉴의 '설명창' 토글로 켜야 보이고, PC·모바일은 항상 보인다. EquipFx 가 VrPalmMenu 를 직접 부르지 않게 정적 클래스로 뺐다.
// ============================================================
public static class InfoCardPolicy
{
    public static bool ShowInfoCards = false;
    public static bool Allowed => ShowInfoCards || !VrDevice.IsActive;
}
