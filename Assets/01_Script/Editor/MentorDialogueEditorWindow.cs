using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

// ============================================================
//  MentorDialogueEditorWindow.cs
//  선임 정비사 대사 편집 — 시나리오 JSON(modules[].config.dialogueLines) ↔ 두드릴 서버(/dialogues)
//  메뉴: Tools / Scenario / 선임 대사 편집
//
//   · JSON 저장   : 고른 모듈의 dialogueLines 만 바꿔 쓴다(나머지 JSON 은 글자 그대로 — JObject 로 해당 배열만 교체)
//   · 서버에서 받기 : GET /dialogues/{scenarioId} 를 받아 같은 id 의 문구·화자를 화면에 덮어쓴다(저장 전에는 JSON 불변)
//   · 서버로 올리기 : PUT /dialogues/{scenarioId} — 두드릴 에디터(UI_DialogueEditor)가 같은 서버를 쓴다. 확인 창을 거친다.
//  id 는 시나리오 안에서 고유한 숫자 문자열(서버 dialogue_id 와 짝). trigger 는 서버로 올라가지 않고 JSON 에만 있다.
// ============================================================
public class MentorDialogueEditorWindow : EditorWindow
{
    private const string ScenariosFolder = "Assets/Resources/Scenarios";

    private string[] _paths = Array.Empty<string>();
    private int _fileIndex;
    private JObject _root;
    private string _loadedPath = "";
    private string[] _moduleIds = Array.Empty<string>();
    private int _modIndex;
    private List<DialogueLineJson> _lines = new List<DialogueLineJson>();
    private Vector2 _scroll;
    private string _status = "";

    [MenuItem("Tools/Scenario/선임 대사 편집")]
    public static void Open() => GetWindow<MentorDialogueEditorWindow>("선임 대사");

    private void OnEnable() { Refresh(); }

    private void Refresh()
    {
        _paths = Directory.Exists(ScenariosFolder)
            ? Directory.GetFiles(ScenariosFolder, "*.json", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();
        if (_paths.Length > 0)
        {
            int ev = Array.FindIndex(_paths, p => Path.GetFileName(p).StartsWith("ev_free"));
            if (_loadedPath == "" && ev >= 0) _fileIndex = ev;
            Load();
        }
    }

    private void Load()
    {
        _fileIndex = Mathf.Clamp(_fileIndex, 0, _paths.Length - 1);
        _loadedPath = _paths[_fileIndex];
        try
        {
            _root = JObject.Parse(File.ReadAllText(_loadedPath, Encoding.UTF8));
            var mods = _root["modules"] as JArray ?? new JArray();
            _moduleIds = mods.Select(m => (string)m["moduleId"] ?? "").ToArray();
            _modIndex = Mathf.Clamp(_modIndex, 0, Mathf.Max(0, _moduleIds.Length - 1));
            Pull();
        }
        catch (Exception e)
        {
            _root = null; _moduleIds = Array.Empty<string>(); _lines.Clear();
            _status = "불러오기 실패: " + e.Message;
        }
    }

    private JToken ConfigOf(int mod) => _root?["modules"]?[mod]?["config"];

    private void Pull()
    {
        _lines = new List<DialogueLineJson>();
        var cfg = ConfigOf(_modIndex);
        var arr = cfg?["dialogueLines"] as JArray;
        if (arr == null) return;
        foreach (var t in arr) _lines.Add(t.ToObject<DialogueLineJson>() ?? new DialogueLineJson());
    }

    private void Push()
    {
        var cfg = ConfigOf(_modIndex) as JObject;
        if (cfg == null) return;
        cfg["dialogueLines"] = JArray.FromObject(_lines);
    }

    private string ScenarioId => _root != null ? (string)_root["scenarioId"] : "";

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "선임 정비사 대사 — trigger 가 비면 그 단계에 들어갈 때 순서대로, 있으면 같은 이름의 사건이 날 때 재생됩니다.\n" +
            "trigger 예: hv_warn · loto_12v · loto_msd · loto_ok · loto_wait · support_need · tool_need · reuse · coolant_open · spill · accident_shock · accident_drop · accident_fire · grab_<프리팹ID>\n" +
            "id 는 시나리오 안의 숫자(서버 dialogue_id 와 짝). 서버에서 받으면 문구·화자만 덮어씁니다.", MessageType.Info);
        if (GUILayout.Button("JSON 목록 새로고침", GUILayout.Width(160))) { _loadedPath = ""; Refresh(); }
        if (_paths.Length == 0) { EditorGUILayout.HelpBox("JSON 이 없습니다: " + ScenariosFolder, MessageType.Warning); return; }

        int nf = EditorGUILayout.Popup("시나리오 JSON", _fileIndex, _paths.Select(Path.GetFileNameWithoutExtension).ToArray());
        if (nf != _fileIndex) { _fileIndex = nf; _modIndex = 0; Load(); }
        if (_root == null) { EditorGUILayout.HelpBox(_status, MessageType.Error); return; }
        if (_moduleIds.Length == 0) { EditorGUILayout.HelpBox("modules 가 비어 있습니다.", MessageType.Warning); return; }

        int nm = EditorGUILayout.Popup("모듈 (moduleId)", _modIndex, _moduleIds);
        if (nm != _modIndex) { Push(); _modIndex = nm; Pull(); }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("JSON 저장", GUILayout.Height(26))) Save();
            if (GUILayout.Button("서버에서 받기", GUILayout.Height(26))) PullFromServer();
            if (GUILayout.Button("서버로 올리기", GUILayout.Height(26))) PushToServer();
        }
        if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.None);

        EditorGUILayout.LabelField($"대사 줄 ({_lines.Count})  —  서버 주소: {MentorDialogue.ServerBase}", EditorStyles.boldLabel);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        for (int i = 0; i < _lines.Count; i++) { if (!DrawLine(i)) break; }
        EditorGUILayout.EndScrollView();
        if (GUILayout.Button("+ 줄 추가", GUILayout.Height(22)))
            _lines.Add(new DialogueLineJson { id = NextId(), speaker = "Mentor", displayDuration = 0f, volume = 1f });
    }

    private bool DrawLine(int i)
    {
        var l = _lines[i];
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"#{i + 1}  id {l.id}  {(string.IsNullOrEmpty(l.trigger) ? "[단계]" : "[" + l.trigger + "]")}", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("↑", GUILayout.Width(26)) && i > 0) { (_lines[i - 1], _lines[i]) = (_lines[i], _lines[i - 1]); EditorGUILayout.EndVertical(); return false; }
            if (GUILayout.Button("↓", GUILayout.Width(26)) && i < _lines.Count - 1) { (_lines[i + 1], _lines[i]) = (_lines[i], _lines[i + 1]); EditorGUILayout.EndVertical(); return false; }
            if (GUILayout.Button("삭제", GUILayout.Width(44))) { _lines.RemoveAt(i); EditorGUILayout.EndVertical(); return false; }
        }
        l.id = EditorGUILayout.TextField("ID (숫자)", l.id ?? "");
        l.speaker = EditorGUILayout.TextField("화자 (Mentor / Trainee)", l.speaker);
        l.trigger = EditorGUILayout.TextField("trigger (비면 단계 대사)", l.trigger ?? "");
        using (new EditorGUILayout.HorizontalScope())
        {
            int ei = Mathf.Max(0, Array.IndexOf(MentorDialogue.Emotions, l.emotion ?? ""));
            var labels = MentorDialogue.Emotions.Select(e => e == "" ? "(자동: trigger 에 맞춤 → " + MentorDialogue.DefaultEmotionFor(l.trigger) + ")" : e).ToArray();
            ei = EditorGUILayout.Popup("표정", ei, labels);
            l.emotion = MentorDialogue.Emotions[ei];
            string shown = string.IsNullOrEmpty(l.emotion) ? MentorDialogue.DefaultEmotionFor(l.trigger) : l.emotion;
            var face = Resources.Load<Sprite>(shown == "neutral" ? "Dialogue/MentorPortrait" : "Dialogue/Mentor_" + shown);
            GUILayout.Label(face != null ? face.texture : (Texture)Texture2D.grayTexture, GUILayout.Width(40), GUILayout.Height(40));
        }
        l.ko = EditorGUILayout.TextField("한국어", l.ko);
        l.en = EditorGUILayout.TextField("English", l.en);
        l.vi = EditorGUILayout.TextField("Tiếng Việt", l.vi);
        l.displayDuration = EditorGUILayout.FloatField("표시 시간(초, 0=자동)", l.displayDuration);
        EditorGUILayout.LabelField("음성 경로 — res:폴더/이름 또는 이름만 = Resources(확장자 제외), sa:파일.wav = StreamingAssets", EditorStyles.miniLabel);
        l.voiceResourcesPath = EditorGUILayout.TextField("음성 기본(한국어)", l.voiceResourcesPath ?? "");
        l.voiceResourcesPathEn = EditorGUILayout.TextField("음성 English", l.voiceResourcesPathEn ?? "");
        l.voiceResourcesPathVi = EditorGUILayout.TextField("음성 Tiếng Việt", l.voiceResourcesPathVi ?? "");
        l.waitForVoiceEnd = EditorGUILayout.ToggleLeft("음성 끝까지 대기", l.waitForVoiceEnd);
        ScenarioVoiceLoader.TryLoadSync(l.voiceResourcesPath, out AudioClip prev);
        var picked = (AudioClip)EditorGUILayout.ObjectField("음성 파일 끌어놓기(기본)", prev, typeof(AudioClip), false);
        if (picked != prev && picked != null)
        {
            string ap = AssetDatabase.GetAssetPath(picked);
            int ri = ap.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);
            if (ri >= 0) l.voiceResourcesPath = "res:" + Path.ChangeExtension(ap.Substring(ri + "/Resources/".Length), null).Replace('\\', '/');
            else Debug.LogWarning("[MentorDialogue] Resources 폴더 안의 음성만 끌어놓을 수 있습니다: " + ap);
        }
        EditorGUILayout.EndVertical();
        return true;
    }

    private string NextId()
    {
        int max = 0;
        if (_root?["modules"] is JArray mods)
            foreach (var m in mods)
                if (m["config"]?["dialogueLines"] is JArray arr)
                    foreach (var t in arr) if (int.TryParse((string)t["id"], out int n)) max = Mathf.Max(max, n);
        foreach (var l in _lines) if (int.TryParse(l.id, out int n2)) max = Mathf.Max(max, n2);
        return (max + 1).ToString();
    }

    private void Save()
    {
        Push();
        try
        {
            string json = _root.ToString(Formatting.Indented);
            File.WriteAllText(_loadedPath, json, new UTF8Encoding(false));
            AssetDatabase.Refresh();
            _status = "저장했습니다: " + Path.GetFileName(_loadedPath);
        }
        catch (Exception e) { _status = "저장 실패: " + e.Message; }
    }

    // ── 서버 ──
    private void PullFromServer()
    {
        string url = $"{MentorDialogue.ServerBase}/dialogues/{UnityWebRequest.EscapeURL(ScenarioId)}";
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = 6;
            var op = req.SendWebRequest();
            while (!op.isDone) { }
            if (req.result != UnityWebRequest.Result.Success) { _status = $"서버에서 받기 실패({req.responseCode}): {req.error}"; return; }
            var payload = JsonUtility.FromJson<ServerScenarioDialoguePayload>(req.downloadHandler.text);
            if (payload?.dialogues == null) { _status = "서버 응답에 dialogues 가 없습니다."; return; }
            int n = 0, add = 0;
            string mod = _moduleIds[_modIndex];
            foreach (var d in payload.dialogues)
            {
                if (d.module_id != mod) continue;
                var l = _lines.FirstOrDefault(x => x.id == d.dialogue_id.ToString());
                if (l == null) { _lines.Add(new DialogueLineJson { id = d.dialogue_id.ToString(), speaker = d.speaker, ko = d.ko, en = d.en, vi = d.vi }); add++; continue; }
                if (!string.IsNullOrWhiteSpace(d.speaker)) l.speaker = d.speaker;
                if (!string.IsNullOrWhiteSpace(d.ko)) l.ko = d.ko;
                if (!string.IsNullOrWhiteSpace(d.en)) l.en = d.en;
                if (!string.IsNullOrWhiteSpace(d.vi)) l.vi = d.vi;
                n++;
            }
            _status = $"서버에서 받음 — 덮어쓴 줄 {n}, 새 줄 {add} (JSON 저장 전까지 파일은 그대로)";
        }
    }

    private void PushToServer()
    {
        Push();
        if (!EditorUtility.DisplayDialog("서버로 올리기",
                $"'{ScenarioId}' 의 대사를 서버 {MentorDialogue.ServerBase} 에 PUT 합니다.\n두드릴 에디터가 같은 서버를 쓰며, 같은 시나리오의 기존 대사는 덮어쓰기 됩니다.\n계속할까요?", "올리기", "취소")) return;

        var all = new List<ServerDialogueLinePayload>();
        if (_root["modules"] is JArray mods)
            foreach (var m in mods)
            {
                string mid = (string)m["moduleId"];
                if (!(m["config"]?["dialogueLines"] is JArray arr)) continue;
                foreach (var t in arr)
                {
                    if (!int.TryParse((string)t["id"], out int did)) continue;
                    all.Add(new ServerDialogueLinePayload { module_id = mid, dialogue_id = did, speaker = (string)t["speaker"], ko = (string)t["ko"], en = (string)t["en"], vi = (string)t["vi"] });
                }
            }
        var body = new ServerScenarioDialoguePayload { scenario_id = ScenarioId, scenario_name_ko = (string)_root["scenarioName"]?["ko"], dialogues = all };
        string json = JsonConvert.SerializeObject(body, Formatting.Indented);   // 한글은 이스케이프하지 않는다(따옴표 등은 올바르게 이스케이프)
        using (var req = new UnityWebRequest($"{MentorDialogue.ServerBase}/dialogues/{UnityWebRequest.EscapeURL(ScenarioId)}", "PUT"))
        {
            req.uploadHandler = new UploadHandlerRaw(new UTF8Encoding(false).GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            req.timeout = 8;
            var op = req.SendWebRequest();
            while (!op.isDone) { }
            _status = req.result == UnityWebRequest.Result.Success ? $"서버에 올렸습니다 ({all.Count}줄)" : $"올리기 실패({req.responseCode}): {req.error}";
        }
    }
}
