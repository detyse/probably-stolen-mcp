using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PSMcp.Contracts;

internal static class HelpChecks
{
    public static async Task Run(string serverDll, Action<bool, string> check)
    {
        // Empty working directory and missing game connection exercise standalone packaging.
        var directory = Path.Combine(Path.GetTempPath(), "psmcp-help-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var decoy = Path.Combine(directory, "overview.md");
        await File.WriteAllTextAsync(decoy, "LOCAL_FILE_MUST_NOT_BE_READ");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "PSMcp offline help test",
                Command = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                Arguments = [Path.GetFullPath(serverDll), "--connection", Path.Combine(directory, "missing.json")],
                WorkingDirectory = directory
            }), cancellationToken: timeout.Token);
            var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
            var helpTool = tools.Single(t => t.Name == "get_help").ProtocolTool;
            check(helpTool.Annotations?.ReadOnlyHint == true && helpTool.Annotations.OpenWorldHint == false,
                "get_help is discoverable as a read-only local tool");
            check(client.ServerInstructions?.Contains("get_help") == true, "MCP initialization advertises optional built-in help");

            var offline = await client.CallToolAsync("get_bridge_status", cancellationToken: timeout.Token);
            check(offline.IsError == true && offline.StructuredContent!.Value.GetProperty("errorCode").GetString() == "connection_file_unavailable",
                "help test runs with no usable game connection");
            var overview = await client.CallToolAsync("get_help", cancellationToken: timeout.Token);
            var body = overview.StructuredContent!.Value;
            check(overview.IsError != true && body.GetProperty("topic").GetString() == "overview"
                && body.GetProperty("protocolVersion").GetInt32() == Protocol.Version
                && !body.GetProperty("content").GetString()!.Contains("LOCAL_FILE_MUST_NOT_BE_READ"),
                "default help comes from the assembly without a repository or game connection");
            check(overview.Content.OfType<TextContentBlock>().Single().Text == body.GetRawText(),
                "text-only and structured MCP clients receive the same help");

            var topics = body.GetProperty("topics").EnumerateArray().Select(t => t.GetProperty("topic").GetString()!).ToArray();
            check(new[] { "overview", "actions", "items", "appraisal", "machines" }.All(topics.Contains)
                && topics.Distinct(StringComparer.Ordinal).Count() == topics.Length, "help catalog includes core operation topics with unique IDs");
            foreach (var topic in topics)
            {
                var reply = await client.CallToolAsync("get_help", new Dictionary<string, object?> { ["topic"] = topic }, cancellationToken: timeout.Token);
                var guide = reply.StructuredContent!.Value;
                check(reply.IsError != true && guide.GetProperty("topic").GetString() == topic
                    && guide.GetProperty("content").GetString()!.StartsWith("# " + guide.GetProperty("title").GetString()),
                    "bundled help topic is readable: " + topic);
            }
            var normalized = await client.CallToolAsync("get_help", new Dictionary<string, object?> { ["topic"] = " ACTIONS " }, cancellationToken: timeout.Token);
            var instructions = normalized.StructuredContent!.Value.GetProperty("content").GetString()!;
            check(normalized.IsError != true && instructions.Contains("expectedSnapshotId") && instructions.Contains("requestId")
                && instructions.Contains("stale_snapshot"), "topic normalization retains complete action and retry guidance");
            foreach (var invalid in new[] { "unknown", "../overview.md", decoy, "https://example.com/guide" })
            {
                var reply = await client.CallToolAsync("get_help", new Dictionary<string, object?> { ["topic"] = invalid }, cancellationToken: timeout.Token);
                check(reply.IsError == true && reply.StructuredContent!.Value.GetProperty("errorCode").GetString() == "unknown_help_topic"
                    && reply.StructuredContent.Value.GetProperty("topics").GetArrayLength() == topics.Length,
                    "unknown topics and paths are rejected with the available catalog");
            }
            var recovered = await client.CallToolAsync("get_help", cancellationToken: timeout.Token);
            check(recovered.IsError != true, "invalid help requests leave the MCP server usable");
        }
        finally
        {
            File.Delete(decoy);
            Directory.Delete(directory);
        }
    }
}
