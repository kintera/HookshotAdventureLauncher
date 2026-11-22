using System.Buffers.Binary;
using System.Text;

namespace HookshotAdventure.Core;

/// <summary>Read-only inventory of potentially relevant timeline actions; these are not confirmed rewards.</summary>
public static class HAL_EventTimelineScan
{
    public sealed record Candidate(string Resource, string Kind, int Index, string Actor, string Action, string Parameter, string Value);

    public static IReadOnlyList<Candidate> Inspect(string resource, byte[] data)
    {
        if (!HAL_EventRewardScan.IsTimelineOnly(data)) throw new InvalidDataException("Not a timeline-only event resource.");
        var utf8 = new UTF8Encoding(false, true);
        ReadOnlySpan<byte> Slice(int offset, int size) => offset >= 0 && size >= 0 && (long)offset + size <= data.Length
            ? data.AsSpan(offset, size) : throw new InvalidDataException("Timeline data is truncated or out of range.");
        int U16(int o) => BinaryPrimitives.ReadUInt16LittleEndian(Slice(o, 2));
        uint U32(int o) => BinaryPrimitives.ReadUInt32LittleEndian(Slice(o, 4));
        int Ptr(int o) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(Slice(o, 8)));
        string StringAt(int o)
        {
            int p = Ptr(o);
            if (p == 0) throw new InvalidDataException("Null timeline string.");
            try { return utf8.GetString(Slice(checked(p + 2), U16(p))); }
            catch (DecoderFallbackException ex) { throw new InvalidDataException("Invalid timeline string.", ex); }
        }
        int root = Ptr(56);
        if (root == 0) throw new InvalidDataException("Missing timeline pointer array.");
        int timeline = Ptr(root);
        if (!Slice(timeline, 4).SequenceEqual("TLIN"u8)) throw new InvalidDataException("Missing timeline header.");
        int actorCount = U16(timeline + 0x14), clipCount = U16(timeline + 0x18), shotCount = U16(timeline + 0x1a);
        int actors = Ptr(timeline + 0x28), clips = Ptr(timeline + 0x30), shots = Ptr(timeline + 0x38);
        if ((actorCount > 0 && actors == 0) || (clipCount > 0 && clips == 0) || (shotCount > 0 && shots == 0))
            throw new InvalidDataException("Missing timeline array.");
        Slice(actors, checked(actorCount * 0x38)); Slice(clips, checked(clipCount * 0x18)); Slice(shots, checked(shotCount * 0x18));
        var result = new List<Candidate>();
        void ReadAction(int entry, bool clip, int index)
        {
            int actorIndex = U16(entry + (clip ? 8 : 4)), actionIndex = U16(entry + (clip ? 10 : 6));
            if (actorIndex >= actorCount) throw new InvalidDataException("Invalid timeline actor index.");
            int actor = actors + actorIndex * 0x38, actionCount = U16(actor + 0x30), actions = Ptr(actor + 0x18);
            if (actionIndex >= actionCount || actions == 0) throw new InvalidDataException("Invalid timeline action index.");
            string actorName = StringAt(actor), action = StringAt(checked(actions + 8 * actionIndex));
            int parameters = Ptr(entry + 0x10);
            if (parameters == 0) return;
            if (Slice(parameters, 1)[0] != 1) throw new InvalidDataException("Invalid timeline parameter container.");
            int count = U16(parameters + 2), dict = Ptr(parameters + 8);
            if (dict == 0 || !Slice(dict, 4).SequenceEqual("DIC "u8) || U32(dict + 4) != count)
                throw new InvalidDataException("Invalid timeline parameter dictionary.");
            for (int i = 0; i < count; i++)
            {
                string key = StringAt(checked(dict + 32 + i * 16));
                int item = Ptr(checked(parameters + 16 + i * 8));
                if (item == 0) throw new InvalidDataException("Missing timeline parameter.");
                byte type = Slice(item, 1)[0];
                if (type != 5) continue;
                string value = StringAt(checked(item + 16));
                if (value.StartsWith("Item_", StringComparison.Ordinal) || value.StartsWith("Weapon_", StringComparison.Ordinal) ||
                    value.StartsWith("Obj_Heart", StringComparison.Ordinal) || value.StartsWith("Obj_Rupee", StringComparison.Ordinal) ||
                    value.StartsWith("TBox_", StringComparison.Ordinal))
                    result.Add(new(resource, clip ? "clip" : "oneshot", index, actorName, action, key, value));
            }
        }
        for (int i = 0; i < clipCount; i++) ReadAction(clips + i * 0x18, true, i);
        for (int i = 0; i < shotCount; i++) ReadAction(shots + i * 0x18, false, i);
        return result;
    }
}
