// Authored fixtures from the Python event-flow tests; no game data.
using HookshotAdventure.Core;
using System.IO.Compression;
using System.Security.Cryptography;

static class FieldFixtures
{
    public static void Check()
    {
        {
            var compressed = Convert.FromBase64String("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");
            using var input = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var output = new MemoryStream(); input.CopyTo(output);
            var original = output.ToArray();
            var patched = HAL_FieldSkips.PatchFieldEntrance(original);
            AssertTest.True(Convert.ToHexString(SHA256.HashData(patched)) == "822DB2D6A0F5BD9DD6598D85848C4FDA3636068BFAF84E254FC05A30F0362AC3" && !original.SequenceEqual(patched),
                "PatchFieldEntrance matches Python on an authored fixture");
            bool rejected = false;
            try { HAL_FieldSkips.PatchFieldEntrance(patched); } catch (InvalidDataException) { rejected = true; }
            AssertTest.True(rejected, "PatchFieldEntrance rejects an altered flow");
        }
        {
            var compressed = Convert.FromBase64String("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");
            using var input = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var output = new MemoryStream(); input.CopyTo(output);
            var original = output.ToArray();
            var patched = HAL_FieldSkips.PatchFirstOwl(original);
            AssertTest.True(Convert.ToHexString(SHA256.HashData(patched)) == "80CA8289631977C7C47AB7C66ABDB1C8720D720AD3F22934490DED23E22258F0" && !original.SequenceEqual(patched),
                "PatchFirstOwl matches Python on an authored fixture");
            bool rejected = false;
            try { HAL_FieldSkips.PatchFirstOwl(patched); } catch (InvalidDataException) { rejected = true; }
            AssertTest.True(rejected, "PatchFirstOwl rejects an altered flow");
        }
        {
            var compressed = Convert.FromBase64String("eJylmV1oFFcYhk9+tNY/rBiNMRURKlHEbOZ/RCTqJpiaxtDEKF64rMmwXTKZTScTcVEk/ottqVQJqFSlWBAvBIsUKWJBeuGFCC2leOGFmKaieNGL4kUvjOvmzLvZjbvJix/sznfme855vrPLzMLs5uamruZWIUSlEGOvxaQok8fGyaVsDBU5P92QdkRl9j0J75mq/EaeyONVef5plXivKF/87vNVRc6Hfa0uUrenmLenSN2dYt5QkfrZKeb9jPrY2LvJ/PiV5O+D78o6fy/STxgHlnDrHyP5b0h+mOQvk/w1kr85gS+fBv8Luf5vJP+Q5B+R/FOSf0nyr0heVHP8bJJfRPLLSb6O5BWS30DyzSTfTvK7Sb6H5D2S30/yR0j+a5IfJvkrJH+d5G+R/F2Sv0/yf5D8Y5L/h+T/Jfn/JV8+Tb5yKbf+fJKvJvmVJL+W5A2SbyT5T0m+k+T3kPwXJO+T/EGSP0HyZ0j+AslfJfkbJH+b5O+R/AOS/4vkn5D8C5L/j+Rfk/ysGo5fSPK1JL+K5CMkv57koyTfRvK7SH4vybskv4/kh0j+NMmfI/lLJH+N5H8q4O/UFGffxkty/VckL5Zx/GySX0Tyy0m+juQVkt9A8s0k307yu0m+h+Q9kk+T/DGS/xb8GlGReb+wrASciVu13PofiWhfbKvvbokPBK4Ti2RCRFu2rCgrMefPgmegc0STF/jp9lTSCyIlbYnMa4Fo2ud4QUd6IHD6NnUHKV98LB/ifThe2hlPBmKe2NEykdkkmZpxptNPJhKOv73f8VpT3fEgmfJ2tGQ6aevvjm2LO/0pPy6C/GU7425vxt6RGvR6Jq58UWLVsrG39c+dLwedgSA62N2b9BJi5ngpEiYNYaKIMvno87lcpU4+AJrqQ1y6JH88V4T7aIv3OWJGAd4ocUvsTPm99SFbvzXtD7rO+Le3LpGZGYu5shaLhdS6RLrPDVtWw0QLEz1MjDAxw8QKE1t8ILceQdaATEGmItOQ6cgMZCYyCxkcChwKHAocChwKHAocChwKHAocChwqHCocKhwqHCocKhwqHCocKhwqHBocGhwaHBocGhwaHBocGhwaHBocOhw6HDocOhw6HDocOhw6HDocOhwGHAYcBhwGHAYcBhwGHAYcBhwGHCYcJhwmHCYcJhwmHCYcJhwmHCYcFhwWHBYcFhwWHBYcloH/OB7U5C7xUne7cCZ6stCThZ5s9GSjJxs92ejJRk82erKxbxsOGw7bFrPCazaSSxtyqZJL1czuKrJd35O/MBvlL8kheTxfm9t1RYldL6zNH68oGNcXjOcIeZfN3vfmThwp+UN10l2xS671iYh+tr7djacdv6Pp79tfjRz+rm701PejFy89u3549PiPI0dPjhz9YfWk+cO5XgpLN3MlfEiaeAOkQ6F4");
            using var input = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var output = new MemoryStream(); input.CopyTo(output);
            var original = output.ToArray();
            var patched = HAL_FieldSkips.PatchHyruleCastle(original);
            AssertTest.True(Convert.ToHexString(SHA256.HashData(patched)) == "22796A5F5570670DE8001EBE63D5DFE3ADFB7D42D8F1E86CDE1F76F911D4BAA6" && !original.SequenceEqual(patched),
                "PatchHyruleCastle matches Python on an authored fixture");
            bool rejected = false;
            try { HAL_FieldSkips.PatchHyruleCastle(patched); } catch (InvalidDataException) { rejected = true; }
            AssertTest.True(rejected, "PatchHyruleCastle rejects an altered flow");
        }
        {
            var compressed = Convert.FromBase64String("eJxzcnMNc/NhYGBgYWD4/48BAzBCaQdMKTBowCFOLIDaDgdMYFIIbi+DAJpDmKH6hCB0tRBl9gsLYBdXwyEOc4YnDvlIAvpGwSgYHOD/f1CarMORXkfBKMAPICW1tuAAO2MUjIJRMApGwSgYBaNgFJAEBBhccuO9E7MTizKz8+MNgAAoFJxfmpcSXFlckprrmFySX8SgB+0lSDC4lqXmlYDlg1ILS1OLS1xKk7Mz89IZeBlCPZG1JEK1SEG0hBRlpqenFvkXpOb55CcnlmTm54V6AnvFkH7xNKjiq1DaxdNZAV+P+Rhar4WHAWaoX2JuKgMrmvK3UOXWDOH5Rdn6MLX6MH+HZebkJKan6qUDNcfH50Cl4+NhCvXSK3NzgE6FjD0EQ5u8G2FNX+gQhJYQwvHMeBy/GK3J/BGNL402pMHNAA1jrH47CdWtyuDiaxWQk1iZWhTs+mRH9+PGSRpPO+Y+nT3v+arGp61LHze3P25epAkMKiTTjDCMk0BYjqLSGEOlE0IloejKFYL5xDWvpKgyID8zr8QAj3oGBpCnAOomfmY=");
            using var input = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var output = new MemoryStream(); input.CopyTo(output);
            var original = output.ToArray();
            var patched = HAL_FieldSkips.PatchKakariko(original);
            AssertTest.True(Convert.ToHexString(SHA256.HashData(patched)) == "3624A8B50102059242CCE777918065FA786428200BF0017E4A50277924E7A2FA" && !original.SequenceEqual(patched),
                "PatchKakariko matches Python on an authored fixture");
            bool rejected = false;
            try { HAL_FieldSkips.PatchKakariko(patched); } catch (InvalidDataException) { rejected = true; }
            AssertTest.True(rejected, "PatchKakariko rejects an altered flow");
        }
    }
}
