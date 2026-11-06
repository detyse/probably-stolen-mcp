using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PSMcp.Contracts;

namespace PSMcp.Server;

[McpServerToolType]
public sealed class HelpTools
{
    private const string Prefix = "PSMcp.Help.";
    private static readonly HelpTopic[] Topics = LoadTopics();

    [McpServerTool(Name = "get_help", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Read built-in operation help without connecting to the game. Omit topic for a short introduction and the available topics; use actions for availableActions, snapshotId and retry rules, or items/appraisal/machines for their current support and workflow boundaries. Help explains this server version; only live availableActions determine which actions can execute. Topic is a catalog ID, not a path or URL.")]
    public CallToolResult GetHelp(
        [Description("Optional topic ID from the returned topics list. Default: overview. Topic IDs are case-insensitive.")] string? topic = null)
    {
        var id = string.IsNullOrWhiteSpace(topic) ? "overview" : topic.Trim().ToLowerInvariant();
        var guide = Topics.FirstOrDefault(t => t.Topic == id);
        var catalog = Topics.Select(t => new { t.Topic, t.Title }).ToArray();
        if (guide == null)
            return Reply(new { success = false, errorCode = "unknown_help_topic",
                message = "Choose a topic from topics, or omit topic for the introduction.", topics = catalog }, true);
        return Reply(new { success = true, protocolVersion = Protocol.Version,
            serverVersion = typeof(HelpTools).Assembly.GetName().Version!.ToString(3),
            guide.Topic, guide.Title, mimeType = "text/markdown", guide.Content, topics = catalog });
    }

    private static HelpTopic[] LoadTopics()
    {
        var assembly = typeof(HelpTools).Assembly;
        var topics = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith(".md", StringComparison.Ordinal))
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();
                var title = content.Split('\n')[0].Trim();
                var id = name[Prefix.Length..^3];
                if (!title.StartsWith("# ", StringComparison.Ordinal) || id != id.ToLowerInvariant())
                    throw new InvalidOperationException("Help topics require lowercase filenames and a Markdown title.");
                return new HelpTopic(id, title[2..], content);
            }).OrderBy(t => t.Topic, StringComparer.Ordinal).ToArray();
        if (!topics.Any(t => t.Topic == "overview")) throw new InvalidOperationException("Built-in overview help is missing.");
        return topics;
    }

    private static CallToolResult Reply<T>(T value, bool isError = false)
    {
        var json = JsonSerializer.SerializeToElement(value, BridgeClient.JsonOptions);
        return new CallToolResult { IsError = isError, StructuredContent = json,
            Content = [new TextContentBlock { Text = json.GetRawText() }] };
    }

    private sealed record HelpTopic(string Topic, string Title, string Content);
}
