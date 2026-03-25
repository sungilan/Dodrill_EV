using UnityEngine;
using TMPro;

// ============================================================
//  WorldHoverLabel.cs
//  FreeLookController가 호버 시 오브젝트 위에 스폰하는 라벨.
//
//  프리팹 구성:
//    WorldHoverLabel (이 스크립트)
//      └── Canvas (World Space, Scale 0.002)
//            └── Panel (선택)
//                  └── LabelText (TextMeshPro)
//
//  자동 생성:
//    이 스크립트가 붙은 오브젝트를 씬에 배치 후
//    Hierarchy에서 우클릭 → "Create WorldHoverLabel Prefab" 버튼 사용
//    (없으면 아래 수동 생성 참고)
//
//  수동 프리팹 생성:
//    1. 빈 GameObject "WorldHoverLabel" 생성
//    2. Canvas 컴포넌트 추가
//       - Render Mode: World Space
//       - Width: 200, Height: 80
//       - Scale: 0.002, 0.002, 0.002
//    3. Canvas 자식에 TextMeshPro - Text 추가
//       - Alignment: Center / Middle
//       - Font Size: 28
//    4. 이 스크립트(WorldHoverLabel) 루트에 부착
//    5. labelText 필드에 TMP 연결
//    6. Prefab으로 저장
//    7. FreeLookController.worldLabelPrefab 에 할당
// ============================================================
public class WorldHoverLabel : MonoBehaviour
{
    [Header("참조")]
    public TextMeshProUGUI labelText;

    [Header("설정")]
    [Tooltip("카메라를 향해 자동 회전 (Billboard)")]
    public bool billboard = true;

    [Tooltip("라벨 배경 이미지 (없으면 생략)")]
    public UnityEngine.UI.Image background;

    [Tooltip("배경 색상")]
    public Color bgColor = new Color(0f, 0f, 0f, 0.6f);

    private Camera _cam;

    private void Awake()
    {
        _cam = Camera.main;
        if (background != null)
            background.color = bgColor;
    }

    private void LateUpdate()
    {
        if (!billboard) return;

        // 카메라 없으면 매 프레임 탐색
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        // 카메라 방향으로 정면 고정 (Billboard)
        transform.forward = _cam.transform.forward;
    }

    /// <summary>텍스트 설정. FreeLookController에서 호출.</summary>
    public void SetText(string objName, string actionMsg)
    {
        if (labelText == null) return;
        labelText.text = $"<b>{objName}</b>\n<size=80%><color=#AAFFAA>{actionMsg}</color></size>";
    }

#if UNITY_EDITOR
    // 에디터에서 프리팹 자동 생성
    [UnityEditor.MenuItem("GameObject/EV Training/Create WorldHoverLabel Prefab", false, 10)]
    private static void CreatePrefab()
    {
        // 루트 오브젝트
        var root = new GameObject("WorldHoverLabel");
        root.AddComponent<WorldHoverLabel>();

        // Canvas (World Space)
        var canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(root.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = canvasGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(200, 60);
        canvasGo.transform.localScale = Vector3.one * 0.005f;

        // 배경 Panel
        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        var panelRt = panelGo.AddComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;
        var img = panelGo.AddComponent<UnityEngine.UI.Image>();
        img.color = new Color(0f, 0f, 0f, 0.6f);

        // TMP Text
        var textGo = new GameObject("LabelText");
        textGo.transform.SetParent(panelGo.transform, false);
        var textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(4, 4);
        textRt.offsetMax = new Vector2(-4, -4);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 24;
        tmp.color = Color.white;
        tmp.text = "Label";

        // WorldHoverLabel 필드 연결
        var label = root.GetComponent<WorldHoverLabel>();
        label.background = img;

        // TextMeshPro는 UGUI 버전이므로 WorldHoverLabel.labelText는
        // TextMeshPro (3D) 버전을 쓰도록 별도 처리
        // → labelText 필드를 null로 두고 GetComponentInChildren으로 찾도록 수정

        // 씬에 배치
        UnityEditor.Selection.activeGameObject = root;

        // Prefab 저장 경로
        string path = "Assets/03_3D/WorldHoverLabel.prefab";
        bool exists = System.IO.File.Exists(path);
        if (!exists)
        {
            var prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[WorldHoverLabel] 프리팹 생성: {path}");
            Object.DestroyImmediate(root);
            UnityEditor.Selection.activeObject = prefab;
        }
        else
        {
            Debug.Log($"[WorldHoverLabel] 이미 존재: {path}");
            Object.DestroyImmediate(root);
        }
    }
#endif
}
