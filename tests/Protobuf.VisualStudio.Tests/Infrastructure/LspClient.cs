using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Tests.Infrastructure;

/// <summary>
/// A minimal LSP client over stdio (Content-Length framing, JSON-RPC 2.0). It starts buf exactly the way
/// <c>BufLanguageServerProvider</c> does, so the tests check the contract Visual Studio's LSP client relies on.
/// </summary>
public sealed class LspClient : IAsyncDisposable
{
    private readonly Process process;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> pending = new();
    private readonly List<(string Method, JsonNode? Params)> notifications = [];
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly StringBuilder stderr = new();
    private readonly Task readLoop;
    private int nextId;

    private LspClient(Process process)
    {
        this.process = process;
        this.readLoop = Task.Run(this.ReadLoopAsync);
        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                lock (this.stderr)
                {
                    this.stderr.AppendLine(line);
                }
            }
        });
    }

    /// <summary>Everything the server wrote to stderr so far (its log), for failure messages.</summary>
    public string ServerLog
    {
        get
        {
            lock (this.stderr)
            {
                return this.stderr.ToString();
            }
        }
    }

    internal static LspClient Start(BufBinary buf, string workingDirectory)
    {
        var info = BufCli.CreateStartInfo(buf.Path, buf.LanguageServerArguments(debug: false), workingDirectory);
        var process = Process.Start(info) ?? throw new InvalidOperationException("buf did not start.");
        return new LspClient(process);
    }

    public static string Uri(string path) => new Uri(path).AbsoluteUri;

    /// <summary>
    /// True when two file URIs name the same file. buf writes <c>file:///c%3A/...</c> (lowercase drive, escaped
    /// colon) where .NET writes <c>file:///C:/...</c>, so compare decoded local paths, ignoring case.
    /// </summary>
    public static bool SameFile(string? a, string? b) =>
        a is not null && b is not null &&
        string.Equals(LocalPath(a), LocalPath(b), StringComparison.OrdinalIgnoreCase);

    private static string LocalPath(string uri) => new Uri(System.Uri.UnescapeDataString(uri)).LocalPath;

    public async Task<JsonNode?> RequestAsync(string method, object? parameters, TimeSpan? timeout = null)
    {
        var id = Interlocked.Increment(ref this.nextId);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        this.pending[id] = tcs;
        await this.SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = ToNode(parameters) }).ConfigureAwait(false);

        var finished = await Task.WhenAny(tcs.Task, Task.Delay(timeout ?? TimeSpan.FromSeconds(60))).ConfigureAwait(false);
        if (finished != tcs.Task)
        {
            throw new TimeoutException($"No response to {method} within the timeout.\nServer log:\n{this.ServerLog}");
        }

        return await tcs.Task.ConfigureAwait(false);
    }

    public Task NotifyAsync(string method, object? parameters) =>
        this.SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = ToNode(parameters) });

    /// <summary>Waits until a notification matching <paramref name="predicate"/> arrives (or has already arrived).</summary>
    public async Task<JsonNode?> WaitForNotificationAsync(string method, Func<JsonNode?, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            lock (this.notifications)
            {
                foreach (var (m, p) in this.notifications)
                {
                    if (m == method && predicate(p))
                    {
                        return p;
                    }
                }
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        string received;
        lock (this.notifications)
        {
            received = string.Join('\n', this.notifications.Select(n => $"  {n.Method}: {Truncate(n.Params?.ToJsonString())}"));
        }

        throw new TimeoutException($"No matching {method} within {timeout}.\nReceived:\n{received}\nServer log:\n{this.ServerLog}");
    }

    private static string Truncate(string? text) => text is null ? "null" : text.Length <= 400 ? text : text[..400] + "...";

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!this.process.HasExited)
            {
                await this.RequestAsync("shutdown", null, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await this.NotifyAsync("exit", null).ConfigureAwait(false);
                await this.process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or IOException or InvalidOperationException)
        {
        }
        finally
        {
            BufCli.TryKill(this.process);
            this.process.Dispose();
            this.writeGate.Dispose();
        }
    }

    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode node => node,
        _ => JsonSerializer.SerializeToNode(value),
    };

    private async Task SendAsync(JsonObject message)
    {
        var body = Encoding.UTF8.GetBytes(message.ToJsonString());
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await this.writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var stdin = this.process.StandardInput.BaseStream;
            await stdin.WriteAsync(header).ConfigureAwait(false);
            await stdin.WriteAsync(body).ConfigureAwait(false);
            await stdin.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            this.writeGate.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        var stdout = this.process.StandardOutput.BaseStream;
        try
        {
            while (true)
            {
                var length = await ReadHeaderAsync(stdout).ConfigureAwait(false);
                if (length < 0)
                {
                    break;
                }

                var buffer = new byte[length];
                await stdout.ReadExactlyAsync(buffer).ConfigureAwait(false);
                await this.DispatchAsync(JsonNode.Parse(buffer)!).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ObjectDisposedException)
        {
        }
        finally
        {
            foreach (var tcs in this.pending.Values)
            {
                tcs.TrySetException(new IOException($"The server closed the connection.\nServer log:\n{this.ServerLog}"));
            }
        }
    }

    private async Task DispatchAsync(JsonNode message)
    {
        var hasId = message["id"] is not null;
        var method = message["method"]?.GetValue<string>();

        if (method is null && hasId)
        {
            // Response to one of our requests.
            if (this.pending.TryRemove(message["id"]!.GetValue<int>(), out var tcs))
            {
                if (message["error"] is { } error)
                {
                    tcs.TrySetException(new InvalidOperationException($"LSP error: {error.ToJsonString()}"));
                }
                else
                {
                    tcs.TrySetResult(message["result"]?.DeepClone());
                }
            }

            return;
        }

        if (method is not null && hasId)
        {
            // Server-to-client request (workspace/configuration, client/registerCapability, ...). Answer like a
            // client with no extra configuration, so the server never blocks waiting for us.
            JsonNode? result = method == "workspace/configuration"
                ? new JsonArray((message["params"]?["items"]?.AsArray() ?? []).Select(_ => (JsonNode?)null).ToArray())
                : null;
            await this.SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = message["id"]!.DeepClone(), ["result"] = result }).ConfigureAwait(false);
            return;
        }

        if (method is not null)
        {
            lock (this.notifications)
            {
                this.notifications.Add((method, message["params"]?.DeepClone()));
            }
        }
    }

    /// <summary>Reads header lines up to the blank line and returns Content-Length, or -1 at end of stream.</summary>
    private static async Task<int> ReadHeaderAsync(Stream stream)
    {
        var contentLength = -1;
        var line = new StringBuilder();
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one).ConfigureAwait(false) == 0)
            {
                return -1;
            }

            if (one[0] == '\n')
            {
                var text = line.ToString().TrimEnd('\r');
                line.Clear();
                if (text.Length == 0)
                {
                    if (contentLength >= 0)
                    {
                        return contentLength;
                    }

                    continue;
                }

                if (text.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    contentLength = int.Parse(text["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            else
            {
                line.Append((char)one[0]);
            }
        }
    }
}
