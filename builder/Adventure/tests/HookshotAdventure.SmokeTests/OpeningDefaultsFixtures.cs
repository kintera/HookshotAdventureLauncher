// Authored fixtures from tests/test_saria_greeting.py; no game data.
using HookshotAdventure.Core;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Buffers.Binary;

static class OpeningDefaultsFixtures
{
    public static void Check()
    {
        string[] fixtures = [
            "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",
            "eJyLdGJnEGBgYGgA4gwmBgYAEEYBnQ==",
        ];
        var decoded = fixtures.Select(text => { using var stream = new ZLibStream(new MemoryStream(Convert.FromBase64String(text)), CompressionMode.Decompress); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }).ToArray();
        var forest=(byte[])decoded[0].Clone(); var b=new HAL_Byml(forest);
        int Node(HAL_Byml.Field field)=>checked((int)b.Child(field,0xc1));
        var tables=b.Fields(Node(b.Fields(b.Root)["Data"])); var flags=b.Array(tables["Bool"]).Take(2).Select(x=>b.Fields(Node(x))).ToArray();
        uint[] hashes=[HAL_GameDataHash.Name("KokiriForest_Npc_Kokiri_Mido_Child_Blocker"),HAL_GameDataHash.Name("KokiriForest_Npc_Kokiri_007_Child_Blocker_00")];
        for(int i=0;i<2;i++) { BinaryPrimitives.WriteUInt32LittleEndian(forest.AsSpan(flags[i]["Hash"].Slot),hashes[i]);
            BinaryPrimitives.WriteUInt32LittleEndian(forest.AsSpan(flags[i]["DefaultValue"].Slot),1); }
        byte[] gates=HAL_OpenForestDefaults.Patch(forest,true,true);
        AssertTest.True(flags.All(f=>BinaryPrimitives.ReadUInt32LittleEndian(gates.AsSpan(f["DefaultValue"].Slot))==0) &&
            flags.All(f=>BinaryPrimitives.ReadUInt32LittleEndian(forest.AsSpan(f["DefaultValue"].Slot))==1),
            "forest blocker defaults are changed only on a private copy");
        byte[] deku=HAL_OpenForestDefaults.Patch(forest,false,true);
        AssertTest.True(BinaryPrimitives.ReadUInt32LittleEndian(deku.AsSpan(flags[0]["DefaultValue"].Slot))==0 &&
            BinaryPrimitives.ReadUInt32LittleEndian(deku.AsSpan(flags[1]["DefaultValue"].Slot))==1,
            "Deku and forest blocker defaults are independently selectable");
        bool missing=false; try { HAL_OpenForestDefaults.Patch(decoded[0],true,true); } catch(InvalidDataException) { missing=true; }
        AssertTest.True(missing,"forest blocker edit rejects a resource missing its verified flags");
        AssertTest.True(Convert.ToHexString(SHA256.HashData(HAL_OpeningDefaults.Patch(decoded[0]))) == "CBFD8F5619C1E4AD28DB07600F1480AA634A70FF33C4C3184A31EC45365210C3", "opening defaults match Python on an authored fixture");
        foreach (var invalid in decoded.Skip(1))
        {
            bool rejected = false;
            try { HAL_OpeningDefaults.Patch(invalid); } catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException) { rejected = true; }
            AssertTest.True(rejected, "opening defaults reject invalid mapping or policy");
        }
    }
}
