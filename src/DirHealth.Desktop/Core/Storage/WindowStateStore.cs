using System.IO;
using System.Text.Json;

namespace DirHealth.Desktop.Core.Storage;

public class WindowStateData
{
    public double Left       { get; set; } = double.NaN;
    public double Top        { get; set; } = double.NaN;
    public double Width      { get; set; } = 1100;
    public double Height     { get; set; } = 700;
    public bool   Maximized  { get; set; } = false;
}

public class WindowStateStore
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DirHealth", "window-state.json");

    public WindowStateData Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<WindowStateData>(File.ReadAllText(Path))
                       ?? new WindowStateData();
        }
        catch { }
        return new WindowStateData();
    }

    public void Save(WindowStateData data)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(data));
        }
        catch { }
    }
}
