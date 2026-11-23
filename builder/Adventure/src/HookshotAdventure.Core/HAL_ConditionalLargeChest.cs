namespace HookshotAdventure.Core;

/// <summary>Compose a private conditional actor that inherits the original large chest's appearance.</summary>
public static class HAL_ConditionalLargeChest
{
    public const string Regular = "Obj_TBox_HAL_LargeAmmo";
    public const string NoBake = "Obj_TBox_HAL_LargeAmmo_NoBake";
    public const string Conditional = "Obj_TBox_A_06";
    public static string Pack(string actor) => "Pack/Actor/" + actor + ".pack.zs";
    private static string Actor(string actor) => "Actor/" + actor + ".engine__actor__ActorParam.bgyml";

    public static string Alias(string actor) => actor switch
    {
        "Obj_TBox_A_02" => Regular,
        "Obj_TBox_A_02_NoBake" => NoBake,
        _ => throw new InvalidDataException("Unsupported large chest actor.")
    };

    public static byte[] Build(byte[] largeArchive, byte[] conditionalArchive, string actor)
    {
        string alias = Alias(actor);
        var large = HAL_ChestEvidence.SarcFiles(largeArchive);
        var conditional = HAL_ChestEvidence.SarcFiles(conditionalArchive);
        if (!large.ContainsKey(Actor(actor)) || !conditional.TryGetValue(Actor(Conditional), out var sourceActor) ||
            large.ContainsKey(Actor(alias)) || conditional.ContainsKey(Actor(alias)))
            throw new InvalidDataException("Large or conditional actor archive differs from the expected form.");
        var files = large.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        foreach (var (name, bytes) in conditional)
        {
            if (files.TryGetValue(name, out var existing) && !existing.SequenceEqual(bytes))
                throw new InvalidDataException("Actor archives contain conflicting dependencies.");
            files[name] = bytes;
        }
        var byml = new HAL_Byml(sourceActor); var fields = byml.Fields(byml.Root);
        if (!fields.TryGetValue("$parent", out var parent) || byml.Text(parent) != "Work/Actor/Obj_TBox_A_01.engine__actor__ActorParam.gyml")
            throw new InvalidDataException("Conditional actor has an unexpected parent.");
        files.Add(Actor(alias), HAL_BymlStringEdit.Replace(sourceActor, parent, "Work/Actor/" + actor + ".engine__actor__ActorParam.gyml"));
        return HAL_ActorArchive.Write(files);
    }
}
