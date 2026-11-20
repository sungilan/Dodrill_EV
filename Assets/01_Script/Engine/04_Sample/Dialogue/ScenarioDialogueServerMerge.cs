using System;
using System.Collections.Generic;
using System.Linq;

public static class ScenarioDialogueServerMerge
{
    public static bool ApplyServerDialogueOverrides(ScenarioData scenarioData, ServerScenarioDialoguePayload payload, bool verboseLog = false)
    {
        if (scenarioData?.modules == null || payload?.dialogues == null || payload.dialogues.Count == 0)
            return false;

        var lookup = BuildLookup(payload.dialogues);
        int updatedCount = 0;

        foreach (var moduleEntry in scenarioData.modules)
        {
            if (moduleEntry == null || string.IsNullOrWhiteSpace(moduleEntry.moduleId) || moduleEntry.config == null)
                continue;

            if (!lookup.TryGetValue(moduleEntry.moduleId, out var moduleDialogueMap))
                continue;

            moduleEntry.config.dialogueLines ??= new List<DialogueLineJson>();

            foreach (var line in moduleEntry.config.dialogueLines)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.id))
                    continue;

                if (!moduleDialogueMap.TryGetValue(line.id, out var serverLine))
                    continue;

                line.speaker = Coalesce(serverLine.speaker, line.speaker);
                line.ko = Coalesce(serverLine.ko, line.ko);
                line.en = Coalesce(serverLine.en, line.en);
                line.vi = Coalesce(serverLine.vi, line.vi);
                updatedCount++;

                if (verboseLog)
                    UnityEngine.Debug.Log($"[ScenarioDialogueServerMerge] Applied {scenarioData.scenarioId} / {moduleEntry.moduleId} / id={line.id}");
            }
        }

        if (verboseLog)
            UnityEngine.Debug.Log($"[ScenarioDialogueServerMerge] Applied overrides: {updatedCount}");

        return updatedCount > 0;
    }

    private static Dictionary<string, Dictionary<string, ServerDialogueLinePayload>> BuildLookup(List<ServerDialogueLinePayload> dialogues)
    {
        var result = new Dictionary<string, Dictionary<string, ServerDialogueLinePayload>>(StringComparer.Ordinal);
        foreach (var dialogue in dialogues.Where(d => d != null && !string.IsNullOrWhiteSpace(d.module_id)))
        {
            string dialogueId = dialogue.dialogue_id.ToString();
            if (!result.TryGetValue(dialogue.module_id, out var perModule))
            {
                perModule = new Dictionary<string, ServerDialogueLinePayload>(StringComparer.Ordinal);
                result[dialogue.module_id] = perModule;
            }

            perModule[dialogueId] = dialogue;
        }

        return result;
    }

    private static string Coalesce(string preferred, string fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
}

[Serializable]
public class ServerScenarioDialoguePayload
{
    public string scenario_id;
    public string scenario_name_ko;
    public string createdAt;
    public string updatedAt;
    public List<ServerDialogueLinePayload> dialogues;
    public List<ServerTaskDescriptionPayload> task_descriptions;
    public List<ServerTaskDescriptionPayload> module_descriptions;
    public List<ServerTaskDescriptionPayload> descriptions;
}

[Serializable]
public class ServerDialogueLinePayload
{
    public string module_id;
    public int dialogue_id;
    public string speaker;
    public string ko;
    public string en;
    public string vi;
}

[Serializable]
public class ServerTaskDescriptionPayload
{
    public string module_id;
    public string ko;
    public string en;
    public string vi;
}
