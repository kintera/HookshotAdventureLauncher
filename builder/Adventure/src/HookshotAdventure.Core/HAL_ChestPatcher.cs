using System.Text;

namespace HookshotAdventure.Core;

public sealed record HAL_ChestAssignment(ulong Placement, string Actor, string ExpectedReward,
    string? ExpectedEvent, string Reward, string? Event, IReadOnlyList<string> Resources, bool RuntimeBlocked = false);

/// <summary>Validate all chest edits before appending private BYML parameter nodes.</summary>
public static class HAL_ChestPatcher
{
    private const ulong Sword = 16757992849254467386, Slingshot = 821372075822700622;
    private const string SwordEvent = "Get_StoneOfForest_GetSword", SlingshotEvent = "InsideTheDekuTree_GetSlingShot";
    private static readonly HashSet<string> Rewards = new(StringComparer.Ordinal)
    {
        "TBox_Rupee_Green", "TBox_Rupee_Blue", "TBox_Rupee_Red", "TBox_Rupee_Purple", "TBox_Rupee_Gold",
        "TBox_ArrowBundle_Medium", "TBox_ArrowBundle_Large", "Item_BombBundle_Small", "Item_BombchuBundle_Small",
        "TBox_DekuNutBundle", "Item_DekuStickBundle_Small", "TBox_DekuSeedBundle", "Tbox_MagicJar_Large",
        "Item_Sword_Kokiri", "Item_Range_SlingShot", "Obj_Heart_Piece",
        "TBox_Shield_Deku", "Item_GlassTruth", "Item_BombBag_Small", "Item_Boomerang", "Item_BulletBag_Medium", "Item_Ocarina_Fairy",
        "Item_Wallet_Medium", "Item_Wallet_Large", "Item_AgonyStone", "Item_BombchuBundle_Large"
    };
    private sealed record Edit(int Pointer, int Node, Dictionary<int, string?> Changes);

    public static byte[] Patch(byte[] data, IReadOnlyList<HAL_ChestAssignment> assignments) => PatchCore(data, assignments, false);

    /// <summary>Reproduce the old local Mido/Sword exchange for research; never used by seed generation.</summary>
    public static byte[] PatchMidoExperiment(byte[] data) => PatchCore(data, new[]
    {
        new HAL_ChestAssignment(9725242744547055868UL, "Obj_TBox_A_01", "TBox_Rupee_Blue", "KokiriForest_TreasureBox_NaviChatOff", "Item_Sword_Kokiri", "KokiriForest_TreasureBox_NaviChatOff", Array.Empty<string>()),
        new HAL_ChestAssignment(Sword, "Obj_TBox_A_02", "Item_Sword_Kokiri", SwordEvent, "TBox_Rupee_Blue", null, Array.Empty<string>())
    }, true);

    private static byte[] PatchCore(byte[] data, IReadOnlyList<HAL_ChestAssignment> assignments, bool midoExperiment)
    {
        var b = new HAL_Byml(data);
        var root = b.Fields(b.Root);
        var index = new Dictionary<ulong, List<IReadOnlyDictionary<string, HAL_Byml.Field>>>();
        if (root.TryGetValue("Actors", out var actors))
            foreach (var field in b.Array(actors))
            {
                var actor = b.Fields(checked((int)b.Child(field, 0xc1)));
                if (!actor.TryGetValue("Hash", out var hash)) continue;
                ulong id = b.UInt(checked((int)b.Child(hash, 0xd5)), 8);
                if (!index.TryGetValue(id, out var matches)) index[id] = matches = new();
                matches.Add(actor);
            }
        var edits = new List<Edit>(); var seen = new HashSet<ulong>();
        foreach (var a in assignments)
        {
            if (a.RuntimeBlocked) throw new InvalidDataException("Assignment requires unimplemented runtime reward.");
            if (!seen.Add(a.Placement) || !index.TryGetValue(a.Placement, out var rows) || rows.Count != 1)
                throw new InvalidDataException("Duplicate or missing target placement.");
            var actor = rows[0]; int node = checked((int)b.Child(actor["Dynamic"], 0xc1));
            var dynamic = b.Fields(node);
            string? currentEvent = dynamic.TryGetValue("GetEventName", out var eventField) ? b.Text(eventField) : null;
            if (b.Text(actor["Gyaml"]) != a.Actor || b.Text(dynamic["Drop__DropActor"]) != a.ExpectedReward || currentEvent != a.ExpectedEvent)
                throw new InvalidDataException("Source chest metadata mismatch.");
            if (a.Reward == a.ExpectedReward && a.Event == currentEvent) continue;
            var changes = new Dictionary<int, string?> { [dynamic["Drop__DropActor"].Slot] = a.Reward };
            if (a.Event != currentEvent)
            {
                bool dungeon = false;
                if (a.ExpectedReward is "Item_Dungeon_Map" or "Item_Dungeon_Compass" && a.Resources.Count == 1)
                {
                    string path = a.Resources[0]; int start = path.IndexOf("Banc/", StringComparison.Ordinal);
                    if (start >= 0)
                    {
                        string region = path[(start + 5)..].Split('/')[0];
                        dungeon = currentEvent == region + (a.ExpectedReward == "Item_Dungeon_Map" ? "_GetMap" : "_GetCompass");
                    }
                }
                if (!((a.Placement == Sword && currentEvent == SwordEvent) ||
                    (a.Placement == Slingshot && currentEvent == SlingshotEvent) || dungeon) || a.Event is not null)
                    throw new InvalidDataException("Unreviewed event rewrite.");
                changes[eventField.Slot] = null;
            }
            if (!Rewards.Contains(a.Reward)) throw new InvalidDataException("Unreviewed replacement reward.");
            edits.Add(new Edit(actor["Dynamic"].Slot, node, changes));
        }
        var result = new List<byte>(data);
        void Align() { while ((result.Count & 3) != 0) result.Add(0); }
        void Put(int at, uint value, int size)
        {
            for (int i = 0; i < size; i++) result[at + (b.LittleEndian ? i : size - 1 - i)] = (byte)(value >> (8 * i));
        }
        void Add(uint value, int size) { int at = result.Count; result.AddRange(new byte[size]); Put(at, value, size); }
        var missing = edits.SelectMany(e => e.Changes.Values).Where(v => v is not null && !b.Values.Contains(v))
            .Select(v => v!).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray();
        if (missing.Length != 0)
        {
            var values = b.Values.Concat(missing).ToArray(); Align(); int table = result.Count;
            result.Add(0xc2); Add((uint)values.Length, 3);
            int bytes = checked(4 + (values.Length + 1) * 4);
            var encoded = values.Select(Encoding.UTF8.GetBytes).ToArray();
            foreach (var text in encoded) { Add((uint)bytes, 4); bytes = checked(bytes + text.Length + 1); }
            Add((uint)bytes, 4);
            foreach (var text in encoded) { result.AddRange(text); result.Add(0); }
            Put(8, (uint)table, 4); b = new HAL_Byml(result.ToArray());
        }
        foreach (var edit in edits)
        {
            var entries = new List<byte[]>();
            for (int i = 0; i < b.Header(edit.Node, 0xc1); i++)
            {
                int at = checked(edit.Node + 4 + i * 8), slot = at + 4;
                if (edit.Changes.TryGetValue(slot, out var replacement) && replacement is null) continue;
                byte[] entry = b.Bytes(at, 8);
                if (edit.Changes.TryGetValue(slot, out replacement))
                {
                    var matches = b.Values.Select((v, n) => (v, n)).Where(x => x.v == replacement).ToArray();
                    if (matches.Length != 1) throw new InvalidDataException("Replacement missing or duplicated in string table.");
                    uint n = (uint)matches[0].n;
                    for (int j = 0; j < 4; j++) entry[4 + (b.LittleEndian ? j : 3 - j)] = (byte)(n >> (8 * j));
                }
                entries.Add(entry);
            }
            Align(); int privateAt = result.Count; result.Add(0xc1); Add((uint)entries.Count, 3);
            foreach (var entry in entries) result.AddRange(entry);
            Put(edit.Pointer, (uint)privateAt, 4);
        }
        return result.ToArray();
    }
}
