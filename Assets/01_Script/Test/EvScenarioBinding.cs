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

        // 시연용 스위치(DemoSettings)
        ScenarioHooks.SpawnFilter = DemoSettings.SkipSpawn;
        ScenarioHooks.ScenarioListFilter = DemoSettings.ScenarioAllowed;
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
