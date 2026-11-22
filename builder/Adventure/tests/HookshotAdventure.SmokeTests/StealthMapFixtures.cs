// Synthetic BYML fixtures; no game data.
using HookshotAdventure.Core;
using System.IO.Compression;
using System.Security.Cryptography;
static class StealthMapFixtures
{
    public static void Check()
    {
        {
            using var input = new ZLibStream(new MemoryStream(Convert.FromBase64String("eJx9j71KA0EUhc9q/AEbCwULCztLVwURrJKQZIO6CgpiNQ5x3F2Z/WF3haQQLXyGYBnfJGMjdml8BUs70dKzZkUrBz7OnTvn3Muc1mYwD+CM3FvA4zSwwnqV2GSL7JAmOSDVTh6nGZpaeq4MFVo9GWo4MvPRzlzlyTyIIxymKlNRR3HeFLDM3BqpkUtyS/rFrCgIpRaNJI6kqPuBPv9pOdyhxIn0OKsV57FweumVVnWZ5VqJdla9yFUqjnKV2JvC3hCNbgI36dCmAyn2pY6jcuBv9/hPF/gYPfXfbx7WzQTAbw8oqAAj1jCTAO8DCsjLJ5XfMIVxDuPzX84qc7Y1zm1TX2cX356XrneLXOGplB6fb0MWxhgssN4jCTHW95rhHfULRAxg7g==")), CompressionMode.Decompress);
            using var output = new MemoryStream(); input.CopyTo(output);
            var patched = HAL_ChildStealth.PatchMap(output.ToArray(), true);
            AssertTest.True(Convert.ToHexString(SHA256.HashData(patched)) == "260EE50F0CDE30B7C707840AF350050ED32055F7C6A80692187E02FB7DE6F91C", "child stealth map matches Python (little, castle=True)");
            bool rejected = false;
            try { HAL_ChildStealth.PatchMap(patched, true); } catch (InvalidDataException) { rejected = true; }
            AssertTest.True(rejected, "child stealth map rejects altered input");
        }
        {
            using var input = new ZLibStream(new MemoryStream(Convert.FromBase64String("eJyFj79KA0EQxmdN/AM2FgoWFnaWngoiWF1CkgvqKSiI1bqc693J3u1xu4IpRAufIVjqm3g2YpfGV7C0Ey39kixo58CPmf12vhmmcTJNiDlwSsTun4mmUC+DFeCBTbAN2mDfj6wuDbWViEORSer0RKYoECahrgllLGyqczoopZF5JIkwbxK+JbAKGuAC3IK+n6eZULxV6FzwZpKqM3JSgB2SH4sYszraah70ykslm8JYJXnX+OdWlvzQysLb4N46b10VFBYR2lQq+J5QOncDf9WjPyrR2uPNZ/9l8FURTeDuBxrl+gCZQavhOdSQa2/I3zgDMiMXs//42NjHPOfb2rlefP1YmHkf+0Y9ddeTPKGoqmpomwe7oHDL8MXufgAjEWDu")), CompressionMode.Decompress);
            using var output = new MemoryStream(); input.CopyTo(output);
            var patched = HAL_ChildStealth.PatchMap(output.ToArray(), true);
            AssertTest.True(Convert.ToHexString(SHA256.HashData(patched)) == "775370EB44BDD42C43274F1A3668CF84D8036F995C3154B3CA747B9A71FDBC5D", "child stealth map matches Python (big, castle=True)");
            bool rejected = false;
            try { HAL_ChildStealth.PatchMap(patched, true); } catch (InvalidDataException) { rejected = true; }
            AssertTest.True(rejected, "child stealth map rejects altered input");
        }
        {
            using var input = new ZLibStream(new MemoryStream(Convert.FromBase64String("eJyNj7FOwzAURW9KgZWBkYGNkQgkhMTUVm1TCVIkkBCTsYJJgpw4io1ENwa+gbkfwQfUfAEL/8LIbRoEG1g6uu9d+T77Xfc3sQXghrwGwNsGsMt6j4TkiJyQEZmSXuJMbTHSMo1loTCeyUIjkjbDxMYqlS43Jc5rZVWZKM5bB3aY2yd9ck+eyMtyVpkXUothZUopBlmub7+tiG8ocSVTzhobZ0Q0qx+0GkjrtBIT27tzqhYXTlXhoQgPxPCxQlwlvKZzKc6kNmU78Me9/OUCXLU5vtPUcwq6wHvjrQHs5xSQj08q1/DLi51/5LptLgxWueOg6f/Moc1lba6iLmh477HN+pQ8E7/6/GJO/QKjd1ug")), CompressionMode.Decompress);
            using var output = new MemoryStream(); input.CopyTo(output);
            var patched = HAL_ChildStealth.PatchMap(output.ToArray(), false);
            AssertTest.True(Convert.ToHexString(SHA256.HashData(patched)) == "BD4DFF6F5CA90A5D19C7E0CE5B56D3FCA5E7E8290E6AD82D75CF74002A70F585", "child stealth map matches Python (little, castle=False)");
            bool rejected = false;
            try { HAL_ChildStealth.PatchMap(patched, false); } catch (InvalidDataException) { rejected = true; }
            AssertTest.True(rejected, "child stealth map rejects altered input");
        }
        {
            using var input = new ZLibStream(new MemoryStream(Convert.FromBase64String("eJyFj71OwzAURm+aQlcGRgY2RiKQEBJTWrVNJQhIIFVM5iqYJMiJo9hIdGPgGZh5CB6g4QlYeBdGPoP52Wrp6NqffK6vh5cDwtoAV0TByyvROvbbYAdE4AAcgQk4jTOrW0MTxXnKlaTpgitFCZuCZiaVOdtS13TWSiPrTBKh3xq8LbALhuAWPICnuC4rVmLc6JrFqCjVNfkowRtSzDlHr6m2WiSL9k7JERurpJiZ+MbKVpxb2UT7ItoT4/uG0ibDNVWyOGGla9/wL734l/6uoCPqoTzTV+2/uRBZiKPLUMN31A98A3Hw4/VWeP1vL4i8d+i9cIVH3iu81yyd03Vu1E1wDB79IEvX5xOJMVug")), CompressionMode.Decompress);
            using var output = new MemoryStream(); input.CopyTo(output);
            var patched = HAL_ChildStealth.PatchMap(output.ToArray(), false);
            AssertTest.True(Convert.ToHexString(SHA256.HashData(patched)) == "825D2FAC1F7000B8F5EB39FB04B141E6BFD5AF4CB22C078CC91C213312702D2D", "child stealth map matches Python (big, castle=False)");
            bool rejected = false;
            try { HAL_ChildStealth.PatchMap(patched, false); } catch (InvalidDataException) { rejected = true; }
            AssertTest.True(rejected, "child stealth map rejects altered input");
        }
    }
}
