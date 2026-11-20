using UnityEngine;

// ============================================================
//  WorldClickPriority.cs — 클릭 대상 고르기 (VehicleSync.cs 에서 이동, 11/20)
//  ClickableAnimator(씬 고정 오브젝트 클릭) 와 IWorldClickable(Interaction) 을 함께 아는 EV 규칙이라 Content 에 둔다.
// ============================================================
/// <summary>
/// 클릭 대상 고르기: 프렁크 덮개·냉각수 뚜껑처럼 후드(ClickableAnimator) 아래에 달린 IWorldClickable 은 후드보다 먼저 눌려야 한다.
/// (예전엔 부모 쪽 후드가 먼저 잡혀 덮개를 닫으려 하면 후드가 닫히고, 뚜껑을 눌러도 후드만 움직였다)
/// </summary>
public static class WorldClickPriority
{
    /// <summary>콜라이더에서 가장 가까운 IWorldClickable 이 ClickableAnimator 보다 안쪽(같거나 자손)이면 그것, 아니면 null.</summary>
    public static IWorldClickable Nearer(Collider col)
    {
        if (col == null) return null;
        var wc = col.GetComponentInParent<IWorldClickable>();
        if (wc == null) return null;
        var ca = col.GetComponentInParent<ClickableAnimator>();
        if (ca == null) return wc;
        var wt = ((Component)wc).transform;
        return wt == ca.transform || wt.IsChildOf(ca.transform) ? wc : null;
    }
}
