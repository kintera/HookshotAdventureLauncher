using System.Text;

namespace HookshotAdventure.Core;

/// <summary>Recolors reviewed texture blocks while preserving decoded alpha.</summary>
public static class HAL_TexturePatch
{
    public sealed record Result(byte[] Data, IReadOnlyList<int> ChangedBlocks, IReadOnlyList<int> AlphaSkipped, double? Reference);
    public static Result Apply(byte[] data, string kind, string color, string codec, string? alias = null,
        (double Left, double Top, double Right, double Bottom)? blade = null)
    {
        var texture = new HAL_TunicTexture(data); var result = (byte[])data.Clone();
        if (alias is not null)
        {
            byte[] name = Encoding.UTF8.GetBytes(alias);
            if (name.Length != texture.NameLength) throw new InvalidDataException("Texture alias must retain its original length.");
            name.CopyTo(result, texture.NameOffset + 2);
        }
        var changedCounts = new List<int>(); var skippedCounts = new List<int>(); double? reference = null;
        for (int level = 0; level < texture.MipOffsets.Count; level++)
        {
            int width = Math.Max(1, texture.Width >> level), height = Math.Max(1, texture.Height >> level);
            int bx = texture.BlockWidth, by = texture.BlockHeight, bw = (width + bx - 1) / bx, bh = (height + by - 1) / by;
            byte[] linear = texture.Linear(level);
            bool bc = texture.Format is 0x1a06 or 0x1c06, bc3 = texture.Format == 0x1c06;
            byte[] original = bc ? HAL_BcTextures.Decode(linear, width, height, bc3) : HAL_AstcCodec.Decode(codec, linear, width, height, bx, by);
            byte[] edited; bool[] pixelMask;
            if (blade is not null) (edited, pixelMask) = HAL_ColorTransforms.Blade(original, width, height, color, blade.Value);
            else
            {
                var transformed = HAL_ColorTransforms.Fabric(original, width, height, kind, color, reference);
                edited = transformed.Pixels; pixelMask = transformed.Mask; reference = transformed.Reference;
            }
            byte[] packed = bc ? HAL_BcTextures.Encode(edited, width, height, linear, bc3) : HAL_AstcCodec.Encode(codec, edited, width, height, bx, by);
            if (packed.Length != linear.Length) throw new InvalidDataException("Texture codec changed allocation size.");
            byte[] decoded = bc ? HAL_BcTextures.Decode(packed, width, height, bc3) : HAL_AstcCodec.Decode(codec, packed, width, height, bx, by);
            int changed = 0, skipped = 0;
            for (int y = 0; y < bh; y++) for (int x = 0; x < bw; x++)
            {
                bool selected = false, alphaChanged = false;
                for (int py = y * by; py < Math.Min(height, (y + 1) * by); py++)
                    for (int px = x * bx; px < Math.Min(width, (x + 1) * bx); px++)
                    {
                        int pixel = py * width + px; selected |= pixelMask[pixel]; alphaChanged |= original[pixel * 4 + 3] != decoded[pixel * 4 + 3];
                    }
                if (!selected) continue;
                if (alphaChanged) { skipped++; continue; }
                int index = y * bw + x; packed.AsSpan(index * texture.BlockBytes, texture.BlockBytes).CopyTo(result.AsSpan(texture.MipOffsets[level][index])); changed++;
            }
            changedCounts.Add(changed); skippedCounts.Add(skipped);
        }
        return new Result(result, changedCounts, skippedCounts, reference);
    }
}
