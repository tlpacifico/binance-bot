using System.Text.RegularExpressions;

namespace BinanceBot.Infrastructure.Telegram;

internal static partial class MarkdownV2Helper
{
    // Characters that must be escaped in MarkdownV2 (outside of code blocks)
    private static readonly char[] SpecialChars =
        ['_', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!'];

    /// <summary>
    /// Escapes a MarkdownV2 message while preserving *bold* markers.
    /// Splits on *, escapes content between them, then re-joins with *.
    /// </summary>
    public static string EscapePreservingBold(string text)
    {
        var parts = text.Split('*');

        // If odd number of parts, we have matched pairs of *
        // parts[0] = before first *, parts[1] = inside first bold, parts[2] = between, etc.
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = EscapeMarkdown(parts[i]);
        }

        return string.Join("*", parts);
    }

    /// <summary>
    /// Escapes all MarkdownV2 special characters in the given text.
    /// </summary>
    public static string EscapeMarkdown(string text)
    {
        foreach (var c in SpecialChars)
        {
            text = text.Replace(c.ToString(), $"\\{c}");
        }
        return text;
    }
}
