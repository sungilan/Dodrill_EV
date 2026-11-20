using System.Collections.Generic;
using Autohand;
using FishNet.Object;
using UnityEngine;

// ============================================================
//  PartTray.cs — 보관함 목록 항목(차 부품 + 휠)을 한 가지로 다루는 도우미 (11/19)
//
//  보관함 화면(PC·모바일 PartTrayPanel, VR VrPartTray)은 항목을 정수 코드 하나로 본다.
//  · 0 이상 WheelBase 미만 : 차 부품 번호(CarPartService.Defs 인덱스)
//  · WheelBase + w        : 휠(0=앞왼쪽 1=앞오른쪽 2=뒤왼쪽 3=뒤오른쪽, TireService)
//  서버 상태는 CarPartService.Tray.cs / TireService.Tray.cs 가 들고 있다.
// ============================================================
public static class PartTray
{
    public const int WheelBase = 100000;

    public static bool IsWheel(int code) => code >= WheelBase;

    /// <summary>지금 차에서 빠져 있는 항목들 — 마지막에 뗀 것이 앞.</summary>
    public static void Collect(List<int> list)
    {
        list.Clear();
        var svc = CarPartService.Instance;
        if (svc != null)
            for (int p = 0; p < CarPartService.Defs.Length; p++)
                if (svc.StateOf(p) == (int)CarPartService.PartState.Off) list.Add(p);
        var ts = TireService.Instance;
        if (ts != null)
            for (int w = 0; w < TireService.Wheels; w++)
                if (ts.IsWheelOff(w)) list.Add(WheelBase + w);
        list.Sort((a, b) => Order(b).CompareTo(Order(a)));
    }

    public static int Order(int code)
    {
        if (IsWheel(code)) { var ts = TireService.Instance; return ts != null ? ts.RemovalOrder(code - WheelBase) : 0; }
        var svc = CarPartService.Instance;
        return svc != null ? svc.RemovalOrder(code) : 0;
    }

    public static bool Stored(int code)
    {
        if (IsWheel(code)) { var ts = TireService.Instance; return ts != null && ts.TrayHas(code - WheelBase); }
        var svc = CarPartService.Instance;
        return svc != null && svc.TrayHas(code);
    }

    /// <summary>보관함에 넣을 수 있는 종류인가(대형 부품은 아님).</summary>
    public static bool Allowed(int code) => IsWheel(code) || CarPartService.TrayAllowed(code);

    public static string Name(int code)
    {
        if (IsWheel(code)) return TireService.HubNames[Mathf.Clamp(code - WheelBase, 0, TireService.Wheels - 1)];
        return CarPartService.Defs[code].name;
    }

    /// <summary>조립할 때 공구 · 규정 토크.</summary>
    public static string ToolLine(int code)
    {
        if (IsWheel(code)) return $"에어 임팩렌치(휠 너트 소켓) · {TireService.NutTorqueMin:0.#}~{TireService.NutTorqueMax:0.#} kgf·m";
        var d = CarPartService.Defs[code];
        return $"{d.tool} · {d.tMin:0.0#}~{d.tMax:0.0#} kgf·m";
    }

    public static void Take(int code, Vector3 at)
    {
        if (IsWheel(code)) TireService.Instance?.RequestTrayTake(code - WheelBase, at);
        else CarPartService.Instance?.RequestTrayTake(code, at);
    }

    // ── 손에 든 것 ──

    /// <summary>내가 들고 있는(벨트 아님) 떼어낸 부품 또는 휠. 없으면 null.</summary>
    public static SyncGrab FindHeld(out int code)
    {
        code = -1;
        foreach (var it in Object.FindObjectsByType<CarPartItem>(FindObjectsSortMode.None))
        {
            if (it == null || it.Part < 0) continue;
            var sg = it.GetComponent<SyncGrab>();
            if (sg != null && sg.IsOwner && sg.IsGrabbed && !sg.IsHolstered) { code = it.Part; return sg; }
        }
        foreach (var tw in Object.FindObjectsByType<TireWheel>(FindObjectsSortMode.None))
        {
            if (tw == null) continue;
            var sg = tw.GetComponent<SyncGrab>();
            if (sg != null && sg.IsOwner && sg.IsGrabbed && !sg.IsHolstered) { code = WheelBase; return sg; }
        }
        return null;
    }

    /// <summary>들고 있는 것을 지금 넣을 수 있나. 휠은 뗀 허브 중 아직 보관 표시가 없는 곳이 있어야 한다(예비 휠만 더 넣는 것은 막음).</summary>
    public static bool CanStore(SyncGrab sg, int code)
    {
        if (sg == null || code < 0) return false;
        if (IsWheel(code)) { var ts = TireService.Instance; return ts != null && ts.TrayCanStore; }
        var svc = CarPartService.Instance;
        return svc != null && svc.StateOf(code) == (int)CarPartService.PartState.Off && !svc.TrayHas(code);
    }

    /// <summary>손(PC 들기·VR 손 모두)에서 놓고 서버에 보관을 요청한다. 표시할 이름을 돌려준다.</summary>
    public static string Store(SyncGrab sg, int code)
    {
        var nob = sg != null ? sg.GetComponent<NetworkObject>() : null;
        if (nob == null) return null;
        var cpi = sg.GetComponent<CarPartItem>(); if (cpi != null) cpi.StoringToTray = true;   // 장착 자리 가까이서 넣어도 끼워지지 않게
        var tw = sg.GetComponent<TireWheel>(); if (tw != null) tw.StoringToTray = true;
        // 먼저 손에서 놓는다(OnReleased 로 '들고 있음'이 풀린다) → 서버가 보관함에 넣고 아이템을 지운다
        var g = sg.GetComponentInChildren<Grabbable>();
        if (g != null && g.GetHeldBy().Count > 0) g.ForceHandsRelease();   // VR 손
        else { sg.StopPCHold(); sg.RequestRelease(); }                   // PC·모바일 들기
        if (IsWheel(code))
        {
            var ts = TireService.Instance;
            ts?.RequestTrayStore(nob);
            return "휠";
        }
        CarPartService.Instance?.RequestTrayStore(code, nob);
        return CarPartService.Defs[code].name;
    }
}
