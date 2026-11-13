using UnityEngine;

// ============================================================
//  UtilityKnife.cs
//
//  커터칼. 누르면(VR 트리거 / PC F / 모바일 작업 버튼) 칼날이 '딸깍딸깍' 나오고, 다시 누르면 들어간다.
//  칼날이 나온 채로 놓으면 안전 안내. 칼날 모양(blade)은 모든 플레이어에게 같게 보인다.
// ============================================================
public class UtilityKnife : EquipmentBase
{
    protected override string MobileActionSub => "누르기";
    protected override string MobileActionName => "칼날 넣고 빼기";

    [Tooltip("칼날(로컬 +Z 로 밀려 나온다)")] public Transform blade;
    public float bladeTravel = 0.035f;

    private readonly FishNet.Object.Synchronizing.SyncVar<bool> _open = new FishNet.Object.Synchronizing.SyncVar<bool>();
    private Vector3 _bladeClosed;
    private float _t;
    private bool _prevInput;
    private bool _wasHeld;

    protected override void Awake()
    {
        base.Awake();
        if (blade != null) _bladeClosed = blade.localPosition;
    }

    protected override bool WantUsing(bool input) => false;

    protected override void Update()
    {
        base.Update();
        bool held = IsLocalHeld;
        if (held && LocalInput && !_prevInput) ToggleServerRpc(!_open.Value);
        _prevInput = held && LocalInput;

        if (_wasHeld && !held && _open.Value && Sync != null && Sync.IsOwner)
            EquipFx.Notice("knife_open", "칼날을 넣고 내려놓으세요", "칼날이 나온 커터칼은 다른 작업자가 집다가 다칠 수 있습니다", EquipFx.Warn, 3f, 10f);
        _wasHeld = held;

        float prev = _t;
        _t = Mathf.MoveTowards(_t, _open.Value ? 1f : 0f, Time.deltaTime * 5f);
        if (blade != null) blade.localPosition = _bladeClosed + Vector3.forward * (bladeTravel * _t / Mathf.Max(1e-4f, blade.parent != null ? blade.parent.lossyScale.z : 1f));
        if (Mathf.Floor(prev * 4f) != Mathf.Floor(_t * 4f))
        {
            AudioSource.PlayClipAtPoint(EquipFx.Ratchet, transform.position, 0.35f);
            if (held) HapticAll(0.015f, 0.35f);
        }
    }

    [FishNet.Object.ServerRpc(RequireOwnership = false)]
    private void ToggleServerRpc(bool open, FishNet.Connection.NetworkConnection sender = null)
    {
        if (Sync == null || !Sync.IsGrabbed || Owner != sender) return;
        _open.Value = open;
    }
}
