using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// ============================================================
//  TireService.Tray.cs — 분해 부품 보관함에 휠 넣기 (11/19)
//
//  차 부품 보관함(CarPartService.Tray.cs)과 같은 방식으로 떼어낸 휠도 보관함에 넣고 꺼낸다.
//  · 휠은 어느 허브에나 끼울 수 있어(TireWheel — 가까운 빈 허브) 아이템이 허브에 묶여 있지 않다.
//    그래서 '보관 중' 표시는 허브(앞 왼쪽 등)마다 두고, 넣을 때는 아직 보관 표시가 없는 빈 허브 중 마지막에 뗀 허브 몫으로 넣는다.
//  · 떼어낸 순서 번호는 차 부품과 한 줄(CarPartService.TrayNextOrder)이라 보관함에서 부품·휠이 뗀 순서대로 섞여 보인다.
//  · 보관함에 휠이 든 허브에 다른 휠(예비 휠 등)을 끼우면, 보관 중 표시를 아직 비어 있는 다른 허브로 옮긴다(옮길 곳이 없으면 지운다).
// ============================================================
public partial class TireService
{
    private readonly SyncList<int> _wheelOrder = new SyncList<int>();   // 허브별 떼어낸 순서(0 = 기록 없음)
    private readonly SyncList<bool> _wheelIn = new SyncList<bool>();    // 허브별 보관함에 휠이 들어 있나
    private Vector3? _spawnAt;                                          // SpawnWheel 이 쓸 위치(보관함에서 꺼낼 때만)

    /// <summary>휠 너트 규정 토크(kgf·m) — 아이오닉 5 정비 매뉴얼 휠 너트 11~13.</summary>
    public const float NutTorqueMin = 11f, NutTorqueMax = 13f;
    public static readonly string[] HubNames = { "앞 왼쪽 휠", "앞 오른쪽 휠", "뒤 왼쪽 휠", "뒤 오른쪽 휠" };

    // ── 읽기(클라·서버) ──
    public bool TrayHas(int w) => w >= 0 && w < _wheelIn.Count && _wheelIn[w];
    public int RemovalOrder(int w) => w >= 0 && w < _wheelOrder.Count ? _wheelOrder[w] : 0;

    /// <summary>보관함에 휠을 하나 더 넣을 자리(뗀 상태이고 아직 보관 표시가 없는 허브)가 있나.</summary>
    public bool TrayCanStore => TrayStoreSlot() >= 0;

    private int TrayStoreSlot()
    {
        int best = -1, bestOrd = -1;
        for (int w = 0; w < Wheels && w < _state.Count && w < _wheelIn.Count; w++)
        {
            if (_state[w] != (int)WheelState.Off || _wheelIn[w]) continue;
            int o = RemovalOrder(w);
            if (o > bestOrd) { bestOrd = o; best = w; }
        }
        return best;
    }

    /// <summary>
    /// 그 대상(휠 너트·너트 묶음)이 속한 바퀴의 렌더러. 아니면 null.
    /// 집중 모드가 너트를 작업할 때 그 바퀴(타이어·림)를 '가리는 것'으로 보고 반투명하게 만들던 문제 — 너트는 바퀴 바깥 면에 붙어 있어 바퀴 경계 상자 안에 든다.
    /// </summary>
    public Renderer[] WheelRenderersFor(Transform t)
    {
        if (t == null || !_found) return null;
        for (int i = 0; i < Wheels; i++)
        {
            var h = _hubs[i];
            if (h == null || h.wheel == null) continue;
            if (t.IsChildOf(h.wheel) || (h.frame != null && t.IsChildOf(h.frame)) || (h.nutRoot != null && t.IsChildOf(h.nutRoot))) return h.wheelRenderers;
            if (h.wheel.IsChildOf(t)) continue;   // 차 전체 같은 큰 대상
            foreach (var n in h.nuts) if (n != null && n.transform.IsChildOf(t)) return h.wheelRenderers;
        }
        return null;
    }

    // ── 서버 ──
    [Server]
    private void TrayServerInit()
    {
        if (_wheelOrder.Count == 0) for (int i = 0; i < Wheels; i++) _wheelOrder.Add(0);
        if (_wheelIn.Count == 0) for (int i = 0; i < Wheels; i++) _wheelIn.Add(false);
    }

    [Server]
    private void TrayNoteRemoved(int w)
    {
        if (w < 0 || w >= _wheelOrder.Count) return;
        var svc = CarPartService.Instance;
        _wheelOrder[w] = svc != null ? svc.TrayNextOrder() : 0;
    }

    [Server]
    private void TrayOnMounted(int w)
    {
        if (!TrayHas(w)) return;
        _wheelIn[w] = false;
        int to = TrayStoreSlot();
        if (to >= 0) _wheelIn[to] = true;
    }

    [Server]
    public void TrayServerClear()
    {
        for (int i = 0; i < _wheelOrder.Count; i++) _wheelOrder[i] = 0;
        for (int i = 0; i < _wheelIn.Count; i++) _wheelIn[i] = false;
    }

    // ── 넣기 ──
    /// <summary>[클라] 손에 든 휠(TireWheel)을 보관함에 넣는다. 부르기 전에 손에서 놓는다(PartTray.Store 가 처리).</summary>
    public void RequestTrayStore(NetworkObject wheel)
    {
        if (IsSpawned && wheel != null) TrayStoreServerRpc(wheel);
    }

    [ServerRpc(RequireOwnership = false)]
    private void TrayStoreServerRpc(NetworkObject wheel)
    {
        if (wheel == null || !wheel.IsSpawned || wheel.GetComponent<TireWheel>() == null) return;
        int w = TrayStoreSlot();
        if (w < 0) return;   // 뗀 허브가 없다(예비 휠을 더 넣는 경우 등) — 넣지 않는다
        _wheelIn[w] = true;
        ServerManager.Despawn(wheel);
    }

    // ── 끼우기만 한 휠 다시 빼기 ──
    public bool IsWheelMounted(int w) => w >= 0 && w < _state.Count && _state[w] == (int)WheelState.Mounted;

    /// <summary>장착(체결 전)이고 규정 토크까지 조인 너트가 하나도 없으면 다시 뺄 수 있다.</summary>
    public bool CanUnmount(int w) => IsWheelMounted(w) && NutsTightened(w) == 0;

    public void RequestUnmount(int w)
    {
        if (IsSpawned) UnmountServerRpc(w);
    }

    [ServerRpc(RequireOwnership = false)]
    private void UnmountServerRpc(int w)
    {
        if (!CanUnmount(w)) return;
        for (int n = 0; n < NutsPerWheel; n++) _nut[w * NutsPerWheel + n] = 0f;
        _state[w] = (int)WheelState.Off;
        SpawnWheel(w);
    }

    // ── 꺼내기 ──
    public void RequestTrayTake(int w, Vector3 at)
    {
        if (IsSpawned) TrayTakeServerRpc(w, at);
    }

    [ServerRpc(RequireOwnership = false)]
    private void TrayTakeServerRpc(int w, Vector3 at)
    {
        if (!TrayHas(w) || w >= _state.Count || _state[w] != (int)WheelState.Off) return;
        if (ServerHubPose(w, out var hub, out _) && (at - hub).sqrMagnitude > 15f * 15f) at = hub + Vector3.up * 0.3f;
        _wheelIn[w] = false;
        _spawnAt = at;
        try { SpawnWheel(w); }
        finally { _spawnAt = null; }
    }
}
