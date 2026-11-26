using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Meitou.Modding;

namespace LivingWasteland.MeitouMod;

// Native captions and settings use Meitou's catalogue. F10 command captions share the same source.
internal static class UiLanguage
{
    internal const string FileName = "ui_locales.json";
    static readonly PropertyInfo? language = typeof(ModContext).GetProperty("Language");
    static readonly MethodInfo? add = typeof(ModContext).GetMethod("AddTranslations",
        [typeof(string), typeof(IEnumerable<KeyValuePair<string, string>>), typeof(string)]);
    static readonly IReadOnlyDictionary<string, string> english = new Dictionary<string, string>();
    static IReadOnlyDictionary<string, string> words = english;
    internal static string Diagnostics { get; private set; } = "English fallback";

    internal static void Register(ModContext context)
    {
        words = english;
        Diagnostics = "English fallback";
        var path = Path.Combine(context.Folder, FileName);
        if (add is null || language is null || !File.Exists(path)) return;
        try
        {
            if (language.GetValue(context) is not string locale || locale.Length == 0) return;
            var translations = Read(File.ReadAllText(path), locale);
            if (translations.Count > 0 && add.Invoke(context, [locale, translations, null]) is true)
            {
                words = translations;
                Diagnostics = $"{locale}, {translations.Count} entries";
            }
        }
        catch (Exception error)
        {
            context.Log($"interface language: {error.GetBaseException().Message}");
        }
    }

    internal static string Text(string source) => words.GetValueOrDefault(source, source);

    internal static string Format(string source, params object?[] arguments)
    {
        string translated = Text(source);
        try { return string.Format(CultureInfo.CurrentCulture, translated, arguments); }
        catch (FormatException) when (translated != source)
        {
            return string.Format(CultureInfo.CurrentCulture, source, arguments);
        }
    }

    internal static Dictionary<string, string> Read(string json, string locale)
    {
        var tables = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string?>>>(json) ?? [];
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string code = Normalise(locale);
        string neutral = code.Split('_', 2)[0];
        Add(neutral);
        if (neutral != code) Add(code);
        return result;

        void Add(string key)
        {
            foreach (var (candidate, table) in tables)
            {
                if (Normalise(candidate) != key || table is null) continue;
                foreach (var (english, translated) in table)
                    if (!string.IsNullOrWhiteSpace(english) && !string.IsNullOrWhiteSpace(translated))
                        result[english] = translated;
            }
        }
    }

    static string Normalise(string code) => code.Replace('-', '_').ToLowerInvariant();
}
