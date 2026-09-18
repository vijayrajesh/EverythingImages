namespace EverythingImages.Core;

/// <summary>
/// Dominant-colour extraction and colour naming. Pure functions over
/// pixels: the scanner decodes the image at thumbnail size and passes them in.
/// </summary>
public static class Colors
{
    static readonly List<HexColor> Default = [new("#808080", 128, 128, 128, 100, "Gray")];

    /// <summary>
    /// Up to 10 distinct colours of a BGRA pixel buffer, most common first,
    /// plus the first one's name. Pixels are binned in steps of 16 per
    /// channel; bins closer than ~42 (RGB distance) to a chosen colour merge
    /// into it, so shades of one colour don't crowd out the rest.
    /// </summary>
    public static (List<HexColor> Colors, string Primary) Dominant(ReadOnlySpan<byte> bgra)
    {
        var counts = new Dictionary<int, int>();
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < 16) continue; // fully transparent pixels say nothing
            int b = Math.Min(bgra[i] / 16 * 16 + 8, 255);
            int g = Math.Min(bgra[i + 1] / 16 * 16 + 8, 255);
            int r = Math.Min(bgra[i + 2] / 16 * 16 + 8, 255);
            var key = (r << 16) | (g << 8) | b;
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        if (counts.Count == 0) return (Default, "Gray");

        const int thresholdSq = 42 * 42;
        var selected = new List<int[]>(); // r, g, b, count
        foreach (var (key, count) in counts.OrderByDescending(kv => kv.Value))
        {
            int r = (key >> 16) & 0xFF, g = (key >> 8) & 0xFF, b = key & 0xFF;
            var similar = selected.FirstOrDefault(s =>
            {
                int dr = r - s[0], dg = g - s[1], db = b - s[2];
                return dr * dr + dg * dg + db * db < thresholdSq;
            });
            if (similar != null)
            {
                similar[3] += count;
            }
            else
            {
                selected.Add([r, g, b, count]);
                if (selected.Count >= 10) break;
            }
        }
        selected.Sort((a, c) => c[3].CompareTo(a[3]));

        double counted = counts.Values.Sum();
        var colors = selected
            .Select(s => new HexColor($"#{s[0]:X2}{s[1]:X2}{s[2]:X2}", s[0], s[1], s[2],
                Math.Round(s[3] / counted * 1000) / 10, Classify(s[0], s[1], s[2])))
            .ToList();
        return (colors, colors[0].Name);
    }

    /// <summary>Colour family of one RGB value, by hue/saturation/value.</summary>
    public static string Classify(int r, int g, int b)
    {
        var (h, s, v) = RgbToHsv(r, g, b);
        if (v < 0.15) return "Black";
        if (s < 0.12 && v > 0.85) return "White";
        if (s < 0.15) return "Gray";
        if (!(h >= 20 && h <= 45) && s > 0.25 && v < 0.55 && r > g) return "Brown";
        if (h >= 345 || h < 15) return "Red";
        if (h < 45) return "Orange";
        if (h < 70) return "Yellow";
        if (h < 165) return "Green";
        if (h < 195) return "Cyan";
        if (h < 255) return "Blue";
        if (h < 290) return "Purple";
        if (h < 345) return "Pink";
        return "Gray";
    }

    static (double H, double S, double V) RgbToHsv(int r, int g, int b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf));
        double delta = max - min;
        // Which channel is max, decided on the integers to avoid float equality.
        int maxChannel = Math.Max(r, Math.Max(g, b));
        double h = 0;
        if (delta > 0)
        {
            if (maxChannel == r) h = 60 * ((((gf - bf) / delta) % 6 + 6) % 6);
            else if (maxChannel == g) h = 60 * (((bf - rf) / delta) + 2);
            else h = 60 * (((rf - gf) / delta) + 4);
        }
        double s = maxChannel == 0 ? 0 : delta / max;
        return (h, s, max);
    }
}
