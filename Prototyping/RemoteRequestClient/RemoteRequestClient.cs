using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Threading.Tasks;

public class ForwardClient
{
    private readonly string host;
    private readonly int port;

    public ForwardClient(string host, int port)
    {
        this.host = host;
        this.port = port;
    }

    public async Task SendNumbersAsync(int amount)
    {
        int number = 0;
        while (number < amount)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    await client.ConnectAsync(host, port);
                    using (NetworkStream stream = client.GetStream())
                    using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                    {
                        string message = number.ToString();
                        Console.WriteLine($"Sending: {message}");
                        await writer.WriteLineAsync(message);
                    }
                }

                number++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending number: {ex.Message}");
            }
        }
    }
}

public class Program
{
    public static async Task Main(string[] args)
    {
        // Start the client to send numbers.
        var client = new ForwardClient("192.168.21.1", 5000);
        await client.SendNumbersAsync(10000);
    }
}
