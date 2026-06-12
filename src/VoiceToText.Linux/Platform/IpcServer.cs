using System.Net.Sockets;
using System.Text;
using VoiceToText.Diagnostics;

namespace VoiceToText.Linux.Platform;

/// <summary>
/// Single-instance guard + command channel: a unix socket in $XDG_RUNTIME_DIR.
/// If a live instance owns the socket, <see cref="Start"/> returns false. A stale
/// socket file (after a crash) is detected by a failed connect and removed.
/// </summary>
public sealed class IpcServer(Func<string, string> handleCommand) : IDisposable
{
    public static string SocketPath
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();
            return Path.Combine(dir, "voicetotext.sock");
        }
    }

    private Socket? _listener;

    public bool Start()
    {
        if (File.Exists(SocketPath))
        {
            // Live instance or stale file? A connect attempt tells us.
            try
            {
                using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                probe.Connect(new UnixDomainSocketEndPoint(SocketPath));
                return false; // someone is listening — we are the second instance
            }
            catch (SocketException)
            {
                try { File.Delete(SocketPath); } catch { /* race: another starter won */ }
            }
        }

        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
        _listener.Listen(4);
        _ = AcceptLoopAsync(_listener);
        return true;
    }

    private async Task AcceptLoopAsync(Socket listener)
    {
        while (true)
        {
            Socket client;
            try
            {
                client = await listener.AcceptAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    using var c = client;
                    var buf = new byte[256];
                    var n = await c.ReceiveAsync(buf).ConfigureAwait(false);
                    var cmd = Encoding.UTF8.GetString(buf, 0, n).Trim();
                    var reply = handleCommand(cmd);
                    await c.SendAsync(Encoding.UTF8.GetBytes(reply)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Error("IPC request failed", ex);
                }
            });
        }
    }

    public void Dispose()
    {
        _listener?.Dispose();
        try { File.Delete(SocketPath); } catch { /* best effort */ }
    }
}
