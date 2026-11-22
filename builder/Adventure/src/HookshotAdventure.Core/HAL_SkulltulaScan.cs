namespace HookshotAdventure.Core;

/// <summary>Discovers candidate gold Skulltula actors; access and variant identity require review.</summary>
public static class HAL_SkulltulaScan
{
    public sealed record Candidate(string Resource, string Placement, IReadOnlyList<float>? Position);
    public sealed record Result(int Maps, IReadOnlyList<Candidate> Candidates);

    public static IReadOnlyList<Candidate> Inspect(string resource, byte[] data)
    {
        var map=new HAL_Byml(data); var root=map.Fields(map.Root);
        if (!root.TryGetValue("Actors",out var actors)) return [];
        var result=new List<Candidate>();
        foreach(var row in map.Array(actors))
        {
            var actor=map.Fields(checked((int)map.Child(row,0xc1)));
            if (!actor.TryGetValue("Gyaml",out var gyaml) || map.Text(gyaml)!="Enemy_Skulltula_Gold") continue;
            if (!actor.TryGetValue("Hash",out var hash)) throw new InvalidDataException("Gold Skulltula placement has no identity.");
            ulong id=map.UInt(checked((int)map.Child(hash,0xd5)),8);
            float[]? position=null;
            if (actor.TryGetValue("Translate",out var translate))
            {
                var coordinates=map.Array(translate); position=new float[coordinates.Count];
                for(int i=0;i<coordinates.Count;i++)
                {
                    if(coordinates[i].Type!=0xd2) throw new InvalidDataException("Unexpected Skulltula position.");
                    position[i]=BitConverter.Int32BitsToSingle(unchecked((int)map.UInt(coordinates[i].Slot)));
                }
            }
            result.Add(new Candidate(resource,id.ToString("X16"),position));
        }
        if (result.Select(x=>x.Placement).Distinct(StringComparer.Ordinal).Count()!=result.Count)
            throw new InvalidDataException("Duplicate Skulltula placement in a map.");
        return result;
    }

    public static Result Scan(string romfs)
    {
        romfs=Path.GetFullPath(romfs); var dictionaries=HAL_RuntimeOverlay.Dictionaries(romfs);
        var files=HAL_ChestScope.Files(romfs,"full_game"); var rows=new List<Candidate>();
        foreach(string file in files)
        {
            string name=Path.GetRelativePath(romfs,file).Replace('\\','/');
            rows.AddRange(Inspect(name,HAL_Zstd.Unpack(File.ReadAllBytes(file),dictionaries)));
        }
        return new Result(files.Count,rows);
    }
}
