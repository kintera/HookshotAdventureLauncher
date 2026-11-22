using System.Text;

namespace HookshotAdventure.Core;

/// <summary>Build the bitwise lookup tree used by BFEVFL entry dictionaries.</summary>
public static class HAL_EventFlowDictionary
{
    public readonly record struct Node(uint Bit, ushort Left, ushort Right);
    private sealed class MutableNode(byte[] key, int bit, int left, int right)
    {
        public byte[] Key = key;
        public int Bit = bit, Left = left, Right = right;
        public int Child(int side) => side == 0 ? Left : Right;
        public void SetChild(int side, int value) { if (side == 0) Left = value; else Right = value; }
    }

    private static int Bit(byte[] key, int index) => index < 0 || index / 8 >= key.Length
        ? 0 : (key[^(index / 8 + 1)] >> (index & 7)) & 1;

    public static IReadOnlyList<Node> Build(IReadOnlyList<string> names)
    {
        if (names.Count > ushort.MaxValue - 1) throw new InvalidDataException("Too many event dictionary entries.");
        var nodes = new List<MutableNode> { new(Array.Empty<byte>(), -1, 0, 0) };
        var utf8 = new UTF8Encoding(false, true);
        foreach (string name in names)
        {
            if (string.IsNullOrEmpty(name) || name.Contains('\0')) throw new InvalidDataException("Invalid event dictionary key.");
            byte[] key = utf8.GetBytes(name);
            int p = 0, x = nodes[0].Left;
            while (nodes[p].Bit < nodes[x].Bit) { p = x; x = nodes[x].Child(Bit(key, nodes[x].Bit)); }
            byte[] prior = nodes[x].Key;
            int difference = -1;
            for (int i = 0; i < Math.Max(key.Length, prior.Length) * 8; i++)
                if (Bit(key, i) != Bit(prior, i)) { difference = i; break; }
            if (difference < 0) throw new InvalidDataException("Duplicate event dictionary key.");
            p = 0; x = nodes[0].Left;
            while (nodes[p].Bit < nodes[x].Bit && nodes[x].Bit < difference)
            { p = x; x = nodes[x].Child(Bit(key, nodes[x].Bit)); }
            int id = nodes.Count, direction = Bit(key, difference);
            nodes.Add(direction == 0 ? new(key, difference, id, x) : new(key, difference, x, id));
            nodes[p].SetChild(Bit(key, nodes[p].Bit), id);
        }
        return nodes.Select(n => new Node(n.Bit < 0 ? uint.MaxValue : checked((uint)n.Bit),
            checked((ushort)n.Left), checked((ushort)n.Right))).ToArray();
    }
}
