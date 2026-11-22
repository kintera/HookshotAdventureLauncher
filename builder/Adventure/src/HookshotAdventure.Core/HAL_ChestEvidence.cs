using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace HookshotAdventure.Core;

/// <summary>Read-only evidence for research into chest actor defaults and event names.</summary>
public static class HAL_ChestEvidence
{
    public sealed record Default(string Parameter, string Value, string Member, string? Parent);
    public sealed record Event(string Resource, string Match, IReadOnlyList<string> RelatedStrings);
    public sealed record Placement(string Resource, ulong Id, string Actor, string? Reward, string? Event);
    public static IReadOnlyDictionary<string, byte[]> SarcFiles(byte[] data)
    {
        ReadOnlySpan<byte> Span(int at, int length)
        {
            if (at < 0 || length < 0 || (long)at + length > data.Length) throw new InvalidDataException("SARC field outside archive.");
            return data.AsSpan(at, length);
        }
        if (!Span(0,4).SequenceEqual("SARC"u8) || data.Length < 0x20) throw new InvalidDataException("Invalid SARC header.");
        bool little = Span(6,2).SequenceEqual(new byte[] { 255,254 });
        if (!little && !Span(6,2).SequenceEqual(new byte[] { 254,255 })) throw new InvalidDataException("Invalid SARC byte order.");
        int U16(int at) => little ? BinaryPrimitives.ReadUInt16LittleEndian(Span(at,2)) : BinaryPrimitives.ReadUInt16BigEndian(Span(at,2));
        uint U32(int at) => little ? BinaryPrimitives.ReadUInt32LittleEndian(Span(at,4)) : BinaryPrimitives.ReadUInt32BigEndian(Span(at,4));
        int header = U16(4), start = checked((int)U32(12));
        if (!Span(header,4).SequenceEqual("SFAT"u8)) throw new InvalidDataException("Missing SFAT.");
        int count = U16(header+6), nodes = checked(header + U16(header+4)), namesHeader = checked(nodes + count*16);
        if (!Span(namesHeader,4).SequenceEqual("SFNT"u8)) throw new InvalidDataException("Missing SFNT.");
        int names = checked(namesHeader + U16(namesHeader+4)); var result = new Dictionary<string,byte[]>(StringComparer.Ordinal);
        for (int i=0;i<count;i++)
        {
            int at=checked(nodes+i*16); uint attribute=U32(at+4);
            if ((attribute >> 24) != 1) throw new InvalidDataException("Unnamed SARC member.");
            int nameAt=checked(names+(int)(attribute & 0xffffff)*4), begin=checked(start+(int)U32(at+8)), end=checked(start+(int)U32(at+12));
            if (nameAt < names || nameAt >= start || start > data.Length || begin < start || end < begin || end > data.Length)
                throw new InvalidDataException("SARC member outside archive.");
            int terminator = Array.IndexOf(data,(byte)0,nameAt,start-nameAt);
            if (terminator <= nameAt) throw new InvalidDataException("Invalid SARC member name.");
            string name = new UTF8Encoding(false,true).GetString(Span(nameAt,terminator-nameAt));
            if (!result.TryAdd(name,Span(begin,end-begin).ToArray())) throw new InvalidDataException("Duplicate SARC member.");
        }
        return result;
    }
    public static IReadOnlyList<Default> DefaultEvidence(IReadOnlyDictionary<string,byte[]> files)
    {
        var rows = new List<Default>(); var keys = new HashSet<string>(StringComparer.Ordinal) { "DefaultDropActorName", "Drop__DropActor", "Drop__DropTable", "GetEventName" };
        foreach (var (name,data) in files.OrderBy(x => x.Key,StringComparer.Ordinal))
        {
            if (!name.Contains("/BlackboardParamTable/",StringComparison.Ordinal) || !name.EndsWith(".bgyml",StringComparison.Ordinal)) continue;
            var b = new HAL_Byml(data); var root = b.Fields(b.Root);
            if (!root.TryGetValue("BlackboardParamStringArray",out var array)) continue;
            string? parent = root.TryGetValue("$parent",out var p) ? b.Text(p) : null;
            foreach (var field in b.Array(array))
            {
                var entry = b.Fields(checked((int)b.Child(field,0xc1))); string key = b.Text(entry["BBKey"]);
                if (keys.Contains(key)) rows.Add(new Default(key,b.Text(entry["InitValConverted"]),name,parent));
            }
        }
        return rows;
    }
    public static IReadOnlySet<string> Strings(byte[] data) => Regex.Matches(Encoding.Latin1.GetString(data), "[A-Za-z_][A-Za-z_0-9./:-]{3,}")
        .Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
    public static IReadOnlyList<Event> EventEvidence(string eventName, IReadOnlyDictionary<string,IReadOnlySet<string>> flows)
    {
        var found = new List<Event>(); string[] words = ["pouch","equip","sword","treasurebox","gamedata","weapon"];
        foreach (var (name,tokens) in flows.OrderBy(x=>x.Key,StringComparer.Ordinal))
        {
            string stem = name.Split('/')[^1]; if (stem.EndsWith(".bfevfl.zs",StringComparison.Ordinal)) stem = stem[..^10];
            string? match = tokens.Contains(eventName) ? "exact_string" :
                eventName.StartsWith(stem+"_",StringComparison.Ordinal) && tokens.Contains(eventName[(stem.Length+1)..]) ? "file_and_entry_candidate" : null;
            if (match is not null) found.Add(new Event(name,match,tokens.Where(token => words.Any(word=>token.Contains(word,StringComparison.OrdinalIgnoreCase)))
                .OrderBy(token=>token,StringComparer.Ordinal).ToArray()));
        }
        return found;
    }
    public static JsonObject Audit(string romfs, IReadOnlyList<Placement> placements, IReadOnlyList<byte[]> dictionaries)
    {
        var actors = new JsonObject(); var errors = new JsonArray();
        foreach (string name in placements.Select(x=>x.Actor).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal))
        {
            string resource = $"Pack/Actor/{name}.pack.zs"; IReadOnlyList<Default> evidence = [];
            try
            {
                if (name.Contains('/') || name.Contains('\\') || name is "." or "..") throw new InvalidDataException("Invalid actor name.");
                byte[] data = HAL_Zstd.Unpack(File.ReadAllBytes(Path.Combine(romfs,resource.Replace('/',Path.DirectorySeparatorChar))),dictionaries);
                evidence = DefaultEvidence(SarcFiles(data));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException or KeyNotFoundException or OverflowException)
            { errors.Add(new JsonObject { ["resource"] = resource, ["error"] = ex.Message }); }
            actors[name] = new JsonObject { ["resource"] = resource, ["evidence"] = System.Text.Json.JsonSerializer.SerializeToNode(evidence.Select(x=>new
                { parameter=x.Parameter, value=x.Value, member=x.Member, parent=x.Parent }).ToArray()) };
        }
        var flows = new Dictionary<string,IReadOnlySet<string>>(StringComparer.Ordinal); string directory = Path.Combine(romfs,"Event","EventFlow");
        if (Directory.Exists(directory)) foreach (string path in Directory.EnumerateFiles(directory,"*.bfevfl.zs").OrderBy(x=>x,StringComparer.Ordinal))
        {
            string resource = Path.GetRelativePath(romfs,path).Replace('\\','/');
            try { flows[resource] = Strings(HAL_Zstd.Unpack(File.ReadAllBytes(path),dictionaries)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OverflowException)
            { errors.Add(new JsonObject { ["resource"] = resource, ["error"] = ex.Message }); }
        }
        var events = new JsonObject();
        foreach (string name in placements.Select(x=>x.Event).Where(x=>!string.IsNullOrEmpty(x)).Select(x=>x!).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal))
            events[name] = System.Text.Json.JsonSerializer.SerializeToNode(EventEvidence(name,flows).Select(x=>new { resource=x.Resource, match=x.Match, related_strings=x.RelatedStrings }).ToArray());
        var missing = new JsonArray();
        foreach (var row in placements.Where(x=>x.Reward is null))
        {
            var candidates = actors[row.Actor]!["evidence"]!.AsArray().Where(x=>x!["parameter"]!.GetValue<string>() is "DefaultDropActorName" or "Drop__DropActor")
                .Select(x=>x!["value"]!.GetValue<string>()).Where(x=>x.Length != 0).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            missing.Add(new JsonObject { ["resource"] = row.Resource, ["placement"] = row.Id, ["actor"] = row.Actor,
                ["candidate_rewards"] = System.Text.Json.JsonSerializer.SerializeToNode(candidates), ["status"] = "requires_inheritance_and_runtime_confirmation" });
        }
        return new JsonObject { ["schema_version"] = 1, ["status"] = "static_evidence_only", ["actor_defaults"] = actors,
            ["custom_events"] = events, ["missing_overrides"] = missing, ["errors"] = errors, ["event_flows_scanned"] = flows.Count };
    }
}
