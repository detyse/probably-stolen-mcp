using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

// Local integration driver: only explicitly queued commands are sent to the real MCP server.
if (args.Length != 2) throw new ArgumentException("Usage: LiveDriver <run-directory> <python-executable>");
var runDirectory = Path.GetFullPath(args[0]);
var python = Path.GetFullPath(args[1]);
Directory.CreateDirectory(runDirectory);
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
McpClient? client = null;
JsonElement? lastState = null;

async Task StartServer()
{
    if (client != null) await client.DisposeAsync();
    client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "PSMcp real-game validation",
        Command = "python",
        EnvironmentVariables = new Dictionary<string, string?>
        {
            ["PATH"] = Path.GetDirectoryName(python) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
        },
        Arguments = ["-m", "psmcp"]
    }));
    var tools = await client.ListToolsAsync();
    await File.WriteAllTextAsync(Path.Combine(runDirectory, "server.json"), JsonSerializer.Serialize(new
    {
        startedUtc = DateTimeOffset.UtcNow, python, tools = tools.Select(t => t.Name).ToArray()
    }, jsonOptions));
}

await StartServer();
try
{
    while (true)
    {
        foreach (var file in Directory.EnumerateFiles(runDirectory, "*.command.json").Order(StringComparer.Ordinal))
        {
            var resultPath = file.Replace(".command.json", ".result.json");
            if (File.Exists(resultPath)) continue;
            JsonDocument command;
            try { command = JsonDocument.Parse(await File.ReadAllTextAsync(file)); }
            catch (Exception e) when (e is IOException or JsonException) { continue; }
            using (command)
            {
                if (command.RootElement.TryGetProperty("command", out var operation))
                {
                    if (operation.GetString() == "stop")
                    {
                        await File.WriteAllTextAsync(resultPath, "{\"stopped\":true}");
                        return;
                    }
                    if (operation.GetString() == "restart_mcp")
                    {
                        await StartServer();
                        await File.WriteAllTextAsync(resultPath, "{\"restarted\":true}");
                        continue;
                    }
                }
                try
                {
                    var tool = command.RootElement.GetProperty("tool").GetString()!;
                    var arguments = command.RootElement.TryGetProperty("arguments", out var supplied)
                        ? supplied.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone())
                        : new Dictionary<string, object?>();
                    if (tool == "perform_action")
                    {
                        if (!arguments.ContainsKey("requestId")) arguments["requestId"] = Guid.NewGuid().ToString();
                        if (!arguments.ContainsKey("expectedSnapshotId") && lastState.HasValue)
                            arguments["expectedSnapshotId"] = lastState.Value.GetProperty("snapshotId").GetString();
                    }
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                    var result = await client!.CallToolAsync(tool, arguments, cancellationToken: timeout.Token);
                    if (result.StructuredContent.HasValue && result.StructuredContent.Value.TryGetProperty("state", out var state)
                        && state.ValueKind == JsonValueKind.Object)
                    {
                        lastState = state.Clone();
                        await File.WriteAllTextAsync(Path.Combine(runDirectory, "latest-state.json"), state.GetRawText());
                    }
                    var images = new List<string>();
                    foreach (var image in result.Content.OfType<ImageContentBlock>())
                    {
                        var imagePath = file.Replace(".command.json", $".image-{images.Count}." + (image.MimeType == "image/png" ? "png" : "jpg"));
                        await File.WriteAllBytesAsync(imagePath, image.DecodedData.ToArray());
                        images.Add(imagePath);
                    }
                    await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new
                    {
                        timeUtc = DateTimeOffset.UtcNow, tool, arguments, result.IsError, result.StructuredContent, images,
                        text = result.StructuredContent.HasValue ? null : result.Content.OfType<TextContentBlock>().Select(t => t.Text).ToArray()
                    }, jsonOptions));
                }
                catch (Exception e)
                {
                    await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { error = e.ToString() }, jsonOptions));
                }
            }
        }
        await Task.Delay(100);
    }
}
finally { if (client != null) await client.DisposeAsync(); }
