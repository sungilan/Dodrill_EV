using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================
//  SlotBlockerCleanup — (11/20 내부 시연) 사다리를 잡아서 옮겨도 원래 자리에 뭔가 막혀 있는 느낌
//
//  Game 씬 [PlayerCollision] 아래에 예전 고정 소품 자리마다 보이지 않는 박스 콜라이더(Prop_FBX_Ladder 등)가 남아 있다.
//  지금 그 자리에는 잡아서 옮기는 장비(사다리·오일 드레이너 등)가 슬롯(Slot_*)으로 스폰되므로,
//  장비를 옮기면 빈 자리에 투명 벽만 남는다.
//  → 씬 로드 시 [PlayerCollision] 자식 중 렌더러가 없고 같은 자리(수평 0.35m 안)에 바닥 장비 슬롯이 있는 것만 끈다.
//    벽(Wall_*)·캐비닛 등 슬롯이 없는 고정 소품 콜라이더는 그대로 둔다.
// ============================================================
public static class SlotBlockerCleanup
{
    private const float Radius = 0.35f;
    private const float FloorSlotMaxY = 0.05f;   // 지금 씬 기준: 사다리 2, 오일 드레이너 2 만 해당

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        for (int i = 0; i < SceneManager.sceneCount; i++) Clean(SceneManager.GetSceneAt(i));
    }

    private static void OnLoaded(Scene s, LoadSceneMode m) => Clean(s);

    private static void Clean(Scene scene)
    {
        if (!scene.isLoaded) return;
        Transform blockers = null, slots = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (blockers == null) blockers = Find(root.transform, "[PlayerCollision]");
            if (slots == null) slots = Find(root.transform, "[Equipment Slots]");
        }
        if (blockers == null || slots == null) return;

        int off = 0;
        foreach (Transform b in blockers)
        {
            if (!b.gameObject.activeSelf || b.GetComponentInChildren<Renderer>() != null) continue;
            var col = b.GetComponent<Collider>();
            Vector3 c = col != null ? col.bounds.center : b.position;
            foreach (Transform s in slots.GetComponentsInChildren<Transform>(true))
            {
                if (!s.name.StartsWith("Slot_")) continue;
                // 바닥에 놓이는 장비(사다리·오일 드레이너) 자리만 — 선반·카트·서랍 위 공구 슬롯 아래의 가구 콜라이더는 남긴다
                if (s.position.y - slots.position.y > FloorSlotMaxY) continue;
                Vector3 d = s.position - c; d.y = 0f;
                if (d.sqrMagnitude > Radius * Radius) continue;
                b.gameObject.SetActive(false);
                off++;
                Debug.Log($"[SlotBlockerCleanup] 옮길 수 있는 장비 자리의 투명 콜라이더 끔: {b.name} (슬롯 {s.name})");
                break;
            }
        }
        if (off > 0) Debug.Log($"[SlotBlockerCleanup] {scene.name}: {off}개 정리");
    }

    private static Transform Find(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            var f = Find(c, name);
            if (f != null) return f;
        }
        return null;
    }
}
