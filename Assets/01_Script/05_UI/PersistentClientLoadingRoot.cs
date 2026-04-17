using UnityEngine;

/// <summary>
/// Client 씬에 배치한 <c>Loading_BG</c> 등을 게임 씬까지 끌고 가서 재사용합니다.
/// 이 컴포넌트가 붙은 GameObject만 <see cref="DontDestroyOnLoad"/> 됩니다.
/// </summary>
[DefaultExecutionOrder(-200)]
public class PersistentClientLoadingRoot : MonoBehaviour
{
    public static PersistentClientLoadingRoot Instance { get; private set; }

    [SerializeField] private bool dontDestroyOnLoad = true;

    /// <summary>
    /// <see cref="Instance"/>가 아직 없을 때(비활성 등으로 Awake 순서가 늦을 때) 씬 전체에서 찾아 동기화합니다.
    /// </summary>
    public static PersistentClientLoadingRoot GetInstanceIncludeInactive()
    {
        if (Instance != null)
            return Instance;

        var found = Object.FindFirstObjectByType<PersistentClientLoadingRoot>(FindObjectsInactive.Include);
        if (found == null)
            return null;

        if (Instance != null && Instance != found)
            return Instance;

        Instance = found;
        found.ApplyDontDestroyIfNeeded();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ApplyDontDestroyIfNeeded();
    }

    private void ApplyDontDestroyIfNeeded()
    {
        if (!dontDestroyOnLoad)
            return;
        if (gameObject.scene.name == "DontDestroyOnLoad")
            return;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
