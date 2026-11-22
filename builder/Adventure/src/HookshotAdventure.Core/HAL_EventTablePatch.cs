using System.Text;

namespace HookshotAdventure.Core;

public sealed record HAL_EventTableRow(string Id, string Template, string? EntryPoint);

/// <summary>Clone event table rows while retaining the original resource's other fields and children.</summary>
public static class HAL_EventTablePatch
{
    public static byte[] AddRows(byte[] source, IReadOnlyList<HAL_EventTableRow> additions)
    {
        var byml = new HAL_Byml(source);
        var existing = byml.Array(new HAL_Byml.Field(0xc0, 12));
        var rows = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var field in existing)
        {
            int node = checked((int)byml.Child(field, 0xc1));
            var fields = byml.Fields(node);
            if (!fields.TryGetValue("__RowId", out var id) || !rows.TryAdd(byml.Text(id), node))
                throw new InvalidDataException("Event table contains a missing or duplicate row ID.");
        }
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in additions)
        {
            if (string.IsNullOrEmpty(row.Id) || row.Id.Contains('\0') || rows.ContainsKey(row.Id) || !wanted.Add(row.Id) ||
                !rows.TryGetValue(row.Template, out int template))
                throw new InvalidDataException("Event table has an invalid, duplicate or missing row template.");
            var fields = byml.Fields(template);
            if (row.EntryPoint is not null && (!fields.TryGetValue("EntryPointName", out var entry) || entry.Type != 0xa0 ||
                string.IsNullOrEmpty(row.EntryPoint) || row.EntryPoint.Contains('\0')))
                throw new InvalidDataException("Event table template has no editable entry point.");
        }
        if (additions.Count == 0) return (byte[])source.Clone();
        var output = new List<byte>(source);
        void Align() { while ((output.Count & 3) != 0) output.Add(0); }
        void Put(int at, uint value, int size)
        {
            for (int i = 0; i < size; i++) output[at + (byml.LittleEndian ? i : size - 1 - i)] = (byte)(value >> (8 * i));
        }
        void Write(uint value, int size) { int at = output.Count; output.AddRange(new byte[size]); Put(at, value, size); }
        var needed = additions.SelectMany(r => r.EntryPoint is null ? new[] { r.Id } : new[] { r.Id, r.EntryPoint })
            .Where(s => !byml.Values.Contains(s)).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        if (needed.Length != 0)
        {
            var values = byml.Values.Concat(needed).ToArray(); Align(); int table = output.Count;
            output.Add(0xc2); Write((uint)values.Length, 3);
            var texts = values.Select(Encoding.UTF8.GetBytes).ToArray(); int offset = checked(4 + (values.Length + 1) * 4);
            foreach (var text in texts) { Write((uint)offset, 4); offset = checked(offset + text.Length + 1); }
            Write((uint)offset, 4);
            foreach (var text in texts) { output.AddRange(text); output.Add(0); }
            Put(8, (uint)table, 4); byml = new HAL_Byml(output.ToArray());
        }
        foreach (var row in additions)
        {
            int template = rows[row.Template], count = byml.Header(template, 0xc1);
            var fields = byml.Fields(template);
            byte[] copy = byml.Bytes(template, checked(4 + count * 8));
            void Replace(string fieldName, string value)
            {
                int index = -1;
                for (int i = 0; i < byml.Values.Count; i++)
                    if (byml.Values[i] == value) { if (index >= 0) throw new InvalidDataException("Duplicate BYML value."); index = i; }
                if (index < 0) throw new InvalidDataException("Event table value was not inserted.");
                int offset = fields[fieldName].Slot - template;
                for (int i = 0; i < 4; i++) copy[offset + (byml.LittleEndian ? i : 3 - i)] = (byte)((uint)index >> (8 * i));
            }
            Replace("__RowId", row.Id);
            if (row.EntryPoint is not null) Replace("EntryPointName", row.EntryPoint);
            Align(); rows.Add(row.Id, output.Count); output.AddRange(copy);
        }
        var sorted = rows.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
        Align(); int array = output.Count; output.Add(0xc0); Write((uint)sorted.Length, 3);
        foreach (var _ in sorted) output.Add(0xc1);
        Align(); foreach (var row in sorted) Write((uint)row.Value, 4);
        Put(12, (uint)array, 4);
        return output.ToArray();
    }
}
