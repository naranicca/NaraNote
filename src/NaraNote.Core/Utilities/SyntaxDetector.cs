using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace NaraNote.Core.Utilities;

public sealed record SyntaxDetectionResult(string Language, double Confidence);

public static partial class SyntaxDetector
{
    public static SyntaxDetectionResult? Detect(string text, string? fileName = null)
    {
        if (!string.IsNullOrWhiteSpace(fileName) && DetectExtension(Path.GetExtension(fileName)) is { } extensionLanguage)
            return new(extensionLanguage, 1);
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 8) return null;

        var trimmed = text.Trim();
        if ((trimmed.StartsWith('{') || trimmed.StartsWith('[')) && IsJson(trimmed)) return new("Json", 1);
        if (trimmed.StartsWith('<') && IsXml(trimmed))
            return new(HtmlRootRegex().IsMatch(trimmed) ? "Html" : "Xml", .98);

        var scores = new Dictionary<string, int>
        {
            ["CSharp"] = Score(text, CSharpPatterns), ["Cpp"] = Score(text, CppPatterns),
            ["Python"] = Score(text, PythonPatterns), ["Lua"] = Score(text, LuaPatterns),
            ["Html"] = Score(text, HtmlPatterns), ["JavaScript"] = Score(text, JavaScriptPatterns),
            ["Css"] = Score(text, CssPatterns), ["Markdown"] = Score(text, MarkdownPatterns),
            ["PowerShell"] = Score(text, PowerShellPatterns)
        };
        var best = scores.OrderByDescending(pair => pair.Value).First();
        var runnerUp = scores.Values.OrderByDescending(value => value).Skip(1).First();
        if (best.Value > 0 && runnerUp == 0)
            return new(best.Key, Math.Min(.95, .78 + (best.Value - 1) * .06));
        if (best.Value < 3 || best.Value - runnerUp < 2) return null;
        return new(best.Key, Math.Min(.95, .55 + best.Value * .08));
    }

    private static readonly (string Extension, string Language)[] ExtensionLanguages =
    [
        (".cs", "CSharp"), (".c", "Cpp"), (".cc", "Cpp"), (".cpp", "Cpp"), (".cxx", "Cpp"),
        (".h", "Cpp"), (".hpp", "Cpp"), (".py", "Python"), (".pyw", "Python"), (".lua", "Lua"),
        (".json", "Json"), (".xml", "Xml"), (".html", "Html"), (".htm", "Html"),
        (".js", "JavaScript"), (".mjs", "JavaScript"), (".css", "Css"),
        (".md", "Markdown"), (".markdown", "Markdown"), (".ps1", "PowerShell"), (".psm1", "PowerShell")
    ];

    private static string? DetectExtension(string extension) => ExtensionLanguages
        .FirstOrDefault(item => string.Equals(item.Extension, extension, StringComparison.OrdinalIgnoreCase)).Language;
    private static bool IsJson(string text) { try { using var _ = JsonDocument.Parse(text); return true; } catch (JsonException) { return false; } }
    private static bool IsXml(string text) { try { _ = XDocument.Parse(text); return true; } catch { return false; } }
    private static int Score(string text, Regex[] patterns) => patterns.Count(pattern => pattern.IsMatch(text));
    private static Regex R(string pattern) => new(pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex[] CSharpPatterns = [R(@"^\s*using\s+[\w.]+\s*;"), R(@"\bnamespace\s+\w+"), R(@"\b(public|private|internal)\s+(sealed\s+)?(class|record|interface)\b"), R(@"\bConsole\.Write(Line)?\s*\("), R(@"\b(string|int|bool|var)\s+\w+\s*[=;]")];
    private static readonly Regex[] CppPatterns = [R(@"^\s*#\s*include\s*[<""]"), R(@"\bstd::\w+"), R(@"\b(int|void)\s+main\s*\("), R(@"\b(cout|cin|printf|malloc)\b"), R(@"\b(class|struct)\s+\w+\s*\{")];
    private static readonly Regex[] PythonPatterns = [R(@"^\s*(def|class)\s+\w+.*:\s*$"), R(@"^\s*(from\s+\S+\s+)?import\s+"), R(@"^\s*(if|for|while|try|with)\b.*:\s*$"), R(@"\bself\.\w+"), R(@"^\s*print\s*\(")];
    private static readonly Regex[] LuaPatterns = [R(@"^\s*local\s+\w+"), R(@"\bfunction\s+[\w.:]+\s*\("), R(@"^\s*(if|for|while)\b.*\b(then|do)\s*$"), R(@"^\s*end\s*$"), R(@"\brequire\s*\(?\s*['""]")];
    private static readonly Regex[] HtmlPatterns = [R(@"<!doctype\s+html"), R(@"<html\b"), R(@"<(div|span|body|head|script|style)\b"), R(@"</\w+>"), R(@"\b(class|id)=['""]")];
    private static readonly Regex[] JavaScriptPatterns = [R(@"\b(const|let|var)\s+\w+\s*="), R(@"\bfunction\s+\w*\s*\("), R(@"=>"), R(@"\bconsole\.(log|error)\s*\("), R(@"^\s*(export\b|import\s+.+\s+from\b|import\s+['""])")];
    private static readonly Regex[] CssPatterns = [R(@"[^{}]+\{\s*$"), R(@"^\s*[\w-]+\s*:\s*[^;]+;"), R(@"#[\w-]+\s*\{"), R(@"\.[\w-]+\s*\{"), R(@"@(media|keyframes|font-face)\b")];
    private static readonly Regex[] MarkdownPatterns = [R(@"^#{1,6}\s+\S"), R(@"^\s*```"), R(@"^\s*[-*+]\s+\S"), R(@"\[[^]]+\]\([^)]+\)"), R(@"^\s*>\s+\S")];
    private static readonly Regex[] PowerShellPatterns = [R(@"\$[a-z_]\w*"), R(@"\b(Get|Set|New|Remove|Invoke|Start|Stop)-[A-Z]\w+"), R(@"^\s*param\s*\("), R(@"\|\s*(Where|ForEach|Select)-Object\b"), R(@"\$env:\w+")];

    [GeneratedRegex(@"^\s*(<!doctype\s+html|<html\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlRootRegex();
}
