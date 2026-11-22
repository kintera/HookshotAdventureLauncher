using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HookshotAdventure.Core;

public static class HAL_ChildStealth
{
    private static readonly string[] Songs = ["Item_Melody_ZeldaLullaby", "Item_Melody_EponaSong", "Item_Melody_SariaSong",
        "Item_Melody_SunSong", "Item_Melody_TimeSong", "Item_Melody_StormSong", "Item_Melody_ForestMinuet",
        "Item_Melody_FireBolero", "Item_Melody_WaterSerenade", "Item_Melody_SpiritRequiem", "Item_Melody_ShadowNocturne", "Item_Melody_LightPrelude"];
    public static string StartingSong(string? seed, bool shuffled)
    {
        if (!shuffled) return Songs[0];
        if (string.IsNullOrEmpty(seed) || seed.EnumerateRunes().Count() > 64) throw new ArgumentException("Invalid child stealth seed.");
        var hash = SHA256.HashData(new UTF8Encoding(false, true).GetBytes("hookshot-child-stealth-song:" + seed));
        return Songs[BinaryPrimitives.ReadUInt64BigEndian(hash) % (ulong)Songs.Length];
    }
    public static byte[] PatchCastleEntrance(byte[] data)
    {
        var f = new HAL_EventFlow(data, "Goto_HyruleCastle");
        if (!f.EntryPoints().TryGetValue("Goto_HyruleCastle_EntryMarket", out var entry) || entry.EventIndex != 22)
            throw new InvalidDataException("Unexpected castle entrance entry.");
        foreach (var (index, challenge, step, next) in new[] { (22,"Goto_HyruleCastle","Step02_01",206),
            (38,"Goto_HyruleCastle","Step03_02",0xffff), (3,"Goto_HyruleCastle","Complete",5), (5,"DoorOfTime","Step01",160) })
        {
            int at = f.EventOffset(index); var p = f.StringParameters(index);
            if (f.UInt(at + 8, 1) != 0 || f.UInt(at + 10, 2) != (ulong)next ||
                f.Action(index) != ("ChallengeSystemActor", "EventExecuteProgressChallengeStep") || p.Count != 2 ||
                !p.TryGetValue("ChallengeName", out var actualChallenge) || actualChallenge != challenge ||
                !p.TryGetValue("StepName", out var actualStep) || actualStep != step)
                throw new InvalidDataException("Castle quest flow differs from the supported version.");
        }
        var result = (byte[])data.Clone();
        foreach (var (index, next) in new[] { (22,38), (38,3), (5,0xffff) })
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(f.EventOffset(index) + 10), (ushort)next);
        return result;
    }

    public static byte[] PatchMap(byte[] data, bool castle)
    {
        const string oldName = "Goto_HyruleCastle_IsAfter_Step03_02_Exp", newName = "Talon_AfterWakeUp";
        const ulong malon = 3576280271882277111, talon = 5439531180495276262;
        var b = new HAL_Byml(data);
        if (b.Values.Count(x => x == oldName) != 1 || b.Values.Contains(newName)) throw new InvalidDataException("Unexpected presence string table.");
        var observed = new List<(string, ulong?, ulong)>(); int? talonSlot = null, malonPresence = null;
        foreach (var actor in b.Array(b.Fields(b.Root)["Actors"]))
        {
            int at = checked((int)b.Child(actor, 0xc1)); var fields = b.Fields(at);
            string name = fields.TryGetValue("Gyaml", out var gyaml) ? b.Text(gyaml) : "";
            ulong? id = fields.TryGetValue("Hash", out var hash) ? b.UInt(checked((int)b.Child(hash, 0xd5)), 8) : null;
            if (castle && name == "Npc_Hylia_Talon_Child" && id == talon)
            {
                if (fields.ContainsKey("Presence") || talonSlot is not null) throw new InvalidDataException("Unexpected castle Talon placement.");
                talonSlot = actor.Slot;
            }
            if (!fields.TryGetValue("Presence", out var presenceField)) continue;
            int presenceAt = checked((int)b.Child(presenceField, 0xc1)); var presence = b.Fields(presenceAt);
            if (b.Text(presence["FlagName"]) != oldName) continue;
            observed.Add((name, castle && name == "Npc_Hylia_Malon_Child" ? id : null, b.UInt(presence["IsNegation"].Slot)));
            if (castle && name == "Npc_Hylia_Malon_Child" && id == malon) malonPresence = presenceAt;
        }
        var expected = castle ? new[] { ("Npc_Hylia_Malon_Child", (ulong?)malon, 1UL), ("Animal_Horse_Wagon", (ulong?)null, 1UL) } :
            new[] { ("Npc_Hylia_Malon_Child", (ulong?)null, 0UL), ("Npc_Hylia_Talon_Child", (ulong?)null, 0UL), ("Animal_Epona_Child", (ulong?)null, 0UL) };
        if (observed.Count != expected.Length || !observed.ToHashSet().SetEquals(expected) || (castle && (talonSlot is null || malonPresence is null)))
            throw new InvalidDataException("Unexpected child stealth actor placements.");
        int table = checked((int)b.UInt(8)), index = b.Values.ToList().IndexOf(oldName);
        int textAt = checked(table + (int)b.UInt(table + 4 + index * 4));
        var oldBytes = Encoding.UTF8.GetBytes(oldName + "\0");
        if (!b.Bytes(textAt, oldBytes.Length).SequenceEqual(oldBytes)) throw new InvalidDataException("Unexpected presence text.");
        var result = data.ToList();
        var replacement = new byte[oldBytes.Length]; Encoding.UTF8.GetBytes(newName).CopyTo(replacement, 0);
        for (int i = 0; i < replacement.Length; i++) result[textAt + i] = replacement[i];
        void Write(int at, ulong value, int size)
        {
            for (int i = 0; i < size; i++) result[at + (b.LittleEndian ? i : size - i - 1)] = (byte)(value >> (8 * i));
        }
        if (castle)
        {
            int actorAt = checked((int)b.UInt(talonSlot!.Value));
            if (b.Fields(actorAt).ContainsKey("Presence") || b.Keys.Count(x => x == "Presence") != 1)
                throw new InvalidDataException("Unexpected castle Talon dictionary.");
            var entries = new List<(int Key, byte[] Data)>();
            for (int i = 0; i < b.Header(actorAt, 0xc1); i++)
            {
                int at = checked(actorAt + 4 + i * 8); entries.Add(((int)b.UInt(at, 3), b.Bytes(at, 8)));
            }
            int key = b.Keys.ToList().IndexOf("Presence"); entries.Add((key, new byte[8]));
            while (result.Count % 4 != 0) result.Add(0);
            int newAt = result.Count; result.AddRange(new byte[4]); result[newAt] = 0xc1; Write(newAt + 1, (uint)entries.Count, 3);
            foreach (var item in entries.OrderBy(x => x.Key))
            {
                int at = result.Count; result.AddRange(item.Data);
                if (item.Key == key) { Write(at, (uint)key, 3); result[at + 3] = 0xc1; Write(at + 4, (uint)malonPresence!.Value, 4); }
            }
            Write(talonSlot.Value, (uint)newAt, 4);
        }
        return result.ToArray();
    }
}
