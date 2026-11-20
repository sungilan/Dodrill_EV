using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Game 씬이 로드되면 작업장 바닥(y=0 위)에 보이지 않는 두꺼운 박스 콜라이더를 하나 만든다.
/// 놓은 도구·부품이 바닥을 뚫고 끝없이 떨어지는 것을 막는다(서버·모든 클라이언트 공통).
///
/// · 씬 파일을 건드리지 않고 코드로 만든다 — 이미 바닥 Plane 이 있어도 겹쳐 있을 뿐 문제 없다.
/// · 레이어는 'Wall'(12): 기존 바닥 Plane 과 같은 레이어라 충돌 규칙(도구·손·몸 ↔ 바닥)이 기존과 동일하다.
/// · 두께 1m(윗면 y=0): 빠르게 떨어져도 뚫지 않게.
/// </summary>
public static class WorkshopFloorCollider
{
    public const string ObjectName = "[WorkshopFloorCollider]";

    // 작업장 안쪽 범위(PlayerCollisionBuilder 와 같은 측정값) + 여유 2m
    private static readonly Vector3 Center = new Vector3(0.9f, -0.5f, 2.0f);
    private static readonly Vector3 Size = new Vector3(16f, 1f, 20f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        // 이미 Game 씬에서 시작한 경우(에디터 등)
        var active = SceneManager.GetActiveScene();
        if (active.name == "Game") Build(active);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Game") Build(scene);
    }

    private static void Build(Scene scene)
    {
        foreach (var r in scene.GetRootGameObjects())
            if (r.name == ObjectName) return;   // 이미 있음

        int layer = LayerMask.NameToLayer("Wall");
        var go = new GameObject(ObjectName);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.layer = layer >= 0 ? layer : 0;
        go.transform.position = Center;
        var col = go.AddComponent<BoxCollider>();
        col.size = Size;
        Debug.Log($"[WorkshopFloorCollider] 바닥 콜라이더 생성: center={Center} size={Size} layer={LayerMask.LayerToName(go.layer)}");
    }
}
