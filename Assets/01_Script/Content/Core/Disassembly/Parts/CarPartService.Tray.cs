using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// ============================================================
//  CarPartService.Tray.cs — 분해 부품 보관함 (11/19)
//
//  실제 정비에서 떼어낸 볼트·부품을 트레이에 순서대로 모아 두고 조립 때 역순으로 꺼내 쓰는 습관을 훈련한다.
//  · 떼어낸 순서를 서버가 기록한다(부위마다 #번호). 조립은 보통 그 역순이다.
//  · 손에 든 떼어낸 부품을 보관함에 넣으면 그 부품 아이템은 사라지고(디스폰) '보관 중'으로 표시된다.
//    바닥에 쌓여 길을 막던 문제도 줄어든다.
//  · 보관함에서 꺼내면 꺼낸 사람 앞에 그 부품이 다시 생긴다(바닥으로 떨어짐) — 그대로 들고 가서 원래 자리에 끼우면 된다.
//  · 크레인·배터리 리프트가 필요한 대형 부품(CarPartAccident.IsHeavy)은 넣을 수 없다.
//  보관함은 차 한 대에 하나 — 같은 방의 모든 사람이 같은 내용을 본다(SyncList).
//  화면은 PartTrayPanel.cs.
// ============================================================
public partial class CarPartService
{
    private readonly SyncList<int> _trayOrder = new SyncList<int>();   // 부위별 떼어낸 순서(0 = 기록 없음)
    private readonly SyncList<bool> _trayIn = new SyncList<bool>();    // 부위별 보관함에 들어 있나
    private int _trayCounter;
    private Vector3? _spawnAt;                                         // SpawnItem 이 쓸 위치(보관함에서 꺼낼 때만)

    // ── 읽기(클라·서버) ──
    public bool TrayHas(int p) => p >= 0 && p < _trayIn.Count && _trayIn[p];
    public int RemovalOrder(int p) => p >= 0 && p < _trayOrder.Count ? _trayOrder[p] : 0;
    public static bool TrayAllowed(int p) => p >= 0 && p < Defs.Length && !CarPartAccident.IsHeavy(Defs[p].id);

    // ── 서버 ──
    [Server]
    private void TrayServerInit()
    {
        if (_trayOrder.Count == 0) for (int i = 0; i < Defs.Length; i++) _trayOrder.Add(0);
        if (_trayIn.Count == 0) for (int i = 0; i < Defs.Length; i++) _trayIn.Add(false);
    }

    [Server]
    private void TrayNoteRemoved(int p)
    {
        if (p < 0 || p >= _trayOrder.Count) return;
        _trayOrder[p] = TrayNextOrder();
    }

    /// <summary>[서버] 다음 '떼어낸 순서' 번호. 휠(TireService.Tray.cs)도 같은 번호 줄을 쓴다 — 부품과 휠이 한 순서로 섞여 보이게.</summary>
    [Server]
    internal int TrayNextOrder() => ++_trayCounter;

    [Server]
    private void TrayServerClear()
    {
        _trayCounter = 0;
        for (int i = 0; i < _trayOrder.Count; i++) _trayOrder[i] = 0;
        for (int i = 0; i < _trayIn.Count; i++) _trayIn[i] = false;
    }

    // ── 넣기 ──
    /// <summary>[클라] 손에 든 떼어낸 부품을 보관함에 넣는다. 부르기 전에 손에서 놓아야 한다(PartTrayPanel 이 처리).</summary>
    public void RequestTrayStore(int p, NetworkObject item)
    {
        if (IsSpawned && item != null) TrayStoreServerRpc(p, item);
    }

    [ServerRpc(RequireOwnership = false)]
    private void TrayStoreServerRpc(int p, NetworkObject item)
    {
        if (p < 0 || p >= Defs.Length || p >= _trayIn.Count || item == null) return;
        if (_state[p] != (int)PartState.Off || _trayIn[p] || !TrayAllowed(p)) return;
        if (_item[p] != item.ObjectId) return;   // 다른 부위의 아이템이면 무시
        _trayIn[p] = true;
        _item[p] = -1;
        ServerManager.Despawn(item);
    }

    // ── 끼우기만 한 부품 다시 빼기 ──
    /// <summary>장착(체결 전)이고 규정 토크까지 조인 볼트가 하나도 없으며, 위에 다른 부품이 달려 있지 않으면 다시 뺄 수 있다.</summary>
    public bool CanUnmount(int p)
    {
        if (p < 0 || p >= Defs.Length || StateOf(p) != (int)PartState.Mounted) return false;
        return BoltsTightened(p) == 0 && CanRemove(p);
    }

    /// <summary>[클라] 끼우기만 한 부품을 다시 뺀다 — 그 자리에 부품 아이템이 다시 생긴다(떼어낸 순서 번호는 그대로).</summary>
    public void RequestUnmount(int p)
    {
        if (IsSpawned) UnmountServerRpc(p);
    }

    [ServerRpc(RequireOwnership = false)]
    private void UnmountServerRpc(int p)
    {
        if (!CanUnmount(p)) return;
        for (int n = 0; n < Defs[p].bolts.Length; n++) _prog[s_off[p] + n] = 0f;
        ClearLube(p);
        _state[p] = (int)PartState.Off;
        SpawnItem(p);
    }

    // ── 꺼내기 ──
    /// <summary>[클라] 보관함에서 꺼내 at 위치(보통 내 앞)에 놓는다.</summary>
    public void RequestTrayTake(int p, Vector3 at)
    {
        if (IsSpawned) TrayTakeServerRpc(p, at);
    }

    [ServerRpc(RequireOwnership = false)]
    private void TrayTakeServerRpc(int p, Vector3 at, NetworkConnection sender = null)
    {
        if (p < 0 || p >= _trayIn.Count || !_trayIn[p] || _state[p] != (int)PartState.Off) return;
        // 엉뚱한 곳(벽 너머·아주 먼 곳)에 만들지 않게 차에서 너무 멀면 차 옆으로
        Vector3 c = PartCenter(p);
        if ((at - c).sqrMagnitude > 15f * 15f) at = c + Vector3.up * 0.3f;
        _trayIn[p] = false;
        _spawnAt = at;
        try { SpawnItem(p); }
        finally { _spawnAt = null; }
    }
}
