using System.Text.Json.Nodes;
using Protobuf.VisualStudio.Tests.Infrastructure;

namespace Protobuf.VisualStudio.Tests.Integration;

/// <summary>
/// One <c>buf lsp serve</c> session over the weather sample, shared by the tests in
/// <see cref="LanguageServerIntegrationTests"/>. Both sample files are opened like Visual Studio opens documents.
/// </summary>
public sealed class LspSession : IAsyncLifetime
{
    public static readonly string WeatherPath = Path.Combine(RealBuf.WeatherSample, "proto", "acme", "weather", "v1", "weather.proto");
    public static readonly string LocationPath = Path.Combine(RealBuf.WeatherSample, "proto", "acme", "common", "v1", "location.proto");

    public LspClient Client { get; private set; } = null!;

    public JsonNode Capabilities { get; private set; } = null!;

    public string WeatherText { get; } = File.ReadAllText(WeatherPath);

    public string LocationText { get; } = File.ReadAllText(LocationPath);

    public async ValueTask InitializeAsync()
    {
        var buf = await RealBuf.GetAsync();
        this.Client = LspClient.Start(buf, RealBuf.WeatherSample);

        var root = LspClient.Uri(RealBuf.WeatherSample);
        var init = await this.Client.RequestAsync("initialize", new
        {
            processId = Environment.ProcessId,
            rootUri = root,
            workspaceFolders = new[] { new { uri = root, name = "weather" } },
            capabilities = new
            {
                textDocument = new
                {
                    completion = new { completionItem = new { snippetSupport = true } },
                    hover = new { contentFormat = new[] { "markdown", "plaintext" } },
                    publishDiagnostics = new { relatedInformation = true },
                    semanticTokens = new
                    {
                        requests = new { full = true },
                        tokenTypes = Array.Empty<string>(),
                        tokenModifiers = Array.Empty<string>(),
                        formats = new[] { "relative" },
                    },
                },
                workspace = new { workspaceFolders = true, configuration = true },
            },
        });
        this.Capabilities = init!["capabilities"]!;
        await this.Client.NotifyAsync("initialized", new { });

        foreach (var (path, text) in new[] { (WeatherPath, this.WeatherText), (LocationPath, this.LocationText) })
        {
            await this.Client.NotifyAsync("textDocument/didOpen", new
            {
                textDocument = new { uri = LspClient.Uri(path), languageId = "protobuf", version = 1, text },
            });

            // Lint runs on save as well as on open in some buf versions; a save notification covers both.
            await this.Client.NotifyAsync("textDocument/didSave", new { textDocument = new { uri = LspClient.Uri(path) } });
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (this.Client is not null)
        {
            await this.Client.DisposeAsync();
        }
    }
}

[Trait("Category", "Integration")]
public sealed class LanguageServerIntegrationTests(LspSession session) : IClassFixture<LspSession>
{
    private static readonly TimeSpan DiagnosticsTimeout = TimeSpan.FromSeconds(60);

    private static string WeatherUri => LspClient.Uri(LspSession.WeatherPath);

    private static string LocationUri => LspClient.Uri(LspSession.LocationPath);

    [Theory]
    [InlineData("completionProvider")]
    [InlineData("hoverProvider")]
    [InlineData("definitionProvider")]
    [InlineData("referencesProvider")]
    [InlineData("renameProvider")]
    [InlineData("documentFormattingProvider")]
    [InlineData("documentSymbolProvider")]
    [InlineData("semanticTokensProvider")]
    [InlineData("codeActionProvider")]
    public void Server_advertises_the_capabilities_the_extension_relies_on(string capability)
    {
        var value = session.Capabilities[capability];
        Assert.True(value is not null && value.ToJsonString() != "false", $"'{capability}' missing: {session.Capabilities.ToJsonString()}");
    }

    [Fact]
    public async Task Lint_violation_is_published_as_a_diagnostic()
    {
        var diagnostics = await session.Client.WaitForNotificationAsync(
            "textDocument/publishDiagnostics",
            p => LspClient.SameFile(p?["uri"]?.GetValue<string>(), WeatherUri) && Messages(p).Any(m => m.Contains("windSpeedKph", StringComparison.Ordinal)),
            DiagnosticsTimeout);

        var line = Position(session.WeatherText, "windSpeedKph").Line;
        var hit = diagnostics!["diagnostics"]!.AsArray().First(d => d!["message"]!.GetValue<string>().Contains("windSpeedKph", StringComparison.Ordinal))!;
        Assert.Equal(line, hit["range"]!["start"]!["line"]!.GetValue<int>());
    }

    [Fact]
    public async Task Clean_file_has_no_errors()
    {
        var diagnostics = await session.Client.WaitForNotificationAsync(
            "textDocument/publishDiagnostics",
            p => LspClient.SameFile(p?["uri"]?.GetValue<string>(), LocationUri),
            DiagnosticsTimeout);

        // Severity 1 is Error.
        Assert.DoesNotContain(diagnostics!["diagnostics"]!.AsArray(), d => d?["severity"]?.GetValue<int>() == 1);
    }

    [Fact]
    public async Task Go_to_definition_jumps_across_files()
    {
        var result = await session.Client.RequestAsync("textDocument/definition", At(WeatherUri, session.WeatherText, "acme.common.v1.Location location", offset: "acme.common.v1.".Length + 2));

        var target = FirstLocation(result);
        Assert.EndsWith("location.proto", target["uri"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(Position(session.LocationText, "message Location").Line, target["range"]!["start"]!["line"]!.GetValue<int>());
    }

    [Fact]
    public async Task Hover_shows_the_doc_comment()
    {
        var result = await session.Client.RequestAsync("textDocument/hover", At(WeatherUri, session.WeatherText, "Observation observation = 1", offset: 2));

        Assert.Contains("One weather observation", result!["contents"]!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Completion_offers_types_from_imported_packages()
    {
        var result = await session.Client.RequestAsync("textDocument/completion", At(WeatherUri, session.WeatherText, "acme.common.v1.Location location", offset: "acme.common.v1.".Length));

        var items = (result is JsonArray array ? array : result?["items"]?.AsArray()) ?? [];
        Assert.Contains(items, i => i!["label"]!.GetValue<string>().EndsWith("Location", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Find_references_includes_uses_in_other_files()
    {
        var parameters = At(LocationUri, session.LocationText, "message Location", offset: "message ".Length + 1);
        parameters["context"] = new JsonObject { ["includeDeclaration"] = true };

        var result = await session.Client.RequestAsync("textDocument/references", parameters);

        var uris = result!.AsArray().Select(r => r!["uri"]!.GetValue<string>()).ToArray();
        Assert.Contains(uris, u => u.EndsWith("weather.proto", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Document_symbols_list_the_service_and_messages()
    {
        var result = await session.Client.RequestAsync("textDocument/documentSymbol", new { textDocument = new { uri = WeatherUri } });

        var names = Flatten(result!.AsArray()).ToArray();
        Assert.Contains(names, n => n.EndsWith("WeatherService", StringComparison.Ordinal));
        Assert.Contains(names, n => n.EndsWith("Observation", StringComparison.Ordinal));
        Assert.Contains(names, n => n.EndsWith("Condition", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Formatting_returns_edits_or_nothing()
    {
        var result = await session.Client.RequestAsync("textDocument/formatting", new
        {
            textDocument = new { uri = WeatherUri },
            options = new { tabSize = 2, insertSpaces = true },
        });

        Assert.True(result is null or JsonArray, $"Unexpected formatting result: {result?.ToJsonString()}");
    }

    [Fact]
    public async Task Semantic_tokens_colour_the_file()
    {
        var result = await session.Client.RequestAsync("textDocument/semanticTokens/full", new { textDocument = new { uri = WeatherUri } });

        var data = result!["data"]!.AsArray();
        Assert.NotEmpty(data);
        Assert.Equal(0, data.Count % 5); // Each token is five integers.
    }

    private static IEnumerable<string> Messages(JsonNode? publish) =>
        publish?["diagnostics"]?.AsArray().Select(d => d?["message"]?.GetValue<string>() ?? string.Empty) ?? [];

    private static JsonNode FirstLocation(JsonNode? result) => result switch
    {
        JsonArray array when array.Count > 0 => array[0]!["targetUri"] is { } targetUri
            ? new JsonObject { ["uri"] = targetUri.DeepClone(), ["range"] = array[0]!["targetSelectionRange"]!.DeepClone() }
            : array[0]!,
        JsonObject single => single,
        _ => throw new Xunit.Sdk.XunitException($"No definition returned: {result?.ToJsonString()}"),
    };

    private static IEnumerable<string> Flatten(JsonArray symbols)
    {
        foreach (var symbol in symbols)
        {
            yield return symbol!["name"]!.GetValue<string>();
            if (symbol["children"] is JsonArray children)
            {
                foreach (var child in Flatten(children))
                {
                    yield return child;
                }
            }
        }
    }

    /// <summary>LSP position parameters for the character <paramref name="offset"/> into the first <paramref name="needle"/>.</summary>
    private static JsonObject At(string uri, string text, string needle, int offset)
    {
        var (line, character) = Position(text, needle);
        return new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"] = new JsonObject { ["line"] = line, ["character"] = character + offset },
        };
    }

    private static (int Line, int Character) Position(string text, string needle)
    {
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{needle}' not found in the sample.");
        var before = text[..index];
        var line = before.Count(c => c == '\n');
        var character = index - (before.LastIndexOf('\n') + 1);
        return (line, character);
    }
}
