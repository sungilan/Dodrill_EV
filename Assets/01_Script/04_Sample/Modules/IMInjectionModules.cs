// ============================================================
//  IMInjectionModules.cs
//  근육주사(IM) 관련 모듈
// ============================================================

/// <summary>IM 주사 준비 — 주사기 + 약물 + 증류수 집기</summary>
public class PrepareMedication_IMModule : GrabAllItemsModule
{
    public override string ModuleId => "PrepareMedication_IM";
}

/// <summary>주사 부위 선정 — 빈손으로 InjectionSite 존 터치</summary>
public class SelectInjectionSiteModule : GrabZoneTaskModule
{
    public override string ModuleId => "SelectInjectionSite";
}

/// <summary>주사 부위 소독 — 알코올 솜 → InjectionSite 존</summary>
public class DisinfectSiteModule : GrabZoneTaskModule
{
    public override string ModuleId => "DisinfectSite";
}

/// <summary>근육주사 투여 — 주사기 → InjectionSite 존</summary>
public class AdministerIMModule : GrabZoneTaskModule
{
    public override string ModuleId => "AdministerIM";
}

/// <summary>주사 후 처리 (UI 확인)</summary>
public class PostInjectionCareModule : ConfirmTaskModule
{
    public override string ModuleId => "PostInjectionCare";
}
