using System.Net;
using System.CommandLine;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var hostOption = new Option<IPAddress>(
            aliases: new[] { "-h", "--host" },
            getDefaultValue: () => IPAddress.Any);
        var portOption = new Option<int>(
            aliases: new[] { "-p", "--port" },
            getDefaultValue: () => 1080);
        var restrictSameNetworkOption = new Option<bool>(
            aliases: new[] { "-r", "--restrict-same-network" },
            getDefaultValue: () => false);
        var rootCommand = new RootCommand
        {
            hostOption,
            portOption,
            restrictSameNetworkOption,
        };
        rootCommand.SetHandler(
            (host, port, restrictSameNetwork) =>
            {
                var endPoint = new IPEndPoint(host, port);

                var server = new Socks5Server(endPoint);
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    server.CancellationTokenSource.Cancel();
                };
                try
                {
                    server.Run(restrictSameNetwork).AsTask().Wait();
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
            }, hostOption, portOption, restrictSameNetworkOption);
        await rootCommand.InvokeAsync(args);

    }
}