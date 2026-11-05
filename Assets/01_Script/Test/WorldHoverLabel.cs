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

    private bool _onTopApplied;
    private static Sprite s_roundedSprite;

    /// <summary>둥근 모서리 + 얇은 테두리의 9-슬라이스 스프라이트를 코드로 만든다(프로젝트 스프라이트에 의존하지 않음).</summary>
    private static Sprite RoundedSprite()
    {
        if (s_roundedSprite != null) return s_roundedSprite;
        const int S = 64; const float R = 18f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                // 모서리 원까지의 거리로 알파 계산
                float cx = Mathf.Clamp(x + 0.5f, R, S - R), cy = Mathf.Clamp(y + 0.5f, R, S - R);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float a = Mathf.Clamp01(R - d + 0.5f);
                float edge = Mathf.Clamp01(1f - Mathf.Abs(d - (R - 1.5f)));   // 가장자리 1.5px 링 = 테두리
                byte v = (byte)Mathf.RoundToInt(Mathf.Lerp(255f, 255f, edge));
                // 안쪽은 흰색(색상은 Image.color 로), 테두리는 더 밝게 보이도록 알파만 키운다
                float alpha = a * Mathf.Lerp(0.88f, 1f, edge);
                px[y * S + x] = new Color32(v, v, v, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        s_roundedSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(R, R, R, R));
        return s_roundedSprite;
    }

    /// <summary>호버 라벨과 같은 어두운 둥근 카드 배경을 다른 월드 UI(가이드 설명 등)에도 적용한다.</summary>
    public static void StyleCard(UnityEngine.UI.Image img)
    {
        if (img == null) return;
        img.sprite = RoundedSprite();
        img.type = UnityEngine.UI.Image.Type.Sliced;
        img.color = new Color(0.055f, 0.09f, 0.15f, 0.86f);
        img.raycastTarget = false;
    }

    /// <summary>
    /// 월드 스페이스 UI 가 차체 등 3D 모델에 가려지지 않고 항상 보이게 한다(깊이 테스트 끔).
    /// 후드 래치처럼 차 안쪽에 있는 지점의 안내가 모델 뒤로 숨던 문제.
    /// </summary>
    public static void DrawOnTop(GameObject root)
    {
        if (root == null) return;
        foreach (var g in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
        {
            if (g is TMP_Text t)
            {
                var m = t.fontMaterial;                       // 인스턴스 복사본
                if (m != null && m.HasProperty("unity_GUIZTestMode")) m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            else
            {
                var m = new Material(g.materialForRendering);
                if (m.HasProperty("unity_GUIZTestMode")) m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                g.material = m;
            }
        }
    }

    /// <summary>텍스트 설정. FreeLookController에서 호출.</summary>
    public void SetText(string objName, string actionMsg)
    {
        if (labelText == null) labelText = GetComponentInChildren<TextMeshProUGUI>(true);
        if (labelText == null) return;

        // 내부 ID(Hood_Latch_Point 등)는 표시 이름으로 바꾼다
        string title = HoverNameFormatter.Format(objName);
        bool busy = !string.IsNullOrEmpty(actionMsg) && actionMsg.Contains("사용 중");
        string actionColor = busy ? "#FFB454" : "#8FD3FF";

        labelText.richText = true;
        labelText.color = Color.white;
        labelText.enableAutoSizing = false;
        labelText.fontSize = 20f;
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.textWrappingMode = TextWrappingModes.NoWrap;
        labelText.text = string.IsNullOrEmpty(actionMsg)
            ? $"<b>{title}</b>"
            : $"<b>{title}</b>\n<size=68%><color={actionColor}>{actionMsg}</color></size>";

        // 배경: 어두운 둥근 카드
        var panelImg = background != null ? background : GetComponentInChildren<UnityEngine.UI.Image>(true);
        StyleCard(panelImg);

        if (!_onTopApplied) { _onTopApplied = true; DrawOnTop(gameObject); }

        // 글자 양에 맞춰 카드 크기 조절
        var canvas = GetComponentInChildren<Canvas>(true);
        if (canvas != null)
        {
            Vector2 pref = labelText.GetPreferredValues(labelText.text);
            var crt = (RectTransform)canvas.transform;
            crt.sizeDelta = new Vector2(Mathf.Max(110f, pref.x + 44f), pref.y + 24f);
            var trt = labelText.rectTransform;
            trt.offsetMin = new Vector2(10f, 6f); trt.offsetMax = new Vector2(-10f, -6f);
        }
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
