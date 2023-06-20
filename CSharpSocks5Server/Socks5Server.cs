// See https://aka.ms/new-console-template for more information
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Buffers;

class Socks5Server
{
    private TcpListener _listener;
    public CancellationTokenSource CancellationTokenSource { get; } = new CancellationTokenSource();
    public Socks5Server(IPEndPoint endPoint)
    {
        _listener = new TcpListener(endPoint);
    }

    static bool IsValidVersionRequest(ReadOnlySpan<byte> request)
    {
        if (request.Length < 3)
        {
            return false;
        }
        if (request[0] != 0x05) // SOCKS5
        {
            return false;
        }
        if (request[1] < 1)
        {
            return false;
        }
        if (request[2] != 0x00) // no authentication
        {
            return false;
        }
        return true;
    }
    public async ValueTask Run()
    {
        CancellationToken cancellationToken = CancellationTokenSource.Token;
        _listener.Start();
        int count = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var socket = await _listener.AcceptSocketAsync(cancellationToken).ConfigureAwait(false);
            System.Console.WriteLine($"${count} Accept {socket.RemoteEndPoint}");
            socket.SendTimeout = 1000;
            socket.ReceiveTimeout = 1000;
            socket.NoDelay = true;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            //socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            // run Task in background
            Handle(count, socket, cancellationToken);
            System.Console.WriteLine($"${count} Task Started");
            count++;
        }
    }

    static readonly ReadOnlyMemory<byte> VersionResponse = new byte[] { 0x05, 0x00 }.AsMemory();
    private static async void Handle(int count, Socket socket, CancellationToken cancellationToken)
    {
        await Task.Yield();
        using (socket)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(512);
            try
            {
                // version
                int length = await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
                System.Console.WriteLine($"${count} Receive {length} bytes from {socket.RemoteEndPoint}");

                if (!IsValidVersionRequest(buffer.AsSpan(0, length)))
                {
                    System.Console.WriteLine($"${count} Invalid Version Request from {socket.RemoteEndPoint}");
                    return;
                }
                // response
                var send = await socket.SendAsync(VersionResponse, SocketFlags.None, cancellationToken).ConfigureAwait(false);
                System.Console.WriteLine($"${count} Send {send} bytes from {socket.RemoteEndPoint}");
                if (send != VersionResponse.Length)
                {
                    System.Console.WriteLine($"${count} Send {send} bytes to {socket.RemoteEndPoint}");
                    return;
                }

                length = await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
                System.Console.WriteLine($"${count} Receive {length} bytes from {socket.RemoteEndPoint}");

                if (buffer[0] != 0x05)
                {
                    System.Console.WriteLine($"${count} Invalid Version Request from {socket.RemoteEndPoint}");
                    return;
                }
                var command = buffer[1];
                switch (command)
                {
                    case 0x01: // connect
                        await HandleConnectCommand(count, socket, buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                        break;
                    case 0x02: // bind
                    case 0x03: // udp
                    default:
                        System.Console.WriteLine($"${count} Invalid Command Request from {socket.RemoteEndPoint}");
                        return;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"${count} {ex.Message}");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
            System.Console.WriteLine($"${count} Close {socket.RemoteEndPoint}");
        }
    }
    enum CommandError : byte
    {
        succeeded = 0x00,
        generalSocksServerFailure = 0x01,
        connectionNotAllowedByRuleset = 0x02,
        networkUnreachable = 0x03,
        hostUnreachable = 0x04,
        connectionRefused = 0x05,
        ttlExpired = 0x06,
        commandNotSupported = 0x07,
        addressTypeNotSupported = 0x08,
    }
    private async static ValueTask HandleConnectCommand(int count, Socket socket, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        const bool continueOnCapturedContext = false;
        if (data.Length < 5)
        {
            System.Console.WriteLine($"${count} Invalid Command Length from {socket.RemoteEndPoint}");
            await SendResponse(count, socket, CommandError.generalSocksServerFailure, cancellationToken).ConfigureAwait(continueOnCapturedContext);
            return;
        }
        System.Console.WriteLine($"${count} HandleConnectCommand {data.Span[1]:X}/{data.Span[2]:X}/{data.Span[3]:X}/{data.Span[4]:X}");

        var command = data.Span[1];
        switch (command)
        {
            case 0x01:
                // connect
                break;
            default:
                throw new ArgumentException();
        }
        // address type
        var atyp = data.Span[3];
        IPAddress ipAddress;
        int addressLength;
        switch (atyp)
        {
            case 0x01:
                // ipv4
                addressLength = 4;
                ipAddress = new IPAddress(data.Span.Slice(4, addressLength));
                break;
            case 0x03:
                // domain
                var entry = Dns.GetHostEntry(Encoding.UTF8.GetString(data.Span.Slice(5, data.Span[4])));
                if (entry.AddressList.Length == 0)
                {
                    await SendResponse(count, socket, CommandError.hostUnreachable, cancellationToken).ConfigureAwait(continueOnCapturedContext);
                    return;
                }
                addressLength = data.Span[4] + 1;
                ipAddress = entry.AddressList[0];
                break;
            case 0x04:
                // ipv6
                addressLength = 16;
                ipAddress = new IPAddress(data.Span.Slice(4, addressLength));
                break;
            default:
                // response
                await SendResponse(count, socket, CommandError.addressTypeNotSupported, cancellationToken).ConfigureAwait(continueOnCapturedContext);
                return;
        }
        using (Socket remoteSocket = new Socket(SocketType.Stream, ProtocolType.Tcp))
        {

            var port = BitConverter.ToUInt16(data.Span.Slice(4 + addressLength, 2));
            // big endian to host endian
            port = (ushort)IPAddress.NetworkToHostOrder((short)port);
            var endPoint = new IPEndPoint(ipAddress, port);

            try
            {
                await remoteSocket.ConnectAsync(ipAddress, port, cancellationToken).ConfigureAwait(continueOnCapturedContext);
                System.Console.WriteLine($"${count} Connect {remoteSocket.RemoteEndPoint}");
            }
            catch (SocketException ex)
            {
                System.Console.WriteLine($"${count} Connect {ex.Message}");
                //await SendResponse(socket, CommandError.hostUnreachable, cancellationToken);
                return;
            }
            // response
            await SendResponse(count, socket, CommandError.succeeded, cancellationToken).ConfigureAwait(continueOnCapturedContext);

            using var tcs1 = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var tcs2 = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using Barrier barrier = new Barrier(2);
            var t1 = StartTcpRelay(count, barrier, socket, remoteSocket, tcs1, tcs2.Token);
            var t2 = StartTcpRelay(count, barrier, remoteSocket, socket, tcs2, tcs1.Token);
            Task.WaitAll(new Task[] { t1, t2, }, cancellationToken
            );

            System.Console.WriteLine($"${count} Close Remote {remoteSocket.RemoteEndPoint}");
        }

        static async ValueTask SendResponse(int count, Socket socket, CommandError result, CancellationToken cancellationToken)
        {
            const int ResponseSize = 10;
            var buffer = ArrayPool<byte>.Shared.Rent(ResponseSize);
            try
            {
                var serverEndPoint = ((IPEndPoint)(socket.LocalEndPoint!));
                (stackalloc byte[ResponseSize] {
                    0x05,
                    (byte)result, // REP
                    0x00, // RSV
                    0x01, // ATYP
                    0x00,
                    0x00,
                    0x00,
                    0x00, // ipv4 address
                    0x00,
                    0x00,// port
                }).CopyTo(buffer);
                var port = IPAddress.HostToNetworkOrder((short)serverEndPoint.Port);
                BitConverter.TryWriteBytes(buffer.AsSpan(8), port);

                var send = await socket.SendAsync(buffer.AsMemory(0, ResponseSize), SocketFlags.None, cancellationToken).ConfigureAwait(continueOnCapturedContext);
                System.Console.WriteLine($"${count} Send {send} bytes to {socket.RemoteEndPoint}");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

    }

    private static async Task StartTcpRelay(int count, Barrier startupBarrier,
        Socket upstream, Socket downstream, CancellationTokenSource otherTcs, CancellationToken cancellationToken)
    {
        const bool continueOnCapturedContext = false;
        await Task.Yield(); // switch to background
        startupBarrier.SignalAndWait();
        System.Console.WriteLine($"${count}-#{Environment.CurrentManagedThreadId} Start TcpRelay {upstream.RemoteEndPoint} <-> {downstream.RemoteEndPoint}");

        upstream.ReceiveTimeout = 5000;
        upstream.NoDelay = true;
        downstream.SendTimeout = 5000;
        downstream.NoDelay = true;

        var buffer = new byte[upstream.ReceiveBufferSize];
        try
        {
            while (upstream.Connected)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int length = await upstream.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(continueOnCapturedContext);
                if (length == 0)
                {
                    break;
                }
                System.Console.WriteLine($"${count}-#{Environment.CurrentManagedThreadId} Receive {length} bytes from {upstream.RemoteEndPoint}");

                var send = await downstream.SendAsync(buffer.AsMemory(0, length), SocketFlags.None, cancellationToken).ConfigureAwait(continueOnCapturedContext);
                System.Console.WriteLine($"${count}-#{Environment.CurrentManagedThreadId} Send {send} bytes to {downstream.RemoteEndPoint}");

            }
        }
        catch (SocketException ex) when
            (ex.SocketErrorCode == SocketError.ConnectionReset || ex.SocketErrorCode == SocketError.ConnectionAborted)
        {
            System.Console.WriteLine($"${count}-#{Environment.CurrentManagedThreadId} Closed");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"${count}-#{Environment.CurrentManagedThreadId} Exception {ex.Message}");
        }
        System.Console.WriteLine($"${count}-#{Environment.CurrentManagedThreadId} End TcpRelay {upstream.RemoteEndPoint} <-> {downstream.RemoteEndPoint}");
        otherTcs.Cancel();
    }
}
