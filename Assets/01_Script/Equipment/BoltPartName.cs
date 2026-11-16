using UnityEngine;

/// <summary>
/// 지금 돌리고 있는 볼트가 '어느 부품의 볼트'인지 한글 이름으로 알려 준다(볼트 진행 패널 제목에 붙인다).
/// 차체 부품 탈거(CarPartService)·휠 너트(TireService)·시나리오 볼트 묶음(BoltGroupCounter)을 차례로 찾는다.
/// </summary>
public static class BoltPartName
{
    public static string Of(Bolt b)
    {
        if (b == null) return null;
        string n = CarPartService.NameOfBolt(b);
        if (!string.IsNullOrEmpty(n)) return n;
        n = TireService.NameOfBolt(b);
        if (!string.IsNullOrEmpty(n)) return n;
        var g = b.GetComponentInParent<BoltGroupCounter>();
        if (g != null)
        {
            string raw = g.targetPart != null ? g.targetPart.name : g.name;
            if (!string.IsNullOrEmpty(raw)) return HoverNameFormatter.Format(raw);
        }
        return null;
    }

    /// <summary>제목 뒤에 부품 이름을 붙인다(예: '볼트 분해 — 프런트 언더 커버'). 이름을 못 찾으면 제목 그대로.</summary>
    public static string Title(string title, Bolt b)
    {
        string n = Of(b);
        return string.IsNullOrEmpty(n) ? title : $"{title} — {n}";
    }
}
