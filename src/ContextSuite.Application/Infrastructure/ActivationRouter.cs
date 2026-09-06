using System.IO.Pipes;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Transport;
using ContextSuite.Runtime;

namespace ContextSuite.Application.Infrastructure;

internal sealed class ActivationRouter : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly NamedPipeServerStream _server;
    private Task? _listening;
    private bool _accepting = true;

    private ActivationRouter(NamedPipeServerStream server) { _server = server; }

    public static ActivationRouter? TryCreate()
    {
        try { return new ActivationRouter(LocalPipe.CreateServer(LocalPipe.SessionName)); }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode is 5 or 231) { return null; }
    }

    public void Start(Func<OperationRequest?, Task<ActivationReply>> receive)
    {
        _listening = ListenAsync(receive);
    }

    public void StopAccepting() { _accepting = false; }

    public static async Task ForwardAsync(OperationRequest? request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using var client = new NamedPipeClientStream(".", LocalPipe.SessionName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(timeout.Token);
        LocalPipe.VerifyPeer(client, false, null, Environment.ProcessPath!);
        await JsonFrames.WriteAsync(client, new ActivationMessage(1, request), timeout.Token);
        var reply = await JsonFrames.ReadAsync<ActivationReply>(client, timeout.Token);
        if (reply.Version != 1 || reply.RequestId != (request?.RequestId ?? Guid.Empty))
            throw new IOException("The existing application returned an invalid acknowledgement.");
        await JsonFrames.WriteAsync(client, new ActivationReceipt(1, reply.RequestId), timeout.Token);
        if (!reply.Accepted)
            throw new IOException("The existing application did not accept this selection. Try again after closing it.");
    }

    private async Task ListenAsync(Func<OperationRequest?, Task<ActivationReply>> receive)
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                await _server.WaitForConnectionAsync(_lifetime.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                LocalPipe.VerifyPeer(_server, true, null, Environment.ProcessPath!);
                var message = await JsonFrames.ReadAsync<ActivationMessage>(_server, timeout.Token);
                if (message.Version != 1) throw new InvalidDataException("Unknown activation protocol.");
                message.Request?.Validate();
                var reply = _accepting ? await receive(message.Request) :
                    new ActivationReply(1, message.Request?.RequestId ?? Guid.Empty, false, "Application is closing.");
                await JsonFrames.WriteAsync(_server, reply, timeout.Token);
                // DisconnectNamedPipe discards unread data. Wait until the client has
                // consumed the reply before disconnecting and accepting another launch.
                var receipt = await JsonFrames.ReadAsync<ActivationReceipt>(_server, timeout.Token);
                if (receipt.Version != 1 || receipt.RequestId != reply.RequestId)
                    throw new InvalidDataException("Invalid activation receipt.");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException or
                System.Text.Json.JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // One malformed or disconnected client must not stop future activation.
                if (!_lifetime.IsCancellationRequested)
                    Console.Error.WriteLine($"Activation listener rejected a client: {error.GetType().Name}.");
            }
            finally { if (_server.IsConnected) _server.Disconnect(); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_listening is not null) await _listening;
        await _server.DisposeAsync();
        _lifetime.Dispose();
    }
}
