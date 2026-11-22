namespace HookshotAdventure.Core;

/// <summary>Pixel-level clothing and sword color transforms.</summary>
public static class HAL_ColorTransforms
{
    private static (double H, double S, double V) Hsv(byte r, byte g, byte b)
    {
        double red = r / 255.0, green = g / 255.0, blue = b / 255.0;
        double max = Math.Max(red, Math.Max(green, blue)), min = Math.Min(red, Math.Min(green, blue)), d = max - min;
        if (d == 0) return (0, 0, max);
        double h = max == red ? (green - blue) / d : max == green ? 2 + (blue - red) / d : 4 + (red - green) / d;
        return ((h / 6 + 1) % 1, max == 0 ? 0 : d / max, max);
    }
    private static (byte R, byte G, byte B) Rgb(double h, double s, double v)
    {
        double sector = h * 6, f = sector - Math.Floor(sector); int i = (int)Math.Floor(sector) % 6;
        double p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        var (r, g, b) = i switch { 0 => (v,t,p), 1 => (q,v,p), 2 => (p,v,t), 3 => (p,q,v), 4 => (t,p,v), _ => (v,p,q) };
        return ((byte)Math.Clamp((int)Math.Round(r * 255), 0, 255), (byte)Math.Clamp((int)Math.Round(g * 255), 0, 255), (byte)Math.Clamp((int)Math.Round(b * 255), 0, 255));
    }
    private static (double H, double S, double V) Color(string color)
    {
        if (color.Length != 7 || color[0] != '#' || !int.TryParse(color.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out int rgb)) throw new ArgumentException("Invalid color.");
        return Hsv((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
    public static (byte[] Pixels, bool[] Mask, double Reference) Fabric(byte[] rgba, int width, int height, string kind, string color, double? reference = null)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 != rgba.Length) throw new InvalidDataException("Invalid image dimensions.");
        var target = Color(color); var weights = new double[width * height]; var values = new double[weights.Length];
        var selected = new List<double>();
        for (int i = 0; i < weights.Length; i++)
        {
            var hsv = Hsv(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2]);
            double hue = Math.Floor(hsv.H * 255) * (360.0 / 255), saturation = Math.Floor(hsv.S * 255) / 255, value = Math.Floor(hsv.V * 255) / 255;
            double distance = kind switch { "kokiri_tunic_color" => Math.Abs(hue - 112.5), "goron_tunic_color" => Math.Min(hue, 360 - hue),
                "zora_tunic_color" => Math.Abs(hue - 235), _ => throw new ArgumentException("Unknown fabric.") };
            double radius = kind == "kokiri_tunic_color" ? 47.5 : kind == "goron_tunic_color" ? 26 : 45;
            double weight = Math.Clamp((radius - distance) / 12, 0, 1) * Math.Clamp((saturation - .10) / .12, 0, 1);
            if (value <= .025 || rgba[i * 4 + 3] == 0) weight = 0;
            weights[i] = weight; values[i] = value; if (weight > .5) selected.Add(value);
        }
        if (reference is null)
        {
            if (selected.Count < Math.Max(1, width * height / 1000)) throw new InvalidDataException("Expected colored fabric not found.");
            selected.Sort(); int mid = selected.Count / 2;
            reference = selected.Count % 2 != 0 ? selected[mid] : (selected[mid - 1] + selected[mid]) / 2;
        }
        var result = (byte[])rgba.Clone(); var mask = new bool[weights.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            double weight = weights[i]; if (weight <= 0) continue; mask[i] = true;
            // Pillow stores HSV channels as bytes before converting back to RGB.
            double h = Math.Floor(target.H * 255) / 255, s = Math.Floor(target.S * 255) / 255;
            double v = Math.Floor(Math.Clamp(values[i] * target.V / Math.Max(reference.Value, .04), 0, 1) * 255) / 255;
            var rgb = Rgb(h, s, v);
            foreach (var (channel, replacement) in new[] { (0, rgb.R), (1, rgb.G), (2, rgb.B) })
                result[i * 4 + channel] = (byte)Math.Clamp((int)Math.Round(rgba[i * 4 + channel] * (1 - weight) + replacement * weight), 0, 255);
        }
        return (result, mask, reference.Value);
    }
    public static (byte[] Pixels, bool[] Mask) Blade(byte[] rgba, int width, int height, string color, (double Left, double Top, double Right, double Bottom) region)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 != rgba.Length) throw new InvalidDataException("Invalid image dimensions.");
        if (region.Left < 0 || region.Top < 0 || region.Right > 1 || region.Bottom > 1 || region.Left > region.Right || region.Top > region.Bottom)
            throw new ArgumentException("Invalid blade region.", nameof(region));
        var target = Color(color); var result = (byte[])rgba.Clone(); var mask = new bool[width * height];
        for (int y = (int)(height * region.Top); y < (int)(height * region.Bottom); y++)
            for (int x = (int)(width * region.Left); x < (int)(width * region.Right); x++)
            {
                int i = y * width + x; if (rgba[i * 4 + 3] == 0) continue;
                var hsv = Hsv(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2]);
                var rgb = Rgb(Math.Floor(target.H * 255) / 255, Math.Floor(target.S * 255) / 255, Math.Floor(hsv.V * 255) / 255);
                result[i * 4] = rgb.R; result[i * 4 + 1] = rgb.G; result[i * 4 + 2] = rgb.B; mask[i] = true;
            }
        return (result, mask);
    }
}
