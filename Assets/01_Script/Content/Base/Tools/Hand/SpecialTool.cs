using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  SpecialTool.cs
//  매뉴얼 '특수공구' 표의 공구 — 들고 옮기는 소품. 작업 규칙(CarPartRules.NeedsToolWarning)이 '작업 위치 가까이에 맞는 공구가 있는가'를 본다.
//  종류(toolId):
//    puller     볼 조인트 풀러 09568-GI000          — 타이로드 엔드 · 텐션/레터럴 암 볼 조인트 분리
//    strutnut   스트럿 너트 장착·탈거 공구 0K546-F6100 — 스트럿 인슐레이터 록 너트
//    shocknut   쇽 업소버 록 너트 리무버 09546-3X100  — 쇽 인슐레이터 록 너트
//    beam       엔진 서포트 지지대(빔) 09200-3N000     — 프런트 서브 프레임 탈거·장착
//    hanger     고전압 배터리 이송 행어 09375-K4400    — 배터리 시스템 어셈블리 이송(크레인 자키와 함께)
//    wheelsock  타이어 휠 탈장착 소켓 09529-N1000      — 휠 너트(참고용)
//  프리팹은 Editor/EquipmentBuilder 가 도형으로 만든다. 이름표(hover)는 SyncGrab/TaskItem 흐름을 따른다.
// ============================================================
public class SpecialTool : MonoBehaviour
{
    public static readonly List<SpecialTool> All = new List<SpecialTool>();

    public string toolId = "puller";
    public string partNo = "09568-GI000";
    public string toolName = "볼 조인트 풀러";

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    /// <summary>pos 에서 maxDist 안에 toolId 공구가 있는가</summary>
    public static bool NearTool(string toolId, Vector3 pos, float maxDist)
    {
        float m2 = maxDist * maxDist;
        foreach (var t in All)
            if (t != null && t.toolId == toolId && (t.transform.position - pos).sqrMagnitude <= m2) return true;
        return false;
    }

    /// <summary>배터리 팩 작업 장비(특수공구 표 · (P)HEV/EV 전용 장비 표) — id → 이름(품번)</summary>
    public static readonly Dictionary<string, string> BatteryTools = new Dictionary<string, string>
    {
        { "hangerknob", "이송 행어 고정 핸들(09375-K4104)" },
        { "modhanger", "배터리 모듈 행어 세트(09375-BF300)" },
        { "modguide", "배터리 모듈 가이드(09375-GI900)" },
        { "pressjig", "배터리 모듈 면압 지그(09375-GI800)" },
        { "guidepin", "배터리 시스템 장착 가이드 핀(09375-BF000)" },
        { "gapframe", "갭 필러 고정 틀(09375-GI100)" },
        { "gapgun", "갭 필러 작업 건(09375-GI200)" },
        { "gapribbon", "갭 필러 리본(09375-GI300)" },
        { "gaptube", "갭 필러 도포 튜브(09375-GI240)" },
        { "balancer", "모듈 충·방전 장비(xMB-9640)" },
        { "disjig", "배터리 모듈 분해 지그(TMS-1907)" },
        { "helium", "헬륨 가스·압력 조절기" },
        { "heliumdet", "헬륨 가스 감지기" },
        { "ventjig_top", "상부 벤팅 밸브 막음 지그" },
        { "ventjig_side", "측면 벤팅 밸브 막음 지그" },
    };

    public static string NameOf(string toolId)
    {
        if (BatteryTools.TryGetValue(toolId, out var bn)) return bn;
        switch (toolId)
        {
            case "puller": return "볼 조인트 풀러(09568-GI000)";
            case "strutnut": return "스트럿 너트 공구(0K546-F6100)";
            case "shocknut": return "쇽 록 너트 리무버(09546-3X100)";
            case "beam": return "엔진 서포트 지지대(09200-3N000)";
            case "hanger": return "고전압 배터리 이송 행어(09375-K4400)";
            case "crane": return "크레인 자키";
            default: return toolId;
        }
    }
}
