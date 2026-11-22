// Authored BYML pouch table; no game resources are included.
using System.Buffers.Binary;
using System.Text;
using HookshotAdventure.Core;

static class BombchuCapacityFixture
{
    public static void Check()
    {
        byte[] source = Create();
        byte[] patched = HAL_BombchuCapacity.Patch(source);
        var b = new HAL_Byml(patched);
        var rows = b.Array(new HAL_Byml.Field(0xc0, 12)).Select(row => b.Fields((int)b.Child(row, 0xc1))).ToArray();
        var chu = rows.Single(row => b.Text(row["__RowId"]) == "Item_Bombchu");
        var bomb = rows.Single(row => b.Text(row["__RowId"]) == "Item_Bomb");
        AssertTest.True(chu.ContainsKey("FixedMaxNum") && !chu.ContainsKey("MaxNumGameData") &&
            b.Child(chu["FixedMaxNum"], 0xd1) == 20 && b.Text(bomb["MaxNumGameData"]) == "Bomb",
            "bombchus have their own 20-item capacity without changing bomb capacity");
        AssertTest.True(!source.SequenceEqual(patched) && new HAL_Byml(source).Array(new HAL_Byml.Field(0xc0, 12))
            .Select(row => new HAL_Byml(source).Fields((int)new HAL_Byml(source).Child(row, 0xc1)))
            .Any(row => row.ContainsKey("MaxNumGameData")), "bombchu patch leaves its input unchanged");
        bool rejected = false;
        try { HAL_BombchuCapacity.Patch(patched); } catch (InvalidDataException) { rejected = true; }
        AssertTest.True(rejected, "bombchu patch refuses an unexpected or already edited pouch rule");
    }

    public static byte[] Create()
    {
        string[] keys = ["FixedMaxNum", "MaxNumGameData", "SmallBagActor", "__RowId"];
        string[] values = ["Bomb", "Item_Bomb", "Item_Bombchu", "Work/Actor/Item_BombBag_Small.engine__actor__ActorParam.gyml"];
        var bytes = new List<byte>(new byte[16]); bytes[0] = (byte)'Y'; bytes[1] = (byte)'B'; bytes[2] = 7;
        void Align() { while ((bytes.Count & 3) != 0) bytes.Add(0); }
        void Put(int at, uint value) { byte[] b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, value); for (int i = 0; i < 4; i++) bytes[at + i] = b[i]; }
        int Strings(string[] strings)
        {
            Align(); int node = bytes.Count; bytes.Add(0xc2); bytes.Add((byte)strings.Length); bytes.Add(0); bytes.Add(0);
            bytes.AddRange(new byte[(strings.Length + 1) * 4]);
            for (int i = 0; i < strings.Length; i++) { Put(node + 4 + i * 4, (uint)(bytes.Count - node)); bytes.AddRange(Encoding.UTF8.GetBytes(strings[i])); bytes.Add(0); }
            Put(node + 4 + strings.Length * 4, (uint)(bytes.Count - node)); return node;
        }
        int keyTable = Strings(keys), valueTable = Strings(values);
        int Row(string item)
        {
            Align(); int node = bytes.Count; bytes.AddRange(new byte[] { 0xc1, 3, 0, 0 });
            foreach (var (name, value) in new[] { ("MaxNumGameData", "Bomb"), ("SmallBagActor", values[3]), ("__RowId", item) })
            {
                int index = Array.IndexOf(keys, name); bytes.Add((byte)index); bytes.Add(0); bytes.Add(0); bytes.Add(0xa0);
                int at = bytes.Count; bytes.AddRange(new byte[4]); Put(at, (uint)Array.IndexOf(values, value));
            }
            return node;
        }
        int first = Row("Item_Bombchu"), second = Row("Item_Bomb");
        Align(); int root = bytes.Count; bytes.AddRange(new byte[] { 0xc0, 2, 0, 0, 0xc1, 0xc1, 0, 0 });
        int slot = bytes.Count; bytes.AddRange(new byte[8]); Put(slot, (uint)first); Put(slot + 4, (uint)second);
        Put(4, (uint)keyTable); Put(8, (uint)valueTable); Put(12, (uint)root);
        return bytes.ToArray();
    }
}
