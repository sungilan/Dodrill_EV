using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// ============================================================
//  ConeStack.cs
//
//  겹쳐 쌓아 둔 꼬깔콘(라바콘) 묶음. 맨 위 한 개는 진짜 장비(Equip_Cone)로 놓여 있어 잡아 갈 수 있고,
//  가져가면(묶음에서 0.35m 이상 멀어지면) 서버가 다음 한 개를 맨 위에 다시 내놓는다 → 묶음에서 하나씩 뽑아 쓰는 느낌.
//  남은 개수만큼 묶음 모양(stackVisuals)을 보여 준다. 작업 구역 표시용.
// ============================================================
public class ConeStack : NetworkBehaviour
{
    [Tooltip("하나씩 내놓을 꼬깔콘 장비")] public NetworkObject conePrefab;
    [Tooltip("묶음 모양 — 아래에서 위 순서. 남은 개수만큼 켜진다")] public GameObject[] stackVisuals;
    [Tooltip("콘 한 개가 겹칠 때 높아지는 높이(m)")] public float stepHeight = 0.045f;

    private readonly SyncVar<int> _remaining = new SyncVar<int>(-1);
    private NetworkObject _top;
    private float _nextCheck;

    public override void OnStartServer()
    {
        base.OnStartServer();
        _remaining.Value = stackVisuals != null ? stackVisuals.Length : 0;
        SpawnTop();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        _remaining.OnChange += OnRemaining;
        Apply(_remaining.Value);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        _remaining.OnChange -= OnRemaining;
    }

    private void OnRemaining(int prev, int next, bool asServer) => Apply(next);

    private void Apply(int n)
    {
        if (stackVisuals == null || n < 0) return;
        for (int i = 0; i < stackVisuals.Length; i++)
            if (stackVisuals[i] != null) stackVisuals[i].SetActive(i < n);
    }

    private Vector3 TopPos => transform.position + Vector3.up * (stepHeight * Mathf.Max(0, _remaining.Value));

    [Server]
    private void SpawnTop()
    {
        if (conePrefab == null) return;
        _top = Instantiate(conePrefab, TopPos, transform.rotation);
        if (gameObject.scene.IsValid() && _top.gameObject.scene != gameObject.scene)
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_top.gameObject, gameObject.scene);
        ServerManager.Spawn(_top);
    }

    private void Update()
    {
        if (!IsServerStarted || Time.time < _nextCheck) return;
        _nextCheck = Time.time + 0.4f;
        bool taken = _top == null || !_top.IsSpawned;
        if (!taken)
        {
            Vector3 d = _top.transform.position - TopPos; d.y = 0f;
            taken = d.magnitude > 0.35f;
        }
        if (!taken || _remaining.Value <= 0) return;
        _remaining.Value--;
        SpawnTop();
    }
}
