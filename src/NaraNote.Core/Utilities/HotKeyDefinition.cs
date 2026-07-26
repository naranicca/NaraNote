namespace NaraNote.Core.Utilities;

[Flags]
public enum HotKeyModifiers : uint { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }
public readonly record struct HotKeyDefinition(HotKeyModifiers Modifiers, uint VirtualKey)
{
    public static bool TryParse(string value, out HotKeyDefinition definition)
    {
        definition = default; if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); HotKeyModifiers modifiers = 0; uint key = 0;
        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase)) modifiers |= HotKeyModifiers.Control;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= HotKeyModifiers.Alt;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= HotKeyModifiers.Shift;
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)) modifiers |= HotKeyModifiers.Windows;
            else if (part.Length == 1 && char.IsLetterOrDigit(part[0])) key = char.ToUpperInvariant(part[0]);
            else if (part.StartsWith('F') && int.TryParse(part[1..], out var function) && function is >= 1 and <= 24) key = (uint)(0x70 + function - 1);
            else return false;
        }
        if (key == 0 || modifiers == HotKeyModifiers.None) return false; definition = new(modifiers, key); return true;
    }
}
