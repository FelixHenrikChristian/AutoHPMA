using System.Net;
using System.Net.Http;
using System.Net.Http.Json;

using Microsoft.UI.Xaml;

namespace AutoHPMA.Helpers;

/// <summary>
/// 在 WebView2 中展示 GitHub Release 正文：优先通过 GitHub Markdown API 按 GFM 渲染（与网页版 Release 一致），失败时回退为纯文本。
/// </summary>
internal static class ReleaseNotesHtmlHelper
{
    /// <summary>与 <see cref="Services.UpdateService"/> 中 API 使用同一仓库，用于 GFM 中 #issue、相对链接等上下文的解析。</summary>
    private const string DefaultGithubRepoContext = "FelixHenrikChristian/AutoHPMA";

    private static readonly HttpClient MarkdownHttpClient = CreateMarkdownHttpClient();

    private static HttpClient CreateMarkdownHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AutoHPMA");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static bool ResolveIsDarkTheme(FrameworkElement element)
    {
        return element.ActualTheme switch
        {
            ElementTheme.Dark => true,
            ElementTheme.Light => false,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Dark,
        };
    }

    /// <summary>
    /// 调用 <c>POST https://api.github.com/markdown</c>，将 Release 的 Markdown 转为 HTML（与 GitHub 展示一致），无需手写正则。
    /// </summary>
    public static async Task<string> GenerateReleaseNotesHtmlAsync(
        string markdown,
        bool isDarkTheme,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return GenerateFallbackHtml(isDarkTheme);
        }

        try
        {
            using var content = JsonContent.Create(new
            {
                text = markdown,
                mode = "gfm",
                context = DefaultGithubRepoContext,
            });

            using var response = await MarkdownHttpClient
                .PostAsync("https://api.github.com/markdown", content, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            var innerHtml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return WrapHtml(isDarkTheme, $"<div class='markdown-body'>{innerHtml}</div>", pageTitle: "更新日志");
        }
        catch (HttpRequestException)
        {
            return GeneratePlainTextFallbackHtml(markdown, isDarkTheme);
        }
        catch (TaskCanceledException)
        {
            return GeneratePlainTextFallbackHtml(markdown, isDarkTheme);
        }
    }

    public static string GenerateFallbackHtml(bool isDarkTheme) =>
        WrapHtml(
            isDarkTheme,
            bodyInner: "<div class='message'>无法加载更新日志，请前往GitHub查看详细信息</div>",
            pageTitle: "更新日志",
            centered: true);

    /// <summary>网络不可用或 API 失败时，至少保证可读、不破坏格式的纯文本展示。</summary>
    private static string GeneratePlainTextFallbackHtml(string markdown, bool isDarkTheme)
    {
        var encoded = WebUtility.HtmlEncode(markdown);
        var body = $"<pre class='plain-fallback'>{encoded}</pre>";
        return WrapHtml(isDarkTheme, body, pageTitle: "更新日志");
    }

    private static string WrapHtml(bool isDarkTheme, string bodyInner, string pageTitle, bool centered = false)
    {
        var fg = isDarkTheme ? "#ffffff" : "#1f1f1f";
        var bg = isDarkTheme ? "#2d2d30" : "#ffffff";
        var tertiary = isDarkTheme ? "#cccccc" : "#605e5c";
        var accentStrong = isDarkTheme ? "#4fc3f7" : "#0078d4";
        var accentEm = isDarkTheme ? "#81c784" : "#107c10";
        var codeBg = isDarkTheme ? "#3c3c3c" : "#f3f2f1";
        var codeFg = isDarkTheme ? "#d4d4d4" : "#323130";
        var hr = isDarkTheme ? "#484848" : "#edebe9";
        var titleErr = isDarkTheme ? "#ff6b6b" : "#d13438";
        var note = isDarkTheme ? "#999999" : "#8a8886";
        var sbTrack = isDarkTheme ? "#2d2d30" : "#f1f1f1";
        var sbThumb = isDarkTheme ? "#484848" : "#c1c1c1";
        var sbThumbHover = isDarkTheme ? "#5a5a5a" : "#a8a8a8";
        var link = isDarkTheme ? "#58a6ff" : "#0969da";
        var borderMuted = isDarkTheme ? "#444444" : "#d0d7de";

        var extraBody = centered
            ? $"text-align: center; padding-top: 50px; color: {tertiary};"
            : string.Empty;

        var titleSafe = WebUtility.HtmlEncode(pageTitle);
        return $@"<!DOCTYPE html>
<html lang='zh-CN'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>{titleSafe}</title>
    <style>
        body {{
            font-family: 'Segoe UI', 'Microsoft YaHei', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            color: {fg};
            margin: 12px;
            padding: 8px;
            background-color: {bg};
            font-size: 14px;
            {extraBody}
        }}
        a {{ color: {link}; }}
        .markdown-body a {{ word-break: break-all; }}
        .markdown-body img {{ max-width: 100%; height: auto; }}
        .markdown-body table {{
            border-collapse: collapse;
            width: 100%;
            margin: 12px 0;
            font-size: 13px;
        }}
        .markdown-body table th,
        .markdown-body table td {{
            border: 1px solid {borderMuted};
            padding: 6px 10px;
        }}
        .markdown-body table tr:nth-child(2n) {{
            background-color: {(isDarkTheme ? "#252526" : "#f6f8fa")};
        }}
        h1, h2, h3, h4, h5, h6 {{
            color: {fg};
            margin-top: 16px;
            margin-bottom: 8px;
            font-weight: 600;
        }}
        h1 {{ font-size: 20px; }}
        h2 {{ font-size: 18px; }}
        h3 {{ font-size: 16px; }}
        h4 {{ font-size: 15px; }}
        p {{ margin-bottom: 10px; margin-top: 0; }}
        .markdown-body ul, .markdown-body ol {{
            padding-left: 24px;
            margin-top: 8px;
            margin-bottom: 12px;
        }}
        .markdown-body li {{ margin-bottom: 4px; }}
        .markdown-body li > p {{ margin-bottom: 4px; }}
        .markdown-body .task-list-item {{ list-style: none; margin-left: -1.2em; }}
        strong {{ color: {accentStrong}; font-weight: 600; }}
        em {{ color: {accentEm}; font-style: italic; }}
        code {{
            background-color: {codeBg};
            color: {codeFg};
            padding: 2px 4px;
            border-radius: 3px;
            font-family: 'Consolas', 'Courier New', monospace;
            font-size: 13px;
        }}
        pre {{
            background-color: {codeBg};
            color: {codeFg};
            padding: 12px;
            border-radius: 6px;
            overflow-x: auto;
            font-family: 'Consolas', 'Courier New', monospace;
            font-size: 13px;
            line-height: 1.45;
        }}
        pre code {{
            background: none;
            padding: 0;
            font-size: inherit;
        }}
        blockquote {{
            border-left: 3px solid {accentStrong};
            margin: 12px 0;
            padding-left: 12px;
            color: {tertiary};
        }}
        hr {{
            border: none;
            border-top: 1px solid {hr};
            margin: 16px 0;
        }}
        .message {{ font-size: 16px; color: {tertiary}; }}
        .title {{
            font-size: 18px;
            font-weight: 600;
            color: {titleErr};
            margin-bottom: 16px;
        }}
        .note {{ font-size: 12px; color: {note}; margin-top: 16px; }}
        .plain-fallback {{
            white-space: pre-wrap;
            word-wrap: break-word;
            font-family: inherit;
            margin: 0;
            background: transparent;
            color: {fg};
        }}
        ::-webkit-scrollbar {{ width: 8px; }}
        ::-webkit-scrollbar-track {{ background: {sbTrack}; }}
        ::-webkit-scrollbar-thumb {{ background: {sbThumb}; border-radius: 4px; }}
        ::-webkit-scrollbar-thumb:hover {{ background: {sbThumbHover}; }}
    </style>
</head>
<body>
{bodyInner}
</body>
</html>
";
    }
}
