using UnityEngine;

// ============================================================
//  TorqueSpec.cs
//
//  아이오닉 5 정비 매뉴얼(160kW/270kW) 기준 체결 토크 규정값.
//  · 고전압 커넥터 커버  0.8 ~ 1.2 kgf·m   (고전압 차단 절차 8-(4))
//  · 프런트/리어 언더커버 0.8 ~ 1.2 kgf·m   (모터 및 감속기 - 언더 커버)
//  · 12V 배터리 단자     0.8 ~ 1.0 kgf·m   (차량 제어 - 12V 배터리)
//  · 배터리 팩 볼트 등 개별값이 매뉴얼에 없는 것은 '일반 조임 토크표' M8·8T(2.3 ~ 3.1) 를 쓴다
//  · 휠 너트            11 ~ 13 kgf·m
//  토크렌치는 Presets 중 하나로 맞춰 쓰고, 규정 범위 밖이면 조여지지 않는다.
// ============================================================
public static class TorqueSpec
{
    public struct Spec
    {
        public float min, max;
        public string part;     // 화면에 보일 부위 이름
        public string source;   // 근거(매뉴얼 항목)
        public string Range => $"{min:0.0#} ~ {max:0.0#} kgf·m";
    }

    /// <summary>토크렌치 설정 단계(kgf·m)</summary>
    public static readonly float[] Presets = { 0.5f, 0.9f, 1.2f, 2.7f, 5.0f, 12.0f };

    public static bool TryGet(Bolt b, out Spec s)
    {
        s = default;
        if (b == null) return false;
        if (b.GetComponent<FreeWorkBolt>() != null)
        {
            s = new Spec { min = 11f, max = 13f, part = "휠 너트", source = "승용 휠 너트 규정" };
            return true;
        }
        var g = b.GetComponentInParent<BoltGroupCounter>();
        string id = ((g != null ? g.taskId + " " + g.name : "") + " " + b.name).ToUpperInvariant();

        if (id.Contains("12V"))
        {
            s = id.Contains("CLAMP")
                ? new Spec { min = 0.9f, max = 1.4f, part = "12V 배터리 클램프", source = "12V 배터리 탈거 및 장착" }
                : new Spec { min = 0.8f, max = 1.0f, part = "12V 배터리 단자", source = "12V 배터리 탈거 및 장착" };
            return true;
        }
        if (id.Contains("CONNECTOR_COVER") || id.Contains("CONNECTORCOVER"))
        {
            s = new Spec { min = 0.8f, max = 1.2f, part = "고전압 커넥터 커버", source = "고전압 차단 절차 8-(4)" };
            return true;
        }
        if (!id.Contains("BATTERY") && (id.Contains("FRONT_COVER") || id.Contains("REAR_COVER")))
        {
            s = new Spec { min = 0.8f, max = 1.2f, part = id.Contains("FRONT") ? "프런트 언더커버" : "리어 언더커버", source = "언더 커버 탈거 및 장착" };
            return true;
        }
        if (g != null)
        {
            s = new Spec { min = 2.3f, max = 3.1f, part = "배터리·일반 볼트", source = "일반 조임 토크(M8·8T) — 개별값 미기재" };
            return true;
        }
        return false;
    }
}
