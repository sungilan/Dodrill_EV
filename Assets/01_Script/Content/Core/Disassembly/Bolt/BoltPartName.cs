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
            // 볼트 묶음 이름이 한글 사전에 있으면 그것('앞 커버 볼트' → '앞 커버'), 없으면 묶음이 가리키는 부품 이름
            string gn = HoverNameFormatter.Format(g.name);
            if (HasKorean(gn)) return StripBolt(gn);
            if (g.targetPart != null) return HoverNameFormatter.Format(g.targetPart.name);
            return gn;
        }
        return null;
    }

    private static bool HasKorean(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s) if (c >= 0xAC00 && c <= 0xD7A3) return true;
        return false;
    }

    private static string StripBolt(string s)
    {
        if (s.EndsWith(" 조립 볼트")) return s.Substring(0, s.Length - 5) + " 조립";
        if (s.EndsWith(" 볼트")) return s.Substring(0, s.Length - 3);
        return s;
    }

    /// <summary>제목 뒤에 부품 이름을 붙인다(예: '볼트 분해 — 프런트 언더 커버'). 이름을 못 찾으면 제목 그대로.</summary>
    public static string Title(string title, Bolt b)
    {
        string n = Of(b);
        return string.IsNullOrEmpty(n) ? title : $"{title} — {n}";
    }
}
