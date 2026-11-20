using FishNet.Object;
using UnityEngine;

// ============================================================
//  GenericNetworkPlayer.cs — 콘텐츠 없는 범용 PC 플레이어 (모듈화, 2026-11-20)
//
//  프리팹 Avatar/Prefabs/Generic/GenericPlayer.prefab 의 루트에 붙는다.
//    · 내 플레이어(IsOwner): 카메라·오디오 리스너·조작부(SimplePcInteractor)를 켜고, 내 몸은 숨긴다.
//    · 남의 플레이어: 카메라·조작부는 꺼 둔 채 몸(캡슐)만 보인다. 위치는 LocalNetworkTransform 이 전파.
//  FishNet 기본 PlayerSpawner(접속 시 자동 생성)의 Player Prefab 에 이 프리팹을 지정하면 된다.
//  플랫폼별(VR·모바일) 분기가 필요하면 PlayerSpawnner(플랫폼별 프리팹 지정)를 쓴다.
// ============================================================
public class GenericNetworkPlayer : NetworkBehaviour
{
    [SerializeField] private Camera cam;
    [SerializeField] private AudioListener listener;
    [SerializeField] private SimplePcInteractor interactor;
    [Tooltip("내 화면에서는 숨길 몸통 렌더러(1인칭이라 자기 몸이 카메라를 가린다)")]
    [SerializeField] private Renderer[] bodyRenderers;

    public override void OnStartClient()
    {
        base.OnStartClient();
        bool mine = IsOwner;

        if (cam != null)
        {
            cam.enabled = mine;
            if (mine) cam.tag = "MainCamera";
        }
        if (listener != null) listener.enabled = mine;
        if (interactor != null) interactor.enabled = mine;
        foreach (var r in bodyRenderers) if (r != null) r.enabled = !mine;

        name = mine ? "Local Player" : $"Remote Player - {OwnerId}";
    }
}
