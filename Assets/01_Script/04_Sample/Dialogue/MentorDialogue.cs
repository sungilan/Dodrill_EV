using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public enum DialogueSpeaker { Mentor, Trainee }

// ============================================================
//  MentorDialogue.cs — 튜토리얼 '선임 정비사' 대화
//
//  간호술기 프로젝트의 대사 시스템(시나리오 JSON dialogueLines + 두드릴 서버 /dialogues 덮어쓰기)을 EV 에 맞게 옮긴 것.
//   · 단계 대사  : trigger 가 빈 줄 — 시나리오 단계(moduleId)에 들어가면 순서대로 재생
//   · 코칭 대사  : trigger 가 있는 줄 — MentorDialogue.Say("hv_warn") 처럼 사건이 날 때 재생(쿨다운·중복 방지)
//   · 서버 덮어쓰기: GET {서버}/dialogues/{scenarioId} 를 받아 같은 id 의 문구·화자를 바꾼다(실패하면 JSON 그대로)
//   · 화면       : PC·모바일 = 하단 자막(이름표·초상 자리·건너뛰기), VR = 눈앞 월드 패널
//  대화는 단계를 막지 않는다(자막으로 코칭만 한다). 평가 모드(_eval)에서는 나오지 않는다.
//  서버가 없는 빌드(batchmode)에서는 아무것도 하지 않는다.
// ============================================================
public class MentorDialogue : MonoBehaviour
{
    // ── 표정 ──
    //  Assets/Resources/Dialogue/Mentor_<표정>.png (표정: praise serious warn alarm grave sigh) 를 넣으면 대사에 맞춰 초상이 바뀐다.
    //  기본(neutral)은 MentorPortrait.png. 해당 표정 그림이 없으면 기본 그림을 쓴다. JSON 줄의 "emotion" 으로 지정하고, 비면 trigger 로 정한다.
    public static readonly string[] Emotions = { "", "neutral", "praise", "serious", "warn", "alarm", "grave", "sigh" };

    public static string DefaultEmotionFor(string trigger)
    {
        if (string.IsNullOrEmpty(trigger)) return "neutral";
        switch (trigger)
        {
            case "loto_12v": case "loto_ok": case "grab_InsulatedGloves": return "praise";
            case "hv_warn": case "support_need": return "warn";
            case "accident_shock": case "accident_drop": return "alarm";
            case "accident_fire": case "spill": return "grave";
            case "reuse": return "sigh";
            case "tool_need": case "loto_msd": case "loto_wait": case "coolant_open": return "serious";
        }
        return trigger.StartsWith("grab_") ? "serious" : "neutral";
    }

    private static readonly System.Collections.Generic.Dictionary<string, Sprite> s_emoSprites = new System.Collections.Generic.Dictionary<string, Sprite>();

    private static Sprite EmotionSprite(string emotion)
    {
        if (string.IsNullOrEmpty(emotion)) emotion = "neutral";
        if (s_emoSprites.TryGetValue(emotion, out var cached)) return cached;
        Sprite sp = emotion == "neutral" ? null : Resources.Load<Sprite>("Dialogue/Mentor_" + emotion);
        if (sp == null) sp = Resources.Load<Sprite>("Dialogue/MentorPortrait");   // 표정 그림이 없으면 기본 그림
        s_emoSprites[emotion] = sp;
        return sp;
    }

    public const string MentorName = "선임 정비사";
    public const string TraineeName = "나";
    public static string ServerBase = "http://remoshot.i234.me:9143";

    // ── 대화창 모양(코드로 만든다 — 프리팹 없음) ──
    //  · 배경 그림 : Assets/Resources/Dialogue/player_dialogue_box.png (간호술기의 player_dialogue_box, 9-slice). 같은 이름으로 바꿔 넣으면 된다. 없으면 단색 상자.
    //  · 초상      : Assets/Resources/Dialogue/MentorPortrait (스프라이트). 없으면 색 원 + "선임"
    //  · 아래 값으로 크기·위치·색을 조정한다.
    public const string BoxSpriteResource = "Dialogue/player_dialogue_box";
    public static Vector2 PanelSize = new Vector2(1180f, 190f);     // 화면 하단 자막 상자 크기(기준 해상도 1920×1080)
    public static float PanelBottom = 120f;                          // 화면 아래에서 띄우는 거리
    public static Color PanelTint = new Color(0.07f, 0.09f, 0.13f, 0.92f);   // 배경 그림에 곱하는 색(그림이 밝은 회색이라 어둡게 칠해 흰 글자가 읽히게). 흰색으로 두면 그림 그대로
    public static float PanelCornerScale = 2.5f;                     // 9-slice 모서리 크기를 줄이는 배율(클수록 모서리 작음)

    private static MentorDialogue s_inst;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isBatchMode || s_inst != null || FindAnyObjectByType<MentorDialogue>() != null) return;
        var go = new GameObject("[MentorDialogue]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<MentorDialogue>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { s_inst = null; s_emoSprites.Clear(); }

    // ── 시나리오 ──
    private string _scenarioId;
    private ScenarioData _local;
    private string _lastKey;
    private string _curModule;

    private class Item { public DialogueLineJson line; public bool coach; }
    private readonly Queue<Item> _queue = new Queue<Item>();
    private readonly Dictionary<string, float> _lastSaid = new Dictionary<string, float>();
    private Coroutine _play;
    private bool _skip;

    private void Awake() { s_inst = this; }
    private void OnEnable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated += OnTask;
        ScenarioStateReceiver.OnSnapshotReceived += OnSnapshot;
    }
    private void OnDisable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated -= OnTask;
        ScenarioStateReceiver.OnSnapshotReceived -= OnSnapshot;
    }

    private void OnTask(TaskStateBroadcast b) { Enter(b.currentTask); }
    private void OnSnapshot(ScenarioSnapshotBroadcast s) { Enter(s.currentTask); }

    private void Enter(TaskState t)
    {
        var rec = ScenarioStateReceiver.Instance;
        var sc = rec != null ? rec.CurrentScenario : null;
        var tasks = sc?.scenario?.tasks;
        if (sc == null || tasks == null || t.taskIndex < 0 || t.taskIndex >= tasks.Count) return;
        if (t.status != TaskStatus.Running) return;
        string key = sc.scenarioId + "#" + t.taskIndex;
        if (key == _lastKey) return;
        _lastKey = key;
        EnsureScenario(sc);
        _curModule = tasks[t.taskIndex].moduleId;
        if (EvaluationMode.IsEvaluation(sc.scenarioId)) return;   // 평가 중에는 도움말을 주지 않는다
        PlayModule(_curModule);
    }

    private void EnsureScenario(ScenarioData sc)
    {
        if (_scenarioId == sc.scenarioId) return;
        _scenarioId = sc.scenarioId;
        _lastSaid.Clear(); _queue.Clear();
        // FishNet 으로 온 시나리오에는 dialogueLines 가 비어 있다 — 로컬 JSON(Resources/Scenarios)에서 읽는다
        var loaded = ScenarioLoader.Load(sc.scenarioId);
        _local = loaded ?? sc;
        StartCoroutine(FetchServer(sc.scenarioId));
    }

    // ── 서버 덮어쓰기 ──
    private IEnumerator FetchServer(string scenarioId)
    {
        using (var req = UnityWebRequest.Get($"{ServerBase}/dialogues/{UnityWebRequest.EscapeURL(scenarioId)}"))
        {
            req.timeout = 4;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { Debug.Log($"[MentorDialogue] 서버 대사 없음({req.responseCode}) — JSON 대사를 씁니다"); yield break; }
            if (_scenarioId != scenarioId || _local == null) yield break;
            ServerScenarioDialoguePayload payload = null;
            try { payload = JsonUtility.FromJson<ServerScenarioDialoguePayload>(req.downloadHandler.text); }
            catch (Exception e) { Debug.LogWarning("[MentorDialogue] 서버 대사 파싱 실패: " + e.Message); }
            if (payload != null && ScenarioDialogueServerMerge.ApplyServerDialogueOverrides(_local, payload))
                Debug.Log($"[MentorDialogue] 서버 대사 적용: {scenarioId} ({payload.dialogues.Count}줄)");
        }
    }

    // ── 재생 요청 ──
    private List<DialogueLineJson> LinesOf(string moduleId)
    {
        var cfg = _local != null ? _local.GetModuleConfig(moduleId) : null;
        return cfg != null ? cfg.dialogueLines : null;
    }

    private void PlayModule(string moduleId)
    {
        var lines = LinesOf(moduleId);
        if (lines == null) return;
        foreach (var l in lines)
            if (l != null && string.IsNullOrEmpty(l.trigger) && !string.IsNullOrWhiteSpace(l.ko + l.en + l.vi))
                _queue.Enqueue(new Item { line = l, coach = false });
        Kick();
    }

    /// <summary>사건이 났을 때 부른다 — 현재 시나리오에 같은 trigger 의 대사가 있으면 자막으로 말한다. cooldown 초 안에는 다시 말하지 않는다.</summary>
    public static void Say(string trigger, float cooldown = 90f)
    {
        if (s_inst != null) s_inst.SayInternal(trigger, cooldown);
    }

    private void SayInternal(string trigger, float cooldown)
    {
        if (string.IsNullOrEmpty(trigger) || _local == null || EvaluationMode.IsEvaluation(_scenarioId)) return;
        if (_lastSaid.TryGetValue(trigger, out float t) && Time.unscaledTime - t < cooldown) return;
        var found = new List<DialogueLineJson>();
        CollectTrigger(LinesOf(_curModule), trigger, found);
        if (found.Count == 0 && _local.modules != null)
            foreach (var m in _local.modules) CollectTrigger(m?.config?.dialogueLines, trigger, found);
        if (found.Count == 0) return;
        int pendingCoach = 0; foreach (var q in _queue) if (q.coach) pendingCoach++;
        if (pendingCoach >= 2) return;   // 코칭이 밀려 있으면 새 코칭은 버린다(오래된 말이 뒤늦게 나오지 않게)
        _lastSaid[trigger] = Time.unscaledTime;
        foreach (var l in found) _queue.Enqueue(new Item { line = l, coach = true });
        Kick();
    }

    private static void CollectTrigger(List<DialogueLineJson> src, string trigger, List<DialogueLineJson> dst)
    {
        if (src == null) return;
        foreach (var l in src)
            if (l != null && string.Equals(l.trigger, trigger, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(l.ko + l.en + l.vi)) dst.Add(l);
    }

    private void Kick()
    {
        if (_play == null && _queue.Count > 0) _play = StartCoroutine(Run());
    }

    // ── 재생 ──
    private IEnumerator Run()
    {
        EnsureView();
        while (_queue.Count > 0)
        {
            var it = _queue.Dequeue();
            var sp = ParseSpeaker(it.line.speaker);
            string text = it.line.GetLocalizedText();
            _skip = false;
            // 음성: 간호술기와 같은 방식 — res:/접두사 없음 = Resources, sa: = StreamingAssets(비동기)
            AudioClip clip = null;
            string vpath = it.line.GetLocalizedVoicePath();
            if (!string.IsNullOrEmpty(vpath)) yield return ScenarioVoiceLoader.LoadClipCoroutine(vpath, c => clip = c);
            string emo = !string.IsNullOrEmpty(it.line.emotion) ? it.line.emotion : DefaultEmotionFor(it.line.trigger);
            ShowLine(sp, text, emo);
            PlayVoice(clip, it.line.volume);

            // 글자가 한 자씩 나온다
            // 전체 문장으로 크기를 먼저 맞추고(글자가 늘 때마다 크기가 흔들리지 않게) 보이는 글자 수만 늘린다
            SetBody(text);
            for (int i = 1; i <= text.Length && !_skip; i++)
            {
                _body.maxVisibleCharacters = i;
                yield return new WaitForSecondsRealtime(0.025f);
            }
            _body.maxVisibleCharacters = 99999;
            float hold = it.line.displayDuration > 0f ? it.line.displayDuration : Mathf.Clamp(1.6f + text.Length * 0.07f, 2.5f, 9f);
            if (clip != null) hold = Mathf.Max(hold, clip.length);
            float t = 0f;
            while (t < hold && !_skip) { t += Time.unscaledDeltaTime; yield return null; }
            if (clip != null && it.line.waitForVoiceEnd && _voice != null)
                while (_voice.isPlaying && !_skip) yield return null;
            StopVoice();
            yield return new WaitForSecondsRealtime(0.15f);
        }
        HideView();
        _play = null;
    }

    private AudioSource _voice;

    private void PlayVoice(AudioClip clip, float volume)
    {
        if (_voice == null) { _voice = gameObject.AddComponent<AudioSource>(); _voice.playOnAwake = false; _voice.spatialBlend = 0f; }
        _voice.Stop();
        if (clip == null) return;
        _voice.clip = clip;
        _voice.volume = volume > 0f ? Mathf.Clamp01(volume) : 1f;
        _voice.Play();
    }

    private void StopVoice() { if (_voice != null) { _voice.Stop(); _voice.clip = null; } }

    // ── GuideSystem 의 가운데 '단계 설명' 창·물음표 버튼 대신 선임 정비사가 말한다 ──
    /// <summary>처리했으면 true(큰 안내창을 띄우지 않는다). 평가 모드이거나 대화 시스템이 없으면 false.</summary>
    public static bool TrySayStep(string moduleId, string body, string sub, bool force)
    {
        var m = s_inst;
        if (m == null) return false;
        var rec = ScenarioStateReceiver.Instance;
        var sc = rec != null ? rec.CurrentScenario : null;
        if (sc != null && EvaluationMode.IsEvaluation(sc.scenarioId)) return false;
        string text = string.IsNullOrWhiteSpace(sub) ? body : (string.IsNullOrWhiteSpace(body) ? sub : body + " " + sub);
        if (string.IsNullOrWhiteSpace(text)) return true;
        return m.SayStepInternal(sc, moduleId, text.Trim(), force);
    }

    private bool SayStepInternal(ScenarioData sc, string moduleId, string text, bool force)
    {
        if (sc != null) EnsureScenario(sc);
        if (!force)
        {
            // 자동 안내: 그 단계에 단계 대사(JSON)가 있으면 그것이 안내를 대신한다
            var lines = LinesOf(moduleId);
            if (lines != null && lines.Exists(l => l != null && string.IsNullOrEmpty(l.trigger))) return true;
            string key = "step#" + _scenarioId + "#" + moduleId;
            if (_lastSaid.TryGetValue(key, out float at) && Time.unscaledTime - at < 30f) return true;
            _lastSaid[key] = Time.unscaledTime;
        }
        else { _queue.Clear(); _skip = true; }   // 사용자가 직접 물었다(물음표·전구 버튼) — 하던 말을 끊고 바로 답한다
        _queue.Enqueue(new Item { line = new DialogueLineJson { speaker = "Mentor", ko = text, en = text, vi = text }, coach = false });
        Kick();
        return true;
    }

    private static DialogueSpeaker ParseSpeaker(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DialogueSpeaker.Mentor;
        string r = raw.Trim().ToLowerInvariant();
        return r == "trainee" || r == "me" || r == "나" || r == "player" ? DialogueSpeaker.Trainee : DialogueSpeaker.Mentor;
    }

    private void Update()
    {
        if (_play != null && Input.GetKeyDown(KeyCode.Return)) _skip = true;
        if (_root != null && _vr) FollowHead();
    }

    // ══════════════════════════════════════════
    //  화면 — 런타임에 만든다(프리팹 없음)
    // ══════════════════════════════════════════
    private GameObject _root;
    private CanvasGroup _group;
    private TMP_Text _name, _body, _portraitText;
    private Image _portrait, _bar;
    private bool _vr;
    private Coroutine _fade;

    private void EnsureView()
    {
        if (_root != null) return;
        _vr = XRAirpotrSecurity.Utils.GetPlatformType() == XRAirpotrSecurity.PlatformType.VR;
        _root = new GameObject("[MentorDialogue_Canvas]");
        DontDestroyOnLoad(_root);
        var cv = _root.AddComponent<Canvas>();
        float w = PanelSize.x, h = PanelSize.y;
        if (_vr)
        {
            cv.renderMode = RenderMode.WorldSpace;
            var rt0 = cv.GetComponent<RectTransform>(); rt0.sizeDelta = new Vector2(w, h); rt0.localScale = Vector3.one * 0.0011f;
        }
        else
        {
            cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 65;
            var sc = _root.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920f, 1080f); sc.matchWidthOrHeight = 0.5f;
        }
        _root.AddComponent<GraphicRaycaster>();
        _group = _root.AddComponent<CanvasGroup>();

        var panel = NewRect("Panel", _root.transform);
        if (_vr) { panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one; panel.offsetMin = panel.offsetMax = Vector2.zero; }
        else
        {
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f); panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = new Vector2(0f, PanelBottom); panel.sizeDelta = new Vector2(w, h);
        }
        var bg = panel.gameObject.AddComponent<Image>(); bg.color = new Color(0.06f, 0.08f, 0.11f, 0.9f); bg.raycastTarget = false;
        var boxSprite = Resources.Load<Sprite>(BoxSpriteResource);
        if (boxSprite != null)
        {
            bg.sprite = boxSprite; bg.type = Image.Type.Sliced; bg.color = PanelTint;
            bg.pixelsPerUnitMultiplier = PanelCornerScale;
        }

        var bar = NewRect("Bar", panel); bar.anchorMin = Vector2.zero; bar.anchorMax = new Vector2(0f, 1f); bar.pivot = new Vector2(0f, 0.5f);
        if (boxSprite != null) { bar.anchoredPosition = new Vector2(16f, 0f); bar.sizeDelta = new Vector2(7f, -44f); }   // 둥근 모서리 안쪽에 맞춤
        else bar.sizeDelta = new Vector2(8f, 0f);
        _bar = bar.gameObject.AddComponent<Image>(); _bar.raycastTarget = false;

        // 초상 자리 — 나중에 Resources/Dialogue/MentorPortrait 스프라이트가 있으면 그것으로 바꾼다
        var pr = NewRect("Portrait", panel); pr.anchorMin = pr.anchorMax = new Vector2(0f, 0.5f); pr.pivot = new Vector2(0f, 0.5f); pr.anchoredPosition = new Vector2(26f, 0f); pr.sizeDelta = new Vector2(124f, 124f);
        _portrait = pr.gameObject.AddComponent<Image>(); _portrait.raycastTarget = false;
        var spr = Resources.Load<Sprite>("Dialogue/MentorPortrait");
        if (spr != null) { _portrait.sprite = spr; _portrait.color = Color.white; }
        _portraitText = NewText("PortraitText", pr, 46f, TextAlignmentOptions.Center); Stretch(_portraitText.rectTransform);

        _name = NewText("Name", panel, 28f, TextAlignmentOptions.Left); Place(_name.rectTransform, new Vector2(176f, -14f), new Vector2(w - 360f, 40f));
        _name.fontStyle = FontStyles.Bold;
        _body = NewText("Body", panel, 31f, TextAlignmentOptions.TopLeft); Place(_body.rectTransform, new Vector2(176f, -58f), new Vector2(w - 220f, h - 70f));
        _body.textWrappingMode = TextWrappingModes.Normal;
        _body.enableAutoSizing = true; _body.fontSizeMin = 17f; _body.fontSizeMax = 31f;   // 길면 글자가 작아져 상자 안에 들어간다
        _body.overflowMode = TextOverflowModes.Ellipsis;

        // 건너뛰기 — 커서가 풀려 있을 때 누른다(Enter 키도 된다)
        var btn = NewRect("Skip", panel); btn.anchorMin = btn.anchorMax = new Vector2(1f, 1f); btn.pivot = new Vector2(1f, 1f); btn.anchoredPosition = new Vector2(-16f, -12f); btn.sizeDelta = new Vector2(150f, 40f);
        var bi = btn.gameObject.AddComponent<Image>(); bi.color = new Color(1f, 1f, 1f, 0.14f);
        var b = btn.gameObject.AddComponent<Button>(); b.targetGraphic = bi; b.onClick.AddListener(() => _skip = true);
        var bt = NewText("SkipText", btn, 22f, TextAlignmentOptions.Center); Stretch(bt.rectTransform); bt.text = "건너뛰기 (Enter)"; bt.color = new Color(0.85f, 0.9f, 1f);

        _root.SetActive(false);
    }

    private void ShowLine(DialogueSpeaker sp, string text, string emotion)
    {
        if (_root == null) return;
        bool mentor = sp == DialogueSpeaker.Mentor;
        _name.text = mentor ? MentorName : TraineeName;
        _name.color = mentor ? new Color(1f, 0.82f, 0.35f) : new Color(0.55f, 0.85f, 1f);
        _bar.color = _name.color;
        Sprite face = mentor ? EmotionSprite(emotion) : null;   // 나(학습자)는 얼굴 그림이 없다
        if (face != null) { _portrait.sprite = face; _portrait.color = Color.white; _portrait.preserveAspect = true; _portraitText.text = ""; }
        else { _portrait.sprite = null; _portrait.color = mentor ? new Color(0.78f, 0.55f, 0.15f, 0.95f) : new Color(0.2f, 0.45f, 0.7f, 0.95f); _portraitText.text = mentor ? "선임" : "나"; }
        _body.text = "";
        _body.maxVisibleCharacters = 99999;
        _root.SetActive(true);
        if (_vr) FollowHead(true);
        if (_fade != null) StopCoroutine(_fade); _fade = StartCoroutine(FadeIn());
    }

    private IEnumerator FadeIn()
    {
        float a = _group.alpha;
        while (a < 1f) { a = Mathf.MoveTowards(a, 1f, Time.unscaledDeltaTime * 6f); _group.alpha = a; yield return null; }
    }

    private void SetBody(string t) { if (_body != null) _body.text = t; }

    private void HideView()
    {
        if (_root == null) return;
        _group.alpha = 0f;
        _root.SetActive(false);
    }

    private void FollowHead(bool snap = false)
    {
        var cam = Camera.main; if (cam == null || _root == null) return;
        Vector3 fwd = cam.transform.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward; fwd.Normalize();
        Vector3 pos = cam.transform.position + fwd * 1.4f + Vector3.down * 0.38f;
        Quaternion rot = Quaternion.LookRotation(pos - cam.transform.position, Vector3.up);
        var tr = _root.transform;
        tr.SetPositionAndRotation(snap ? pos : Vector3.Lerp(tr.position, pos, Time.unscaledDeltaTime * 4f), snap ? rot : Quaternion.Slerp(tr.rotation, rot, Time.unscaledDeltaTime * 4f));
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    private static TMP_Text NewText(string name, Transform parent, float size, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var kf = MobileWorkButton.KoreanFont();
        if (kf != null) t.font = kf;
        t.fontSize = size; t.alignment = align; t.color = Color.white; t.raycastTarget = false;
        t.richText = false;   // 서버에서 받은 문구가 서식 태그를 심지 못하게
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    private static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }

    private static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = topLeft; rt.sizeDelta = size;
    }
}
