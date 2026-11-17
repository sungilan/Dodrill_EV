// ============================================================
//  EVFreeModules.cs
//  자유 탈거 모드 시나리오(ev_free_disassembly) — 시나리오 단계 없이 차를 마음대로 분해·조립해 보는 연습용.
//  단계는 하나뿐이고 끝내는 방법은 '완료' 버튼(ConfirmTask)이다. 부품 탈거·조립은 CarPartService(자유 실습)가 처리한다.
// ============================================================

public class Free_DisassemblyModule : ConfirmTaskModule
{
    public override string ModuleId => "Free_Disassembly";
}

public static class EVFreeModules
{
    public static void RegisterAll(ModuleRegistry registry)
    {
        registry.Register(new Free_DisassemblyModule());
    }
}
