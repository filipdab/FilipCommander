using System.Text.Json;

namespace FilipCommander;

/// <summary>Ustawienia zapisywane w %AppData%\FilipCommander\settings.json.</summary>
public sealed class Settings
{
    public string LeftPath { get; set; } = "";
    public string RightPath { get; set; } = "";
    public bool ShowHidden { get; set; }
    public string Editor { get; set; } = "notepad.exe";
    public bool RightActive { get; set; }
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowWidth { get; set; } = 1200;
    public int WindowHeight { get; set; } = 750;
    public bool Maximized { get; set; }

    public List<string> LeftTabs { get; set; } = new();
    public List<string> RightTabs { get; set; } = new();
    public int LeftTab { get; set; }
    public int RightTab { get; set; }
    public PanelView LeftView { get; set; }
    public PanelView RightView { get; set; }
    /// <summary>Ulubione katalogi (Ctrl+D).</summary>
    public List<string> Favorites { get; set; } = new();
    /// <summary>„system”, „light” albo „dark”.</summary>
    public string Theme { get; set; } = "system";
    public string FontName { get; set; } = "Segoe UI";
    public float FontSize { get; set; } = 9f;
    /// <summary>Prawy przycisk: menu kontekstowe zamiast zaznaczania.</summary>
    public bool RightClickMenu { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FilipCommander", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            // uszkodzony plik ustawień — startujemy z domyślnymi
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // brak możliwości zapisu nie powinien blokować zamknięcia programu
        }
    }
}
