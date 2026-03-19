using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AutoHPMA.Helpers
{
    /// <summary>
    /// 解析颜色文本
    /// 将带有颜色标记的文本解析为多个Run
    /// 用于日志消息
    /// </summary>
    public static class InlineBindingHelper
    {
        public static readonly DependencyProperty FormattedTextProperty =
            DependencyProperty.RegisterAttached(
                "FormattedText",
                typeof(string),
                typeof(InlineBindingHelper),
                new PropertyMetadata(null, OnFormattedTextChanged));

        public static string GetFormattedText(DependencyObject obj)
        {
            return (string)obj.GetValue(FormattedTextProperty);
        }

        public static void SetFormattedText(DependencyObject obj, string value)
        {
            obj.SetValue(FormattedTextProperty, value);
        }

        private static void OnFormattedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var textBlock = d as TextBlock;
            if (textBlock == null) return;

            textBlock.Inlines.Clear();
            var content = e.NewValue as string;
            if (string.IsNullOrEmpty(content)) return;

            var segments = ParseContent(content);
            foreach (var segment in segments)
            {
                var run = new Run(segment.Text);
                if (segment.Color != null)
                {
                    run.Foreground = new SolidColorBrush(segment.Color.Value);
                }
                textBlock.Inlines.Add(run);
            }
        }

        private static IEnumerable<TextSegment> ParseContent(string content)
        {
            var pattern = @"\[(\w+)\](.*?)\[/\1\]";
            var matches = Regex.Matches(content, pattern, RegexOptions.Singleline);
            int lastIndex = 0;

            foreach (Match match in matches)
            {
                int start = match.Index;
                if (start > lastIndex)
                {
                    yield return new TextSegment
                    {
                        Text = content.Substring(lastIndex, start - lastIndex),
                        Color = null
                    };
                }

                string colorName = match.Groups[1].Value;
                string text = match.Groups[2].Value;
                Color color;
                try
                {
                    color = (Color)ColorConverter.ConvertFromString(colorName);
                }
                catch
                {
                    color = Colors.White; // 默认颜色
                }

                yield return new TextSegment
                {
                    Text = text,
                    Color = color
                };

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < content.Length)
            {
                yield return new TextSegment
                {
                    Text = content.Substring(lastIndex),
                    Color = null
                };
            }
        }

        private class TextSegment
        {
            public string Text { get; set; }
            public Color? Color { get; set; }
        }
    }
}