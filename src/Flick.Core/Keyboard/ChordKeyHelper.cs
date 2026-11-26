using Flick.Core.Models;

namespace Flick.Core.Keyboard;

/// <summary>
/// Helpers for mapping Windows virtual-key codes to/from readable names and formatting chords.
/// </summary>
public static class ChordKeyHelper
{
    public const int VkBack = 0x08;
    public const int VkTab = 0x09;
    public const int VkReturn = 0x0D;
    public const int VkShift = 0x10;
    public const int VkControl = 0x11;
    public const int VkMenu = 0x12;
    public const int VkPause = 0x13;
    public const int VkCapital = 0x14;
    public const int VkEscape = 0x1B;
    public const int VkSpace = 0x20;
    public const int VkPrior = 0x21;
    public const int VkNext = 0x22;
    public const int VkEnd = 0x23;
    public const int VkHome = 0x24;
    public const int VkLeft = 0x25;
    public const int VkUp = 0x26;
    public const int VkRight = 0x27;
    public const int VkDown = 0x28;
    public const int VkInsert = 0x2D;
    public const int VkDelete = 0x2E;
    public const int VkLWin = 0x5B;
    public const int VkRWin = 0x5C;
    public const int VkLShift = 0xA0;
    public const int VkRShift = 0xA1;
    public const int VkLControl = 0xA2;
    public const int VkRControl = 0xA3;
    public const int VkLMenu = 0xA4;
    public const int VkRMenu = 0xA5;

    private static readonly Dictionary<int, string> SpecialNames = new()
    {
        [VkBack] = "Backspace",
        [VkTab] = "Tab",
        [VkReturn] = "Enter",
        [VkPause] = "Pause",
        [VkCapital] = "CapsLock",
        [VkEscape] = "Esc",
        [VkSpace] = "Space",
        [VkPrior] = "PgUp",
        [VkNext] = "PgDn",
        [VkEnd] = "End",
        [VkHome] = "Home",
        [VkLeft] = "Left",
        [VkUp] = "Up",
        [VkRight] = "Right",
        [VkDown] = "Down",
        [VkInsert] = "Insert",
        [VkDelete] = "Delete",
        [0xBA] = ";",
        [0xBB] = "=",
        [0xBC] = ",",
        [0xBD] = "-",
        [0xBE] = ".",
        [0xBF] = "/",
        [0xC0] = "`",
        [0xDB] = "[",
        [0xDC] = "\\",
        [0xDD] = "]",
        [0xDE] = "'"
    };

    public static bool IsModifierKey(int vkCode) => vkCode is
        VkShift or VkLShift or VkRShift or
        VkControl or VkLControl or VkRControl or
        VkMenu or VkLMenu or VkRMenu or
        VkLWin or VkRWin;

    public static ChordModifiers ToModifierFlag(int vkCode) => vkCode switch
    {
        VkControl or VkLControl or VkRControl => ChordModifiers.Ctrl,
        VkMenu or VkLMenu or VkRMenu => ChordModifiers.Alt,
        VkShift or VkLShift or VkRShift => ChordModifiers.Shift,
        VkLWin or VkRWin => ChordModifiers.Win,
        _ => ChordModifiers.None
    };

    public static string GetKeyName(int vkCode)
    {
        if (vkCode is >= 'A' and <= 'Z')
        {
            return ((char)vkCode).ToString();
        }

        if (vkCode is >= '0' and <= '9')
        {
            return ((char)vkCode).ToString();
        }

        if (vkCode is >= 0x70 and <= 0x87)
        {
            return $"F{vkCode - 0x70 + 1}";
        }

        if (vkCode is >= 0x60 and <= 0x69)
        {
            return $"Num{vkCode - 0x60}";
        }

        if (SpecialNames.TryGetValue(vkCode, out string? name))
        {
            return name;
        }

        return $"VK_{vkCode:X2}";
    }

    public static string FormatStep(ChordStep step)
    {
        var parts = new List<string>();
        if (step.Modifiers.HasFlag(ChordModifiers.Ctrl)) parts.Add("Ctrl");
        if (step.Modifiers.HasFlag(ChordModifiers.Alt)) parts.Add("Alt");
        if (step.Modifiers.HasFlag(ChordModifiers.Shift)) parts.Add("Shift");
        if (step.Modifiers.HasFlag(ChordModifiers.Win)) parts.Add("Win");

        foreach (int vk in step.KeyCodes)
        {
            parts.Add(GetKeyName(vk));
        }

        return parts.Count > 0 ? string.Join("+", parts) : "(Empty)";
    }

    public static string FormatChord(KeyboardChordDefinition? chord)
    {
        if (chord is null || chord.Steps.Count == 0)
        {
            return "Unassigned Chord";
        }

        if (chord.Mode == KeyboardChordMode.Simultaneous)
        {
            return FormatStep(chord.Steps[0]);
        }

        return string.Join(" → ", chord.Steps.Select(FormatStep));
    }
}
