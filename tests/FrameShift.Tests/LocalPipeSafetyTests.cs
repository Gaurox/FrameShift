using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using FrameShift.Core.Actions;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Windows.Batch;
using Xunit;

namespace FrameShift.Tests;

public sealed class LocalPipeSafetyTests
{
    private static string Unique => "FrameShift_tests_" + Guid.NewGuid().ToString("N");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SilentOrPartialPathSenderCannotBlockTheNextConnection(bool partial)
    {
        var name = Unique;
        using var stop = new CancellationTokenSource();
        var accepted = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = LocalPipeTransport.ListenForPathsAsync(name, paths => accepted.TrySetResult(paths), stop.Token, TimeSpan.FromMilliseconds(500));
        try
        {
            using var silent = LocalPipeTransport.CreateClient(name, PipeDirection.Out);
            await silent.ConnectAsync(3000);
            if (partial) await silent.WriteAsync(Encoding.UTF8.GetBytes("part of a path"));
            await LocalPipeTransport.SendPathsAsync(name, [@"C:\modèles été\image 01.png"]);
            Assert.Equal(new[] { @"C:\modèles été\image 01.png" }, await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { stop.Cancel(); await listener.WaitAsync(TimeSpan.FromSeconds(3)); }
    }

    [Theory]
    [InlineData("size")]
    [InlineData("paths")]
    [InlineData("utf8")]
    public async Task InvalidPathPacketIsRejectedAsAWholeAndListenerRecovers(string invalid)
    {
        var name = Unique;
        using var stop = new CancellationTokenSource();
        var accepted = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = LocalPipeTransport.ListenForPathsAsync(name, p => accepted.TrySetResult(p), stop.Token, TimeSpan.FromSeconds(2));
        try
        {
            using (var client = LocalPipeTransport.CreateClient(name, PipeDirection.Out))
            {
                await client.ConnectAsync(3000);
                var bytes = invalid == "utf8" ? new byte[] { 0xC3, 0x28 } : Encoding.UTF8.GetBytes(invalid == "paths"
                    ? string.Join('\n', Enumerable.Repeat("path", LocalPipeTransport.MaximumPaths + 1))
                    : new string('x', LocalPipeTransport.MaximumMessageBytes + 1));
                try { await client.WriteAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (IOException) { }
            }
            await LocalPipeTransport.SendPathsAsync(name, ["accepted.png"]);
            Assert.Equal(new[] { "accepted.png" }, await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { stop.Cancel(); await listener.WaitAsync(TimeSpan.FromSeconds(3)); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingCancelsAWaitingOrConnectedListener(bool connected)
    {
        var name = Unique;
        using var stop = new CancellationTokenSource();
        var listener = LocalPipeTransport.ListenForPathsAsync(name, _ => throw new Exception("Unexpected acceptance."), stop.Token);
        using var client = LocalPipeTransport.CreateClient(name, PipeDirection.Out);
        if (connected) await client.ConnectAsync(3000);
        stop.Cancel();
        await listener.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void ServerAclAndNamesAreIsolatedByUserAndSession()
    {
        using var server = LocalPipeTransport.CreateServer(Unique, PipeDirection.InOut);
        using var identity = WindowsIdentity.GetCurrent();
        var acl = server.GetAccessControl();
        Assert.Equal(identity.User, acl.GetOwner(typeof(SecurityIdentifier)));
        var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().Where(r => r.AccessControlType == AccessControlType.Allow).ToArray();
        Assert.NotEmpty(rules);
        Assert.All(rules, r => Assert.Equal(identity.User, r.IdentityReference));
        Assert.NotEqual(LocalPipeTransport.MakeScope("S-1-5-21-1", 1), LocalPipeTransport.MakeScope("S-1-5-21-2", 1));
        Assert.NotEqual(LocalPipeTransport.MakeScope("S-1-5-21-1", 1), LocalPipeTransport.MakeScope("S-1-5-21-1", 2));
        Assert.Equal(LocalPipeTransport.Name("same"), LocalPipeTransport.Name("same"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchClientBoundsMissingAcknowledgement(bool cancel)
    {
        var definition = Definition();
        using var server = LocalPipeTransport.CreateServer(definition.PipeName, PipeDirection.InOut);
        using var cancellation = new CancellationTokenSource();
        var connection = server.WaitForConnectionAsync();
        var send = ConversionBatchSession.SendPathsToPrimaryInstanceAsync(definition, ["path.item"], null, cancellation.Token, TimeSpan.FromSeconds(1));
        await connection.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.NotNull(await LocalPipeTransport.ReadAsync(server, default));
        if (cancel) cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task BlockedPipeWriteIsCancellable()
    {
        var name = Unique;
        using var server = LocalPipeTransport.CreateServer(name, PipeDirection.In);
        using var client = LocalPipeTransport.CreateClient(name, PipeDirection.Out);
        var connection = server.WaitForConnectionAsync();
        await client.ConnectAsync(3000);
        await connection;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LocalPipeTransport.WriteAsync(client,
            new string('x', LocalPipeTransport.MaximumMessageBytes), deadline.Token).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData("{bad json}")]
    [InlineData("{\"invocationId\":\"invalid\",\"items\":[null]}")]
    [InlineData("{\"invocationId\":\"invalid\",\"items\":[{\"path\":\"a\\nb\"}]}")]
    public async Task InvalidBatchInvocationDoesNotQueueAnyPartialItems(string message)
    {
        var definition = Definition();
        using var session = new ConversionBatchSession(definition, new NoOpAction(), new AppLogger(), pipeTimeout: TimeSpan.FromMilliseconds(500));
        session.Initialize([], default);
        using (var client = LocalPipeTransport.CreateClient(definition.PipeName, PipeDirection.InOut))
        {
            await client.ConnectAsync(3000);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await LocalPipeTransport.WriteAsync(client, message, deadline.Token);
            Assert.Contains("false", await LocalPipeTransport.ReadAsync(client, deadline.Token));
        }
        Assert.Empty(session.GetPendingQueueItems());
        Assert.True(await ConversionBatchSession.SendPathsToPrimaryInstanceAsync(definition, ["valid.item"], null, default));
        Assert.Single(session.GetPendingQueueItems());
    }

    [Fact]
    public async Task SilentBatchClientExpiresAndReplayedInvocationIsAcceptedOnce()
    {
        var definition = Definition();
        using var session = new ConversionBatchSession(definition, new NoOpAction(), new AppLogger(), pipeTimeout: TimeSpan.FromMilliseconds(500));
        session.Initialize([], default);
        using var silent = LocalPipeTransport.CreateClient(definition.PipeName, PipeDirection.InOut);
        await silent.ConnectAsync(3000);
        var message = "{\"invocationId\":\"same-id\",\"items\":[{\"path\":\"duplicate.item\"}]}";
        for (var i = 0; i < 2; i++)
        {
            using var client = LocalPipeTransport.CreateClient(definition.PipeName, PipeDirection.InOut);
            await client.ConnectAsync(3000);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await LocalPipeTransport.WriteAsync(client, message, deadline.Token);
            Assert.Contains("true", await LocalPipeTransport.ReadAsync(client, deadline.Token));
        }
        Assert.Single(session.GetPendingQueueItems());
    }

    [Fact]
    public void OversizedOutgoingJsonIsRejectedByTheSerializationBudget() =>
        Assert.Throws<InvalidDataException>(() => LocalPipeTransport.Serialize(new { value = new string('x', 2 * LocalPipeTransport.MaximumMessageBytes) }));

    [Fact]
    public async Task LostAcknowledgementDoesNotRollBackOrDuplicateAnAcceptedInvocation()
    {
        var definition = Definition();
        using var session = new ConversionBatchSession(definition, new NoOpAction(), new AppLogger());
        session.Initialize([], default);
        var message = "{\"invocationId\":\"lost-ack\",\"items\":[{\"path\":\"once.item\"}]}";
        using (var client = LocalPipeTransport.CreateClient(definition.PipeName, PipeDirection.InOut))
        {
            await client.ConnectAsync(3000);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await LocalPipeTransport.WriteAsync(client, message, deadline.Token);
            // Confirm queue acceptance, then disconnect without consuming the response.
            while (session.GetPendingQueueItems().Count == 0) await Task.Delay(10, deadline.Token);
        }
        using var retry = LocalPipeTransport.CreateClient(definition.PipeName, PipeDirection.InOut);
        await retry.ConnectAsync(3000);
        using var retryDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await LocalPipeTransport.WriteAsync(retry, message, retryDeadline.Token);
        Assert.Contains("true", await LocalPipeTransport.ReadAsync(retry, retryDeadline.Token));
        Assert.Equal("once.item", Assert.Single(session.GetPendingQueueItems()).InputPath);
    }

    [Theory]
    [InlineData("bare")]
    [InlineData("json")]
    [InlineData("bom")]
    public async Task LegacyPathAndQueueMessageClientsKeepWorking(string format)
    {
        var definition = Definition();
        using var session = new ConversionBatchSession(definition, new NoOpAction(), new AppLogger());
        session.Initialize([], default);
        using var client = LocalPipeTransport.CreateClient(definition.PipeName, PipeDirection.InOut);
        await client.ConnectAsync(3000);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var path = @"C:\modèles été\image 01.item";
        var message = format == "json" ? ConversionBatchSession.FormatQueueMessage(path, new Dictionary<string, string> { ["profile"] = "legacy" })
            : format == "bom" ? "\uFEFF" + path : path;
        await LocalPipeTransport.WriteAsync(client, message, deadline.Token);
        Assert.Contains("true", await LocalPipeTransport.ReadAsync(client, deadline.Token));
        var item = Assert.Single(session.GetPendingQueueItems());
        Assert.Equal(path, item.InputPath);
        if (format == "json") Assert.Equal("legacy", item.Options!["profile"]);
    }

    private static ConversionBatchSession.BatchDefinition Definition() => new(Unique, "Test", false, null, null, null,
        ".item", @"Local\" + Unique, Unique, false, _ => true, _ => [], () => []);
    private sealed class NoOpAction : IFrameShiftAction
    {
        public ActionDescriptor Descriptor => new("test", "Test", "Test");
        public Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken token) => Task.FromResult(new ActionExecutionResult(true, "Done."));
    }
}
