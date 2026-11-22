namespace HookshotAdventure.Core;

// Standard songs are learned through their IsGet flags; the scarecrow melody is separate.
public static class HAL_SaveDisplayFields
{
    public sealed record Song(string Role, string Label, uint? Hash, bool EventOnly = false);
    public static readonly IReadOnlyList<Song> Songs = new Song[]
    {
        new("song_zelda_lullaby", "Zelda's Lullaby", 29491202),
        new("song_epona", "Epona's Song", 0xB492AC33),
        new("song_saria", "Saria's Song", 3556294604),
        new("song_sun", "Sun's Song", 0x165DD1AD),
        new("song_time", "Song of Time", 0x03B3450E),
        new("song_storms", "Song of Storms", 725212495),
        new("song_scarecrow", "Scarecrow's Song", null),
        new("song_forest_minuet", "Minuet of Forest", 3691645780),
        new("song_fire_bolero", "Bolero of Fire", 1225005753),
        new("song_water_serenade", "Serenade of Water", 1067858310),
        new("song_shadow_nocturne", "Nocturne of Shadow", 3271267828),
        new("song_spirit_requiem", "Requiem of Spirit", 2150250152),
        new("song_light_prelude", "Prelude of Light", 0xD6320BA4)
    };

    internal static void AddSongs(List<ItemState> items, Func<uint, int?> number, Func<uint, string?> text)
    {
        var ocarina = text(0xB72CD3FC);
        if (ocarina is "Item_Ocarina_Time" or "Item_Ocarina_Fairy")
        {
            string role = ocarina == "Item_Ocarina_Time" ? "ocarina_time" : "ocarina_fairy";
            int index = items.FindIndex(item => item.Role == role);
            if (index >= 0) items[index] = items[index] with { State = OwnershipState.Owned, Evidence = "Selected:0xB72CD3FC" };
            else items.Add(new ItemState(role, ocarina == "Item_Ocarina_Time" ? "Ocarina of Time" : "Fairy Ocarina",
                "Inventory", OwnershipState.Owned, true, "Selected:0xB72CD3FC"));
        }
        foreach (var song in Songs)
        {
            int? raw = song.Hash is uint hash ? number(hash) : null;
            var state = raw == 1 ? OwnershipState.Owned : raw == 0 && !song.EventOnly
                ? OwnershipState.NotCarried : OwnershipState.Unknown;
            var evidence = song.Hash is uint field ? $"{(song.EventOnly ? "GetEvent" : "IsGet")}:0x{field:X8}" : "Unmapped field";
            items.Add(new ItemState(song.Role, song.Label, "Songs", state, false, evidence));
        }
    }
}
