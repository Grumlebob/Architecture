using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RemoteMessageServer
{
    public class MessageServer
    {
        private readonly int _port;
        public MessageServer(int port)
        {
            _port = port;
        }

        public async Task StartAsync()
        {
            TcpListener listener = new TcpListener(IPAddress.Any, _port);
            listener.Start();
            Console.WriteLine($"Server started on port {_port}");

            while (true)
            {
                TcpClient client = await listener.AcceptTcpClientAsync();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using NetworkStream stream = client.GetStream();
                        byte[] buffer = new byte[1024];
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        string receivedMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        Console.WriteLine($"Received: {receivedMessage}");

                        // Simply print the message and send back an acknowledgement.
                        string response = $"Ack: Received your message '{receivedMessage}'";
                        byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                        await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error handling client: {ex.Message}");
                    }
                    finally
                    {
                        client.Close();
                    }
                });
            }
        }
    }

    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Use a port passed in as an argument or default to 5000.
            int port = args.Length > 0 ? int.Parse(args[0]) : 5000;
            MessageServer server = new MessageServer(port);
            await server.StartAsync();
        }
    }
}
