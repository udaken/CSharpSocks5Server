using System.Net;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Start Socks5 Server");

        var endPoint = new IPEndPoint(IPAddress.Any, 1080);

        var server = new Socks5Server(endPoint);
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            server.CancellationTokenSource.Cancel();
        };
        try
        {
            await server.Run();
        }
        catch (TaskCanceledException e) when (e.CancellationToken == server.CancellationTokenSource.Token)
        {
            // ignore
            Console.WriteLine("Shutdown Socks5 Server");
        }
        catch (OperationCanceledException e)
        {
            // ignore
            Console.WriteLine(e.Message);
        }
        Console.WriteLine("Shutdown Socks5 Server");
    }
}