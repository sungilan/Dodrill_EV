using System.Collections.Generic;
using System.Text;
using FishNet.Serializing;

// ============================================================
//  FishNetScenarioSerializers.cs
//  FishNet 커스텀 직렬화 - 한글 UTF-8 + null List 대응
// ============================================================

public static class FishNetScenarioSerializers
{
    // ─────────────────────────────────────────
    // UTF-8 안전 string 읽기/쓰기
    // ─────────────────────────────────────────

    private static void WriteUtf8String(this Writer writer, string value)
    {
        if (value == null) { writer.WriteInt32(-1); return; }
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.WriteInt32(bytes.Length);
        writer.WriteBytes(bytes, 0, bytes.Length);
    }

    private static string ReadUtf8String(this Reader reader)
    {
        int len = reader.ReadInt32();
        if (len < 0) return null;
        if (len == 0) return string.Empty;
        byte[] bytes = new byte[len];
        reader.ReadBytes(ref bytes, len);
        return Encoding.UTF8.GetString(bytes);
    }

    // ─────────────────────────────────────────
    // LocalizedString
    // ─────────────────────────────────────────

    public static void WriteLocalizedString(this Writer writer, LocalizedString value)
    {
        bool isNull = value == null;
        writer.WriteBoolean(isNull);
        if (isNull) return;
        writer.WriteUtf8String(value.ko);
        writer.WriteUtf8String(value.en);
        writer.WriteUtf8String(value.vi);
    }

    public static LocalizedString ReadLocalizedString(this Reader reader)
    {
        if (reader.ReadBoolean()) return null;
        return new LocalizedString
        {
            ko = reader.ReadUtf8String(),
            en = reader.ReadUtf8String(),
            vi = reader.ReadUtf8String(),
        };
    }

    // ─────────────────────────────────────────
    // StringKVPair
    // ─────────────────────────────────────────

    public static void WriteStringKVPair(this Writer writer, StringKVPair value)
    {
        writer.WriteUtf8String(value.key);
        writer.WriteUtf8String(value.ko);
        writer.WriteUtf8String(value.en);
        writer.WriteUtf8String(value.vi);
    }

    public static StringKVPair ReadStringKVPair(this Reader reader)
    {
        return new StringKVPair
        {
            key = reader.ReadUtf8String(),
            ko = reader.ReadUtf8String(),
            en = reader.ReadUtf8String(),
            vi = reader.ReadUtf8String(),
        };
    }

    // ─────────────────────────────────────────
    // SpawnBundle
    // spawnObjects 필드가 없는 JSON → null 대신 빈 리스트로 안전 처리
    // ─────────────────────────────────────────

    public static void WriteSpawnBundle(this Writer writer, SpawnBundle value)
    {
        writer.WriteUtf8String(value.prefabId ?? string.Empty);
        writer.WriteUtf8String(value.spawnPointId ?? string.Empty);
        writer.WriteUtf8String(value.guideId ?? string.Empty);
    }

    public static SpawnBundle ReadSpawnBundle(this Reader reader)
    {
        return new SpawnBundle
        {
            prefabId = reader.ReadUtf8String(),
            spawnPointId = reader.ReadUtf8String(),
            guideId = reader.ReadUtf8String(),
        };
    }

    // ─────────────────────────────────────────
    // TaskDef
    // spawnObjects 가 null 이면 빈 리스트로 직렬화
    // ─────────────────────────────────────────

    public static void WriteTaskDef(this Writer writer, TaskDef value)
    {
        writer.WriteUtf8String(value.moduleId ?? string.Empty);
        writer.WriteSingle(value.timeout);
        writer.WriteUtf8String(value.completionMode ?? string.Empty);

        // spawnObjects null → 빈 리스트로 전송
        var list = value.spawnObjects ?? new List<SpawnBundle>();
        writer.WriteInt32(list.Count);
        foreach (var bundle in list)
            writer.WriteSpawnBundle(bundle);
    }

    public static TaskDef ReadTaskDef(this Reader reader)
    {
        var task = new TaskDef
        {
            moduleId = reader.ReadUtf8String(),
            timeout = reader.ReadSingle(),
            completionMode = reader.ReadUtf8String(),
            spawnObjects = new List<SpawnBundle>(),
        };

        int count = reader.ReadInt32();
        // 음수 방어 (count < 0 이면 빈 리스트 유지)
        if (count > 0)
        {
            for (int i = 0; i < count; i++)
                task.spawnObjects.Add(reader.ReadSpawnBundle());
        }

        return task;
    }

    // ─────────────────────────────────────────
    // ItemUsedSignal
    // ─────────────────────────────────────────

    public static void WriteItemUsedSignal(this Writer writer, ItemUsedSignal value)
    {
        writer.WriteInt32(value.taskIndex);
        writer.WriteUtf8String(value.itemId ?? string.Empty);
        writer.WriteInt32(value.clientId);
    }

    public static ItemUsedSignal ReadItemUsedSignal(this Reader reader)
    {
        return new ItemUsedSignal
        {
            taskIndex = reader.ReadInt32(),
            itemId    = reader.ReadUtf8String(),
            clientId  = reader.ReadInt32(),
        };
    }

    // ─────────────────────────────────────────
    // ItemGrabbedSignal
    // ─────────────────────────────────────────

    public static void WriteItemGrabbedSignal(this Writer writer, ItemGrabbedSignal value)
    {
        writer.WriteInt32(value.taskIndex);
        writer.WriteUtf8String(value.itemId ?? string.Empty);
        writer.WriteInt32(value.clientId);
    }

    public static ItemGrabbedSignal ReadItemGrabbedSignal(this Reader reader)
    {
        return new ItemGrabbedSignal
        {
            taskIndex = reader.ReadInt32(),
            itemId    = reader.ReadUtf8String(),
            clientId  = reader.ReadInt32(),
        };
    }

    // ─────────────────────────────────────────
    // TaskConfirmSignal
    // ─────────────────────────────────────────

    public static void WriteTaskConfirmSignal(this Writer writer, TaskConfirmSignal value)
    {
        writer.WriteInt32(value.taskIndex);
        writer.WriteUtf8String(value.moduleId ?? string.Empty);
        writer.WriteInt32(value.clientId);
    }

    public static TaskConfirmSignal ReadTaskConfirmSignal(this Reader reader)
    {
        return new TaskConfirmSignal
        {
            taskIndex = reader.ReadInt32(),
            moduleId  = reader.ReadUtf8String(),
            clientId  = reader.ReadInt32(),
        };
    }

    // ─────────────────────────────────────────
    // ZoneInteractionSignal (클라이언트 → 서버)
    // ─────────────────────────────────────────

    public static void WriteZoneInteractionSignal(this Writer writer, ZoneInteractionSignal value)
    {
        writer.WriteInt32(value.taskIndex);
        writer.WriteUtf8String(value.zoneId ?? string.Empty);
        writer.WriteUtf8String(value.itemId ?? string.Empty);
        writer.WriteInt32(value.clientId);
    }

    public static ZoneInteractionSignal ReadZoneInteractionSignal(this Reader reader)
    {
        return new ZoneInteractionSignal
        {
            taskIndex = reader.ReadInt32(),
            zoneId    = reader.ReadUtf8String(),
            itemId    = reader.ReadUtf8String(),
            clientId  = reader.ReadInt32(),
        };
    }

    // ─────────────────────────────────────────
    // PlaySnapAnimBroadcast (서버 → 전체 클라이언트)
    // ─────────────────────────────────────────

    public static void WritePlaySnapAnimBroadcast(this Writer writer, PlaySnapAnimBroadcast value)
    {
        writer.WriteInt32(value.taskIndex);
        writer.WriteUtf8String(value.itemId  ?? string.Empty);
        writer.WriteUtf8String(value.guideId ?? string.Empty);
        writer.WriteSingle(value.snapDuration);
    }

    public static PlaySnapAnimBroadcast ReadPlaySnapAnimBroadcast(this Reader reader)
    {
        return new PlaySnapAnimBroadcast
        {
            taskIndex    = reader.ReadInt32(),
            itemId       = reader.ReadUtf8String(),
            guideId      = reader.ReadUtf8String(),
            snapDuration = reader.ReadSingle(),
        };
    }
}
