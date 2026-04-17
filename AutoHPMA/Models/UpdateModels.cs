using System.Text.Json.Serialization;

namespace AutoHPMA.Models;

public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;
}

public enum UpdateTrigger
{
    Auto,
    Manual,
}

public class UpdateOption
{
    public UpdateTrigger Trigger { get; set; }
}
