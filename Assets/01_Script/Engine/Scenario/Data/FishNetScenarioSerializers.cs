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

    // (11/20) 예전엔 private 확장 메서드(this Writer, string)라 FishNet 코드 생성기가 'string 직렬화기'로 등록하려다
    // MethodAccessException 으로 생성 직렬화기 초기화가 실패했다(어셈블리 분리 후 드러남). 확장이 아닌 도우미로 둔다 — 바이트 형식은 같다.
    private static void WStr(Writer writer, string value)
    {
        if (value == null) { writer.WriteInt32(-1); return; }
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.WriteInt32(bytes.Length);
        writer.WriteBytes(bytes, 0, bytes.Length);
    }

    private static string RStr(Reader reader)
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
        WStr(writer, value.ko);
        WStr(writer, value.en);
        WStr(writer, value.vi);
    }

    public static LocalizedString ReadLocalizedString(this Reader reader)
    {
        if (reader.ReadBoolean()) return null;
        return new LocalizedString
        {
            ko = RStr(reader),
            en = RStr(reader),
            vi = RStr(reader),
        };
    }

    // ─────────────────────────────────────────
    // StringKVPair
    // ─────────────────────────────────────────

    public static void WriteStringKVPair(this Writer writer, StringKVPair value)
    {
        WStr(writer, value.key);
        WStr(writer, value.ko);
        WStr(writer, value.en);
        WStr(writer, value.vi);
    }

    public static StringKVPair ReadStringKVPair(this Reader reader)
    {
        return new StringKVPair
        {
            key = RStr(reader),
            ko = RStr(reader),
            en = RStr(reader),
            vi = RStr(reader),
        };
    }

    // ─────────────────────────────────────────
    // SpawnBundle
    // spawnObjects 필드가 없는 JSON → null 대신 빈 리스트로 안전 처리
    // ─────────────────────────────────────────

    public static void WriteSpawnBundle(this Writer writer, SpawnBundle value)
    {
        WStr(writer, value.prefabId ?? string.Empty);
        WStr(writer, value.spawnPointId ?? string.Empty);
        WStr(writer, value.guideId ?? string.Empty);
        // 예전에는 아래 4개를 보내지 않아 클라이언트가 항상 기본값(showGuide=true 등)을 받았다
        WStr(writer, value.targetGuideId ?? string.Empty);
        writer.WriteBoolean(value.showGuide);
        writer.WriteBoolean(value.persist);
        writer.WriteBoolean(value.attachToSpawnPoint);
    }

    public static SpawnBundle ReadSpawnBundle(this Reader reader)
    {
        return new SpawnBundle
        {
            prefabId = RStr(reader),
            spawnPointId = RStr(reader),
            guideId = RStr(reader),
            targetGuideId = RStr(reader),
            showGuide = reader.ReadBoolean(),
            persist = reader.ReadBoolean(),
            attachToSpawnPoint = reader.ReadBoolean(),
        };
    }

    // ─────────────────────────────────────────
    // TaskDef
    // spawnObjects 가 null 이면 빈 리스트로 직렬화
    // ─────────────────────────────────────────

    public static void WriteTaskDef(this Writer writer, TaskDef value)
    {
        WStr(writer, value.moduleId ?? string.Empty);
        writer.WriteSingle(value.timeout);
        WStr(writer, value.completionMode ?? string.Empty);

        // spawnObjects null → 빈 리스트로 전송
        var list = value.spawnObjects ?? new List<SpawnBundle>();
        writer.WriteInt32(list.Count);
        foreach (var bundle in list)
            writer.WriteSpawnBundle(bundle);

        // despawnObjectIds (null -> 빈 목록)
        var despawn = value.despawnObjectIds ?? new List<string>();
        writer.WriteInt32(despawn.Count);
        foreach (var id in despawn)
            WStr(writer, id ?? string.Empty);
    }

    public static TaskDef ReadTaskDef(this Reader reader)
    {
        var task = new TaskDef
        {
            moduleId = RStr(reader),
            timeout = reader.ReadSingle(),
            completionMode = RStr(reader),
            spawnObjects = new List<SpawnBundle>(),
            despawnObjectIds = new List<string>(),
        };

        int count = reader.ReadInt32();
        // 음수 방어 (count < 0 이면 빈 리스트 유지)
        if (count > 0)
        {
            for (int i = 0; i < count; i++)
                task.spawnObjects.Add(reader.ReadSpawnBundle());
        }

        int despawnCount = reader.ReadInt32();
        if (despawnCount > 0)
        {
            for (int i = 0; i < despawnCount; i++)
                task.despawnObjectIds.Add(RStr(reader));
        }

        return task;
    }

    // ─────────────────────────────────────────
    // ItemUsedSignal
    // ─────────────────────────────────────────

    public static void WriteItemUsedSignal(this Writer writer, ItemUsedSignal value)
    {
        writer.WriteInt32(value.taskIndex);
        WStr(writer, value.itemId ?? string.Empty);
        writer.WriteInt32(value.clientId);
    }

    public static ItemUsedSignal ReadItemUsedSignal(this Reader reader)
    {
        return new ItemUsedSignal
        {
            taskIndex = reader.ReadInt32(),
            itemId    = RStr(reader),
            clientId  = reader.ReadInt32(),
        };
    }

    // ─────────────────────────────────────────
    // ItemGrabbedSignal
    // ─────────────────────────────────────────

    public static void WriteItemGrabbedSignal(this Writer writer, ItemGrabbedSignal value)
    {
        writer.WriteInt32(value.taskIndex);
        WStr(writer, value.itemId ?? string.Empty);
        writer.WriteInt32(value.clientId);
    }

    public static ItemGrabbedSignal ReadItemGrabbedSignal(this Reader reader)
    {
        return new ItemGrabbedSignal
        {
            taskIndex = reader.ReadInt32(),
            itemId    = RStr(reader),
            clientId  = reader.ReadInt32(),
        };
    }

    // ─────────────────────────────────────────
    // TaskConfirmSignal
    // ─────────────────────────────────────────

    public static void WriteTaskConfirmSignal(this Writer writer, TaskConfirmSignal value)
    {
        writer.WriteInt32(value.taskIndex);
        WStr(writer, value.moduleId ?? string.Empty);
        writer.WriteInt32(value.clientId);
    }

    public static TaskConfirmSignal ReadTaskConfirmSignal(this Reader reader)
    {
        return new TaskConfirmSignal
        {
            taskIndex = reader.ReadInt32(),
            moduleId  = RStr(reader),
            clientId  = reader.ReadInt32(),
        };
    }

    // ─────────────────────────────────────────
    // ZoneInteractionSignal (클라이언트 → 서버)
    // ─────────────────────────────────────────

    public static void WriteZoneInteractionSignal(this Writer writer, ZoneInteractionSignal value)
    {
        writer.WriteInt32(value.taskIndex);
        WStr(writer, value.zoneId ?? string.Empty);
        WStr(writer, value.itemId ?? string.Empty);
        writer.WriteInt32(value.clientId);
    }

    public static ZoneInteractionSignal ReadZoneInteractionSignal(this Reader reader)
    {
        return new ZoneInteractionSignal
        {
            taskIndex = reader.ReadInt32(),
            zoneId    = RStr(reader),
            itemId    = RStr(reader),
            clientId  = reader.ReadInt32(),
        };
    }

    // ─────────────────────────────────────────
    // PlaySnapAnimBroadcast (서버 → 전체 클라이언트)
    // ─────────────────────────────────────────

    public static void WritePlaySnapAnimBroadcast(this Writer writer, PlaySnapAnimBroadcast value)
    {
        writer.WriteInt32(value.taskIndex);
        WStr(writer, value.itemId  ?? string.Empty);
        WStr(writer, value.guideId ?? string.Empty);
        writer.WriteSingle(value.snapDuration);
    }

    public static PlaySnapAnimBroadcast ReadPlaySnapAnimBroadcast(this Reader reader)
    {
        return new PlaySnapAnimBroadcast
        {
            taskIndex    = reader.ReadInt32(),
            itemId       = RStr(reader),
            guideId      = RStr(reader),
            snapDuration = reader.ReadSingle(),
        };
    }
}
