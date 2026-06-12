using System.Net.Sockets;
using System.Text;

namespace VoiceToText.Linux.Platform;

internal static class IpcClient
{
    /// <summary>Send a command to the running daemon. Exit 0 on a reply, 1 if none is running.</summary>
    public static int Send(string command)
    {
        try
        {
            using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            s.Connect(new UnixDomainSocketEndPoint(IpcServer.SocketPath));
            s.Send(Encoding.UTF8.GetBytes(command));
            var buf = new byte[1024];
            var n = s.Receive(buf);
            Console.WriteLine(Encoding.UTF8.GetString(buf, 0, n));
            return 0;
        }
        catch (SocketException)
        {
            Console.Error.WriteLine("voicetotext is not running.");
            return 1;
        }
    }
}
