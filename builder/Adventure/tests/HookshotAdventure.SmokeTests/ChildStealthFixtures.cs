// Authored castle quest fixture; no game data.
using HookshotAdventure.Core;
using System.IO.Compression;
using System.Security.Cryptography;
static class ChildStealthFixtures
{
    public static void Check()
    {
        using var input = new ZLibStream(new MemoryStream(Convert.FromBase64String("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")), CompressionMode.Decompress);
        using var output = new MemoryStream(); input.CopyTo(output);
        var patched = HAL_ChildStealth.PatchCastleEntrance(output.ToArray());
        AssertTest.True(Convert.ToHexString(SHA256.HashData(patched)) == "ADF7EF417D51BD33A3C7548974C00C178F873B57119C55A87FF0A5A26359AABE", "castle stealth quest patch matches Python");
        AssertTest.True(HAL_ChildStealth.StartingSong("ABC123", true) == "Item_Melody_StormSong" && HAL_ChildStealth.StartingSong(null, false) == "Item_Melody_ZeldaLullaby", "child stealth song selection matches Python");
        bool rejected = false;
        try { HAL_ChildStealth.PatchCastleEntrance(patched); } catch (InvalidDataException) { rejected = true; }
        AssertTest.True(rejected, "castle stealth patch rejects altered flow");
    }
}
