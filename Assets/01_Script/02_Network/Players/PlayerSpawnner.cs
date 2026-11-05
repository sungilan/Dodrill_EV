using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using System.Collections;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;
using XRAirpotrSecurity;
using AYellowpaper.SerializedCollections;

public class PlayerSpawnner : NetworkBehaviour
{
    [SerializeField]
    private SerializedDictionary<PlatformType, GameObject> playerObjects = new SerializedDictionary<PlatformType, GameObject>();
    [Header("Stable Prefab Mapping (권장)")]
    [SerializeField] private GameObject vrPlayerPrefab;
    [SerializeField] private GameObject pcPlayerPrefab;
    [SerializeField] private GameObject mobilePlayerPrefab;
    [Header("스폰 재시도")]
    [Tooltip("요청 후 이 시간(초) 안에 내 캐릭터가 생기지 않으면 다시 요청한다.")]
    [SerializeField] private float respawnRetryDelay = 4f;
    [SerializeField] private int maxSpawnAttempts = 3;

    // 서버가 이 스포너 소유자의 플레이어를 이미 만들었는지 (재접속·씬 재로딩·이벤트 중복으로 인한 이중 생성 방지)
    private readonly SyncVar<bool> _playerSpawned = new(
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers));
    private NetworkObject _spawnedPlayer;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if(IsOwner)
        {
            name = "Local Player";
            StartCoroutine(RequestSpawnWithRetry());
        }
        else
        {
            // 예전에는 static 카운터(++playerCount)라 씬을 다시 열수록 번호가 계속 늘었다
            name = $"Remote Player - {OwnerId}";
        }
    }

    private IEnumerator RequestSpawnWithRetry()
    {
        // 플랫폼은 서버가 아닌 클라이언트가 판별하므로 XR 초기화가 끝난 뒤(최대 3초)에 읽는다
        float wait = 0f;
        while(wait < 3f && Utils.GetPlatformType() != PlatformType.VR && IsXrInitializing())
        {
            wait += 0.25f;
            yield return new WaitForSeconds(0.25f);
        }

        PlatformType playerType = Utils.GetPlatformType();
        for(int attempt = 1; attempt <= Mathf.Max(1, maxSpawnAttempts); attempt++)
        {
            Debug.Log($"[PlayerSpawnner] 플레이어 스폰 요청 {attempt}/{maxSpawnAttempts} (platform={playerType})");
            SpawnPlayerReq(playerType);

            float t = 0f;
            while(t < respawnRetryDelay)
            {
                if(_playerSpawned.Value) yield break;
                t += 0.25f;
                yield return new WaitForSeconds(0.25f);
            }
            if(_playerSpawned.Value) yield break;
        }
        Debug.LogError($"[PlayerSpawnner] 플레이어 스폰 응답이 없습니다({maxSpawnAttempts}회 시도). 서버 로그(프리팹 누락 여부)를 확인하세요.");
    }

    private static bool IsXrInitializing()
    {
#if ENABLE_VR || ENABLE_XR_MODULE
        var s = UnityEngine.XR.Management.XRGeneralSettings.Instance;
        return s != null && s.Manager != null && !s.Manager.isInitializationComplete;
#else
        return false;
#endif
    }

    // 소유자만 호출 가능. 예전에는 클라이언트가 보낸 NetworkConnection 을 그대로 소유자로 썼기 때문에
    // 다른 사람 명의로 스폰할 수 있었다 → 서버가 아는 이 오브젝트의 소유자(Owner)를 사용.
    [ServerRpc(RequireOwnership = true)]
    private void SpawnPlayerReq(PlatformType type)
    {
        NetworkConnection connection = base.Owner;
        if(connection == null || !connection.IsValid)
        {
            Debug.LogWarning("[PlayerSpawnner] 소유자 연결이 유효하지 않아 스폰을 무시합니다.");
            return;
        }

        // 이미 만들어졌으면(재요청·중복 이벤트) 무시
        if(_spawnedPlayer != null && _spawnedPlayer.IsSpawned)
        {
            _playerSpawned.Value = true;
            return;
        }

        if(!System.Enum.IsDefined(typeof(PlatformType), type))
        {
            Debug.LogWarning($"[PlayerSpawnner] 알 수 없는 platform type: {type}");
            return;
        }

        GameObject prefab = ResolvePlayerPrefab(type);
        if(prefab == null)
        {
            Debug.LogError($"No player object for platform type: {type}");
            return;
        }

        // 모두 같은 자리에 생성돼 서로의 머리·몸 속에서 시작하던 문제 → 먼저 들어온 사람 수에 따라 옆으로 비켜 세운다
        Vector3 spawnPos = transform.position + transform.rotation * SpawnOffset(CountSpawnedOthers());
        GameObject playerInstance = Instantiate(prefab, spawnPos, transform.rotation);

        // 구 PC/모바일 프리팹은 루트가 곧 카메라라 0.7m 띄워 생성. 아바타 리그 프리팹은 루트가 바닥이고 눈높이는 CameraPivot이 담당.
        if(type != PlatformType.VR && playerInstance.GetComponent<PcMobileAvatarController>() == null)
        {
            playerInstance.transform.position += Vector3.up * 0.7f;
        }

        playerInstance.name = $"{type} Player - {connection.ClientId}";

        NetworkObject netObj = playerInstance.GetComponent<NetworkObject>();
        NetworkObject parentNetObj = GetComponent<NetworkObject>();

        // ✅ Spawn 먼저, 그 다음 SetParent
        Spawn(playerInstance, connection);
        netObj.SetParent(parentNetObj); // Spawn 후에 SetParent

        _spawnedPlayer = netObj;
        _playerSpawned.Value = true;
    }

    // 스폰 위치 기준 좌우·뒤로 늘어서는 자리(스포너 로컬 좌표, m). 첫 사람은 원래 자리.
    private static readonly Vector3[] SpawnSlots =
    {
        new Vector3(0f, 0f, 0f),
        new Vector3(0.9f, 0f, 0f),
        new Vector3(-0.9f, 0f, 0f),
        new Vector3(0f, 0f, -0.9f),
        new Vector3(0.9f, 0f, -0.9f),
        new Vector3(-0.9f, 0f, -0.9f),
        new Vector3(1.8f, 0f, 0f),
        new Vector3(-1.8f, 0f, 0f),
    };

    private static Vector3 SpawnOffset(int index)
    {
        return SpawnSlots[Mathf.Abs(index) % SpawnSlots.Length];
    }

    /// <summary>이 스포너 말고, 이미 플레이어를 만들어 둔 스포너 수(서버에서만 의미 있음).</summary>
    private int CountSpawnedOthers()
    {
        int n = 0;
        foreach(var s in FindObjectsByType<PlayerSpawnner>(FindObjectsSortMode.None))
        {
            if(s == this) continue;
            if(s._spawnedPlayer != null && s._spawnedPlayer.IsSpawned) n++;
        }
        return n;
    }

    private GameObject ResolvePlayerPrefab(PlatformType type)
    {
        // Dictionary 인스펙터가 흔들릴 때를 대비해 고정 필드를 우선 사용.
        switch(type)
        {
            case PlatformType.VR:
                if(vrPlayerPrefab != null) return vrPlayerPrefab;
                break;
            case PlatformType.PC:
                if(pcPlayerPrefab != null) return pcPlayerPrefab;
                break;
            case PlatformType.Mobile:
                if(mobilePlayerPrefab != null) return mobilePlayerPrefab;
                break;
        }

        if(playerObjects != null && playerObjects.TryGetValue(type, out var mapped) && mapped != null)
            return mapped;

        return null;
    }

    private void OnValidate()
    {
        // 기존 Dictionary 값이 있으면 고정 필드로 한번 채워두고,
        // 이후에는 고정 필드가 있으면 Dictionary도 동기화해 혼선 방지.
        if(playerObjects != null)
        {
            if(vrPlayerPrefab == null && playerObjects.TryGetValue(PlatformType.VR, out var vr) && vr != null)
                vrPlayerPrefab = vr;
            if(pcPlayerPrefab == null && playerObjects.TryGetValue(PlatformType.PC, out var pc) && pc != null)
                pcPlayerPrefab = pc;
            if(mobilePlayerPrefab == null && playerObjects.TryGetValue(PlatformType.Mobile, out var mobile) && mobile != null)
                mobilePlayerPrefab = mobile;

            if(vrPlayerPrefab != null) playerObjects[PlatformType.VR] = vrPlayerPrefab;
            if(pcPlayerPrefab != null) playerObjects[PlatformType.PC] = pcPlayerPrefab;
            if(mobilePlayerPrefab != null) playerObjects[PlatformType.Mobile] = mobilePlayerPrefab;
        }
    }
}