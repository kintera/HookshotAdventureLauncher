using System.Text;

namespace HookshotAdventure.Core;

/// <summary>Set one BYML string field while retaining every existing string index.</summary>
public static class HAL_BymlStringEdit
{
    public static byte[] Replace(byte[] source, HAL_Byml.Field field, string value)
    {
        if (field.Type != 0xa0 || string.IsNullOrEmpty(value) || value.Contains('\0')) throw new InvalidDataException("Invalid BYML string edit.");
        var b = new HAL_Byml(source); _ = b.Text(field); var output = new List<byte>(source);
        void Align() { while ((output.Count & 3) != 0) output.Add(0); }
        void Put(int at, uint v, int size) { for (int i = 0; i < size; i++) output[at + (b.LittleEndian ? i : size - 1 - i)] = (byte)(v >> (i * 8)); }
        void Write(uint v, int size) { int at = output.Count; output.AddRange(new byte[size]); Put(at, v, size); }
        int index = b.Values.ToList().IndexOf(value);
        if (index < 0)
        {
            var values = b.Values.Append(value).ToArray(); index = values.Length - 1; Align(); int table = output.Count;
            output.Add(0xc2); Write((uint)values.Length, 3); var encoded = values.Select(Encoding.UTF8.GetBytes).ToArray();
            int offset = checked(4 + (encoded.Length + 1) * 4);
            foreach (var text in encoded) { Write((uint)offset, 4); offset = checked(offset + text.Length + 1); }
            Write((uint)offset, 4); foreach (var text in encoded) { output.AddRange(text); output.Add(0); }
            Put(8, (uint)table, 4);
        }
        Put(field.Slot, (uint)index, 4); return output.ToArray();
    }
}
