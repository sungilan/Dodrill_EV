using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  ManualTips.cs
//
//  도구·보호구를 처음 집을 때 정비 매뉴얼의 규정값·점검 방법을 짧게 띄운다(본인 화면만).
//  · 절연 장갑: 공기 누설 점검법 / 멀티미터: 30V 이하·0V / 메가: 1000V 1분·2MΩ / 진단기: 융착·500kΩ …
//  · 탈거한 고전압 부품(기존 PRA)을 내려놓을 때 절연 매트 위가 아니면 경고
//  씬에 따로 둘 필요 없이 게임 시작 시 스스로 붙는다.
// ============================================================
public class ManualTips : MonoBehaviour
{
    private struct Tip { public string title, body; public bool warn; }

    private static readonly Dictionary<string, Tip> Tips = new Dictionary<string, Tip>(System.StringComparer.OrdinalIgnoreCase)
    {
        ["InsulatedGloves"] = new Tip { title = "절연 장갑 — 착용 전 점검", body = "장갑을 접고 3~4번 더 말아 공기를 가둔 뒤 찢어짐·누설을 확인. 물기 제거 후 착용 (1000V / 300A 이상)" },
        ["FaceShield"] = new Tip { title = "보안경·안면 보호대", body = "스파크가 날 수 있는 고전압 단자·배선 탈착, 배터리 어셈블리 작업 시 착용" },
        ["InsulatedShoes"] = new Tip { title = "절연화 — 금속 장신구 제거", body = "시계·반지 등 금속은 고전압 단락을 일으킬 수 있으니 작업 전에 몸에서 빼 둡니다" },
        ["GDS_Tablet"] = new Tip { title = "진단기 점검 기준", body = "POWER ON(EV READY 금지) · BMS 융착: Relay Welding not detection · 절연 저항 500kΩ 이상" },
        ["Multimeter"] = new Tip { title = "전압 측정 기준", body = "서비스 플러그 분리 후 3분 이상 대기 → 인버터 단자 간 30V 이하 · 배터리 리어 커넥터 단자 간 0V(릴레이 융착 점검)" },
        ["Megger"] = new Tip { title = "절연저항 측정 기준", body = "(−) 리드는 케이스 접지, (+) 리드는 PRA 단자 · 1000V 인가 후 약 1분 대기 · 2MΩ 이상(20°C)" },
        ["Equip_Spanner_Insulated"] = new Tip { title = "절연 공구 세트 (09090-09000)", body = "고전압 부품 작업은 절연 공구로. 분리한 고전압 케이블은 바로 절연 테이프로 감싸고 (+)(−) 단자끼리 닿지 않게" },
        ["Equip_Screwdriver_Insulated"] = new Tip { title = "절연 공구 세트 (09090-09000)", body = "고전압 부품 작업은 절연 공구로. 분리한 고전압 케이블은 바로 절연 테이프로 감싸고 (+)(−) 단자끼리 닿지 않게" },
        ["Equip_Extinguisher"] = new Tip { title = "전기차 화재 대응", body = "소규모 화재는 ABC·BC 소화기. 하부 구동 배터리 화재는 다량의 물을 오래 — 진압이 어려우면 대피 후 119에 '전기차 화재' 신고", warn = true },
        ["Equip_BatteryLift"] = new Tip { title = "리프트 테이블 — 배터리 탈거", body = "배터리 케이스를 리프트 포인트로 쓰지 않습니다. 조금 들어 안착을 눈으로 확인한 뒤 끝까지 올리고 다시 확인" },
        ["BatteryJackA"] = new Tip { title = "리프트 테이블 — 배터리 탈거", body = "배터리 케이스를 리프트 포인트로 쓰지 않습니다. 조금 들어 안착을 눈으로 확인한 뒤 끝까지 올리고 다시 확인" },
        ["Equip_WheelChock"] = new Tip { title = "타이어 고임목", body = "차량이 갑자기 움직이지 않도록 타이어 앞·뒤에 받칩니다 (주차 브레이크·P 위치와 함께)" },
        ["Equip_Creeper"] = new Tip { title = "차체 하부 작업", body = "차 밑에서 작업할 때는 반드시 리프트 잠금·안전 스탠드로 차를 받친 상태에서" },
    };

    /// <summary>탈거 후 절연 매트에 두어야 하는 고전압 부품</summary>
    private static readonly HashSet<string> HvParts = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Old_PRA_Unit" };

    private static ManualTips s_inst;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (s_inst != null) return;
        var go = new GameObject("[ManualTips]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        s_inst = go.AddComponent<ManualTips>();
    }

    private void OnEnable() => InteractionEvents.OnItemGrabbed += OnGrabbed;
    private void OnDisable() => InteractionEvents.OnItemGrabbed -= OnGrabbed;

    private void OnGrabbed(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (Tips.TryGetValue(id, out var t))
            EquipFx.Notice("manual_" + id, t.title, t.body, t.warn ? EquipFx.Warn : EquipFx.Info, 4.5f, 120f);
        if (HvParts.Contains(id)) StartCoroutine(WatchRelease(id));
    }

    private IEnumerator WatchRelease(string id)
    {
        yield return new WaitForSeconds(0.3f);
        SyncGrab held = null;
        foreach (var g in FindObjectsByType<SyncGrab>(FindObjectsSortMode.None))
        {
            if (!g.IsGrabbed || !g.IsOwner) continue;
            var ti = g.GetComponent<TaskItem>();
            if (ti != null && string.Equals(ti.prefabId, id, System.StringComparison.OrdinalIgnoreCase)) { held = g; break; }
        }
        if (held == null) yield break;
        while (held != null && held.IsGrabbed) yield return null;
        if (held == null) yield break;
        yield return new WaitForSeconds(1.2f);   // 떨어져 자리 잡을 시간
        if (held == null || held.IsGrabbed || !held.gameObject.activeInHierarchy) yield break;

        if (InsulationMat.Contains(held.transform.position))
            EquipFx.Notice("hv_mat_ok", "절연 매트에 보관", "탈거한 고전압 부품을 절연 매트 위에 두었습니다", EquipFx.Info, 2.5f, 10f);
        else
            EquipFx.Notice("hv_mat", "고전압 부품은 절연 매트 위에!", "탈거한 고전압 부품은 누전·감전 예방을 위해 절연 매트 위에 정리해 보관합니다", EquipFx.Warn, 4f, 10f);
    }
}
