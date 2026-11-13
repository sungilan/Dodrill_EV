using FishNet.Object.Synchronizing;
using UnityEngine;

// ============================================================
//  WorkLight.cs
//
//  작업등(헤드램프형 LED 핸드 라이트). 잡고 트리거(PC: F, 모바일: 작업 버튼)를 누를 때마다 켜고 끈다.
//  · 켜짐 상태는 서버를 거쳐 모두에게 같다 → 다른 사람이 비춰 주는 것도 보인다
//  · 앞쪽(로컬 +Z)으로 스포트라이트, 렌즈가 빛난다
//  · 자석 받침(StayOnRelease) — 차체 아래·리프트 기둥 등에 대고 놓으면 그 자리에서 계속 비춘다
// ============================================================
public class WorkLight : EquipmentBase
{
    protected override string MobileActionSub => "누르기";
    protected override string MobileActionName => "작업등 켜기/끄기";

    [Tooltip("렌즈(빛나는 면) 렌더러")] public Renderer lens;
    [Tooltip("빛이 나가는 기준(없으면 자기 자신, 앞쪽 +Z)")] public Transform emitter;
    public float range = 7f;
    public float spotAngle = 55f;
    public float intensity = 6f;

    private readonly SyncVar<bool> _on = new SyncVar<bool>();
    private Light _light;
    private MaterialPropertyBlock _mpb;
    private bool _shown = true;

    private void Start()
    {
        var at = emitter != null ? emitter : transform;
        var go = new GameObject("[WorkLightBeam]");
        go.transform.SetParent(at, false);
        _light = go.AddComponent<Light>();
        _light.type = LightType.Spot;
        _light.range = range;
        _light.spotAngle = spotAngle;
        _light.innerSpotAngle = spotAngle * 0.55f;
        _light.intensity = intensity;
        _light.color = new Color(1f, 0.97f, 0.9f);
        _light.shadows = LightShadows.None;   // 모바일·Quest 부담
        _mpb = new MaterialPropertyBlock();
        Apply(_on.Value);
    }

    protected override void OnPressDown(Autohand.Hand h)
    {
        ToggleServerRpc();
        EquipFx.Haptic(h, 0.03f, 0.4f);
        AudioSource.PlayClipAtPoint(EquipFx.Click, transform.position, 0.6f);
    }

    [FishNet.Object.ServerRpc(RequireOwnership = false)]
    private void ToggleServerRpc(FishNet.Connection.NetworkConnection sender = null)
    {
        if (Sync == null || !Sync.IsGrabbed || Owner != sender) return;
        _on.Value = !_on.Value;
    }

    // 트리거는 '쓰는 중' 상태 대신 토글로만 쓴다
    protected override bool WantUsing(bool input) => false;

    protected override void Update()
    {
        base.Update();
        if (_light != null && _shown != _on.Value) Apply(_on.Value);
    }

    private void Apply(bool on)
    {
        _shown = on;
        if (_light != null) _light.enabled = on;
        if (lens != null)
        {
            lens.GetPropertyBlock(_mpb);
            _mpb.SetColor("_EmissionColor", on ? new Color(1f, 0.95f, 0.85f) * 4f : Color.black);
            _mpb.SetColor("_BaseColor", on ? Color.white : new Color(0.35f, 0.35f, 0.35f));
            lens.SetPropertyBlock(_mpb);
        }
    }
}
