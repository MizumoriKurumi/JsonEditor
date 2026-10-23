using System.Globalization;
using System.Resources;

namespace JsonEditor;

internal static class Localization
{
    private static readonly ResourceManager Resources =
        new("JsonEditor.Resources.Strings", typeof(Localization).Assembly);
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JsonEditor", "language.txt");

    public static CultureInfo Language { get; private set; } =
        CultureInfo.GetCultureInfo("zh-CN");

    public static string Get(string key) => Resources.GetString(key, Language) ?? key;
    public static string Format(string key, params object[] args) =>
        string.Format(Language, Get(key), args);

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string code = File.ReadAllText(SettingsPath).Trim();
                if (code is "zh-CN" or "en") SetLanguage(code);
            }
        }
        catch (IOException) { /* Keep the default when settings cannot be read. */ }
        catch (UnauthorizedAccessException) { }
    }

    public static void SetLanguage(string code)
    {
        if (code is not ("zh-CN" or "en"))
            throw new ArgumentException("Unsupported UI language.", nameof(code));
        // UI language is independent of number and date parsing culture.
        Language = CultureInfo.GetCultureInfo(code);
    }

    public static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, Language.Name);
    }
}
