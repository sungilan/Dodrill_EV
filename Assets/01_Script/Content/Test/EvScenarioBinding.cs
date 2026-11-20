using UnityEngine;

// ============================================================
//  EvScenarioBinding.cs — EV 콘텐츠가 시나리오 엔진(ScenarioHooks)에 자기 기능을 등록하는 곳 (모듈화 1단계, 2026-11-20)
//
//  엔진이 EV 클래스(EVModules·ClickableAnimatorManager·LiftControllerBase·GdsTablet·ImpactWrench·VehicleLiftController·DemoSettings)를
//  직접 부르던 것을 이 파일 한 곳으로 모았다. 엔진 쪽은 ScenarioHooks 만 안다.
//  씬이 로드되기 전에 한 번 실행된다(서버·클라이언트·에디터 플레이 공통).
// ============================================================
public static class EvScenarioBinding
{
    private const int EvModuleOrder = 100;   // 간호(0) 다음

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        ScenarioHooks.AddModuleProvider("EV", EvModuleOrder, EVModules.RegisterAll);

        ScenarioHooks.RecentActor = ClickableAnimatorManager.TryGetRecentActor;
        ScenarioHooks.LiftActor = ResolveLiftActor;

        // 진단기(GDS)의 OBD 존 신호는 진단기 화면(원인 확인·고장 코드 소거)에서 낸 것만 서버로 보낸다.
        // OBD 단자 접촉, PC E키/클릭 등으로 연결만 해도 진단 단계가 넘어가던 문제 방지.
        // 사다리·차체·볼트가 OBD 존 상자에 닿아도 신호가 나가 서버에 '잘못된 아이템' 위반이 쌓였다 → 진단기 화면 신호만 통과
        ScenarioHooks.ZoneSignalFilter = (zoneId, itemId) =>
        {
            if (zoneId == "OBD_Zone" && !GdsTablet.ObdSignalOpen)
            {
                Debug.Log($"<color=cyan>[Bridge]</color> OBD_Zone 신호 무시({itemId}) — 진단기 화면에서 진단을 진행해야 완료됩니다.");
                return false;
            }
            return true;
        };

        // 사람마다 따로 쓰는 공구(임팩트 렌치)는 인원수만큼 스폰한다
        ScenarioHooks.IsPerPlayerPrefab = prefab => prefab.GetComponentInChildren<ImpactWrench>(true) != null;

        // 차량이 스폰되면 리프트에 자동 연결
        ScenarioHooks.OnMapObjectSpawned = go =>
        {
            string id = go.name;
            bool isVehicle = id.Contains("Ioniq") || id.Contains("Vehicle") || id.Contains("Car");
            if (!isVehicle) return;
            var lift = Object.FindFirstObjectByType<VehicleLiftController>();
            if (lift != null)
            {
                lift.AttachVehicle(go.transform);
                Debug.Log($"[ScenarioSpawner] VehicleLiftController ← {go.name} 자동 연결");
            }
        };

        // 커넥터 단계: 클릭 신호가 아니라 커넥터의 열림/닫힘 상태로 완료를 판정한다
        ScenarioHooks.TargetStateOk = ConnectorStateOk;

        // 시연용 스위치(DemoSettings)
        ScenarioHooks.SpawnFilter = DemoSettings.SkipSpawn;
        ScenarioHooks.ScenarioListFilter = DemoSettings.ScenarioAllowed;
    }

    /// <summary>
    /// 분리 단계(…Disconnect / …Removal)는 커넥터가 열려(분리) 있어야, 연결 단계(…Connect / …Reinstall)는 닫혀(연결) 있어야 완료로 본다.
    /// 대상은 Conn_ 으로 시작하는 커넥터(고전압·ICCU·BMU·접지)만. 다른 오브젝트·모르는 단계 이름은 예전처럼 클릭만으로 통과.
    /// (예전: 클릭하면 상태와 상관없이 신호가 나가, 후방 커넥터를 재연결하다 '열렸는데' 단계가 넘어갔다)
    /// 서버에서도 이 오브젝트(ClickableAnimator)가 같은 상태로 적용돼 있어 서버가 직접 판정한다.
    /// </summary>
    private static bool ConnectorStateOk(string moduleId, string targetObjName)
    {
        if (string.IsNullOrEmpty(targetObjName) || !targetObjName.StartsWith("Conn_")) return true;
        var anim = ClickableAnimatorManager.Get(targetObjName);
        if (anim == null) return true;
        bool wantOpen;
        if (moduleId.IndexOf("Disconnect", System.StringComparison.OrdinalIgnoreCase) >= 0 || moduleId.IndexOf("Removal", System.StringComparison.OrdinalIgnoreCase) >= 0) wantOpen = true;
        else if (moduleId.IndexOf("Connect", System.StringComparison.OrdinalIgnoreCase) >= 0 || moduleId.IndexOf("Reinstall", System.StringComparison.OrdinalIgnoreCase) >= 0) wantOpen = false;
        else return true;
        return anim.IsOpen == wantOpen;
    }

    /// <summary>리프트 상승 단계(차량 Car_Lift_Up / 배터리 잭 BatteryJack_Lift_Up…)를 시작한 뒤 해당 리프트 UP 버튼을 누른 사람</summary>
    private static bool ResolveLiftActor(bool battery, float sinceTime, out int clientId)
    {
        clientId = -1;
        foreach (var lift in Object.FindObjectsByType<LiftControllerBase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (battery != (lift is BatteryLiftController)) continue;
            if (lift.TryGetMoveActor(sinceTime, out clientId)) return true;
        }
        clientId = -1;
        return false;
    }
}
