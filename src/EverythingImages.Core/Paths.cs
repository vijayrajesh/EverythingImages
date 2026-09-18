namespace EverythingImages.Core;

public static class Paths
{
    /// <summary>Set by tests to keep them away from the real index and settings.</summary>
    public static string? AppDataOverride { get; set; }

    static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>A portable copy (release.bat puts portable.txt beside the
    /// exe) keeps everything in a data folder next to itself, so the whole
    /// folder can move to another PC or a USB drive.</summary>
    public static bool IsPortable => File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "portable.txt"));

    static string PortableDir => System.IO.Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>This app's own files (index, settings). EVERYTHINGIMAGES_DATA_DIR
    /// redirects it (screenshots on a test library).</summary>
    public static string AppDataDir()
    {
        var dir = AppDataOverride
                  ?? Environment.GetEnvironmentVariable("EVERYTHINGIMAGES_DATA_DIR")
                  ?? (IsPortable ? PortableDir : System.IO.Path.Combine(AppData, "EverythingImages"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Downloaded models. Normally beside the index and settings; a
    /// portable copy keeps them in data\shared. EVERYTHINGIMAGES_DATA_DIR
    /// redirects them along with everything else (tests' AppDataOverride does
    /// not, so the end-to-end test can use models already downloaded).</summary>
    public static string SharedDataDir()
    {
        var dir = Environment.GetEnvironmentVariable("EVERYTHINGIMAGES_DATA_DIR")
                  ?? (IsPortable ? System.IO.Path.Combine(PortableDir, "shared") : System.IO.Path.Combine(AppData, "EverythingImages"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>For a portable copy: this PC's usual EverythingImages folder, if
    /// it has one, so models already downloaded here are copied instead of
    /// downloaded again. Null otherwise.</summary>
    public static string? PcSharedDataDir()
    {
        if (!IsPortable || Environment.GetEnvironmentVariable("EVERYTHINGIMAGES_DATA_DIR") != null) return null;
        var dir = System.IO.Path.Combine(AppData, "EverythingImages");
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>The AI engine that ships beside the exe: runs the models on the GPU or the CPU.</summary>
    public static string EngineDir() => System.IO.Path.Combine(AppContext.BaseDirectory, "ai-engine");
}
