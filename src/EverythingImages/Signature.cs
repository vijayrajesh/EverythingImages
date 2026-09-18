// The signature block these tools carry in their About box.
//
// Deliberately in the global namespace and dependency-free, so any C# project can
// drop it in - either by copying the file, or by pointing at this one:
//
//   <Compile Include="../../About/Signature.cs" Link="Signature.cs" />
//
// Keep the wording identical everywhere. If it changes, it changes here first.

using System.Drawing.Text;

public static class Signature
{
    // The Tamil is escaped rather than written literally so the file survives whatever
    // encoding it passes through - the same way the Rust and C versions spell it.

    /// <summary>Thiruchitrambalam.</summary>
    public const string TamilFirst =
        "\u0BA4\u0BBF\u0BB0\u0BC1\u0B9A\u0BCD\u0B9A\u0BBF\u0BB1\u0BCD\u0BB1\u0BAE\u0BCD\u0BAA\u0BB2\u0BAE\u0BCD";

    /// <summary>Anbe Sivam.</summary>
    public const string TamilSecond =
        "\u0B85\u0BA9\u0BCD\u0BAA\u0BC7 \u0B9A\u0BBF\u0BB5\u0BAE\u0BCD";

    public const string Meaning = "\"The Sacred Stage  -  Love is God\"";
    public const string Credit = "Developed by Rajeshkannan MJ with AI";
    public const string Site = "pixelthemes.com";
    public const string SiteUrl = "https://pixelthemes.com/";

    /// <summary>Both Tamil lines, ready to drop into a centred label.</summary>
    public static string Tamil => TamilFirst + "\r\n" + TamilSecond;

    /// <summary>
    /// Segoe UI carries no Tamil glyphs. Windows usually substitutes one, but naming a
    /// font that really has them keeps the shaping off whatever the fallback happens to be.
    /// </summary>
    public static string TamilFontFamily()
    {
        using var installed = new InstalledFontCollection();
        foreach (var candidate in new[] { "Nirmala UI", "Latha", "Segoe UI" })
            foreach (var family in installed.Families)
                if (string.Equals(family.Name, candidate, StringComparison.OrdinalIgnoreCase))
                    return candidate;
        return "Segoe UI";
    }
}
