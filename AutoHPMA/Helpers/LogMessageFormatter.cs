using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AutoHPMA.Helpers;

public static partial class LogMessageFormatter
{
    private static readonly Regex MarkupRegex = CreateMarkupRegex();

    public static void Apply(TextBlock textBlock, string message)
    {
        ArgumentNullException.ThrowIfNull(textBlock);

        textBlock.Inlines.Clear();

        var last = 0;
        foreach (Match match in MarkupRegex.Matches(message))
        {
            if (match.Index > last)
            {
                textBlock.Inlines.Add(new Run { Text = message[last..match.Index] });
            }

            var run = new Run { Text = match.Groups["text"].Value };
            if (TryCreateBrush(match.Groups["name"].Value, out var brush))
            {
                run.Foreground = brush;
            }

            textBlock.Inlines.Add(run);
            last = match.Index + match.Length;
        }

        if (last < message.Length)
        {
            textBlock.Inlines.Add(new Run { Text = message[last..] });
        }
    }

    private static bool TryCreateBrush(string name, out SolidColorBrush brush)
    {
        Color? color = name.ToLowerInvariant() switch
        {
            "yellow" or "gold" => Color.FromArgb(0xFF, 0xFA, 0xCC, 0x15),
            "lime" => Color.FromArgb(0xFF, 0x84, 0xCC, 0x16),
            "aquamarine" => Color.FromArgb(0xFF, 0x7F, 0xFF, 0xD4),
            "red" => Color.FromArgb(0xFF, 0xF8, 0x71, 0x71),
            "green" or "cyan" => Color.FromArgb(0xFF, 0x22, 0xD3, 0xEE),
            "blue" => Color.FromArgb(0xFF, 0x60, 0xA5, 0xFA),
            _ => default,
        };

        if (color is not { } resolvedColor)
        {
            brush = new SolidColorBrush(Colors.Transparent);
            return false;
        }

        brush = new SolidColorBrush(resolvedColor);
        return true;
    }

    [GeneratedRegex(@"\[(?<name>[A-Za-z]+)\](?<text>.*?)\[/\k<name>\]", RegexOptions.Compiled)]
    private static partial Regex CreateMarkupRegex();
}
