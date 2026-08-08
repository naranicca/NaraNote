using NaraNote.Core.Utilities;

namespace NaraNote.Core.Tests;

public sealed class SyntaxDetectorTests
{
    [Theory]
    [InlineData("{\"name\":\"NaraNote\",\"enabled\":true}", "Json")]
    [InlineData("<?xml version=\"1.0\"?><note><text>Hello</text></note>", "Xml")]
    [InlineData("<!doctype html><html><body><div class=\"note\">Hi</div></body></html>", "Html")]
    [InlineData("using System;\nnamespace Demo;\npublic sealed class App { string name = \"x\"; }", "CSharp")]
    [InlineData("#include <iostream>\nint main() { std::cout << \"hi\"; return 0; }", "Cpp")]
    [InlineData("import os\ndef greet(name):\n    if name:\n        print(name)\n", "Python")]
    [InlineData("local value = require('demo')\nfunction run()\n  if value then print(value) end\nend", "Lua")]
    [InlineData("const value = 1;\nfunction run() { console.log(value); }\nexport { run };", "JavaScript")]
    [InlineData("# Title\n\n- first\n- second\n\n[link](https://example.com)\n```cs\ncode\n```", "Markdown")]
    [InlineData("param($Name)\n$items = Get-ChildItem\n$items | Where-Object { $_.Name -eq $Name }", "PowerShell")]
    public void Detects_supported_syntax(string text, string expected)
        => Assert.Equal(expected, SyntaxDetector.Detect(text)?.Language);

    [Theory]
    [InlineData("sample.py", "Python")]
    [InlineData("sample.CPP", "Cpp")]
    [InlineData("sample.lua", "Lua")]
    public void File_extension_has_high_confidence(string fileName, string expected)
    {
        var result = SyntaxDetector.Detect("short", fileName);
        Assert.Equal(expected, result?.Language);
        Assert.Equal(1, result?.Confidence);
    }

    [Fact]
    public void Natural_language_is_not_forced_into_a_syntax()
        => Assert.Null(SyntaxDetector.Detect("오늘 해야 할 일을 간단히 정리한 일반 메모입니다."));
}
