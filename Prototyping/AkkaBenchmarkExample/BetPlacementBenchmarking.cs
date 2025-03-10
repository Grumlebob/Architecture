using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace AkkaBenchmarkExample
{
    #region Bookmaker Request Types

    public abstract class BookmakerRequestBase
    {
        public int Id { get; set; }
        public DateTime InitiatedTime { get; set; }
        public DateTime ComputationDoneTime { get; set; }
    }

    public static class IPHelper
    {
        public static List<string> IPs { get; private set; } = new List<string> { "192.168.0.138", "192.168.0.170" }; // Thor, Jacob
    }

    public sealed class BetanoBookmaker : BookmakerRequestBase { }
    public sealed class GetLuckyBookmaker : BookmakerRequestBase { }

    public static class BookmakerRequestFactory
    {
        public static BookmakerRequestBase Create(int i) =>
            (i % 2) switch
            {
                0 => new BetanoBookmaker { Id = i, InitiatedTime = DateTime.UtcNow },
                1 => new GetLuckyBookmaker { Id = i, InitiatedTime = DateTime.UtcNow },
                _ => throw new Exception("Unexpected case")
            };
    }

    #endregion

    #region Distributed Approach (Actors Simulation)

    // Simulated router that in a real system would route to remote actors.
    public static class ActorProcessor
    {
        // In this simulation the router chooses the “actor” by calling a network method.
        public static async Task<BookmakerRequestBase> ProcessRequestAsync(BookmakerRequestBase request)
        {
            // Based on the request type, choose the corresponding remote IP/port.
            // For simplicity, we assume each actor is on a different laptop.
            string ip = request switch
            {
                BetanoBookmaker _ => IPHelper.IPs[0],
                GetLuckyBookmaker _ => IPHelper.IPs[1],
                _ => throw new ArgumentOutOfRangeException()
            };

            // Assume each remote laptop listens on port 5000 for the actor-based messages.
            int port = 5000;
            return await NetworkProcessor.SendRequestAsync(ip, port, request);
        }
    }

    #endregion

    #region Load Balancer Approach

    // Simulated load balancer that forwards requests to the least busy server.
    public static class LoadBalancerProcessor
    {
        // List of remote servers (using two extra laptops, different ports if needed).
        private static readonly List<RemoteServer> Servers = new List<RemoteServer>
        {
            new RemoteServer(IPHelper.IPs[0], 5000),
            new RemoteServer(IPHelper.IPs[1], 5000)
        };

        public static async Task<BookmakerRequestBase> ProcessRequestAsync(BookmakerRequestBase request)
        {
            // For this simulation, we choose the server with the least current load.
            RemoteServer chosenServer = Servers.OrderBy(s => s.CurrentLoad).First();
            chosenServer.IncrementLoad();
            var result = await chosenServer.ProcessRequestAsync(request);
            chosenServer.DecrementLoad();
            return result;
        }
    }

    // RemoteServer uses TCP to communicate with a remote laptop.
    public class RemoteServer
    {
        public string IpAddress { get; }
        public int Port { get; }
        public int CurrentLoad { get; private set; } = 0;

        public RemoteServer(string ipAddress, int port)
        {
            IpAddress = ipAddress;
            Port = port;
        }

        public async Task<BookmakerRequestBase> ProcessRequestAsync(BookmakerRequestBase request)
        {
            // Use the shared network processor to send the request.
            return await NetworkProcessor.SendRequestAsync(IpAddress, Port, request);
        }

        public void IncrementLoad() => CurrentLoad++;
        public void DecrementLoad() => CurrentLoad = Math.Max(CurrentLoad - 1, 0);
    }

    #endregion

    #region Network Communication Helper

    // NetworkProcessor handles sending requests and receiving responses over TCP.
    public static class NetworkProcessor
    {
        // Cache persistent connections: key is "ip:port"
        private static readonly ConcurrentDictionary<string, (TcpClient client, NetworkStream stream, SemaphoreSlim semaphore)> connections
            = new ConcurrentDictionary<string, (TcpClient, NetworkStream, SemaphoreSlim)>();

        // Helper to get or create a persistent connection.
        private static async Task<(TcpClient client, NetworkStream stream, SemaphoreSlim semaphore)> GetOrCreateConnectionAsync(string ipAddress, int port)
        {
            string key = $"{ipAddress}:{port}";

            // Try to get an existing connection.
            if (connections.TryGetValue(key, out var existingConnection))
            {
                if (existingConnection.client.Connected)
                    return existingConnection;
                else
                    connections.TryRemove(key, out _);
            }

            // Create a new connection.
            var client = new TcpClient();
            await client.ConnectAsync(ipAddress, port);
            var stream = client.GetStream();
            var sem = new SemaphoreSlim(1, 1);
            var newConnection = (client, stream, sem);

            // Use GetOrAdd to ensure only one connection is created per key.
            var connectionFromDict = connections.GetOrAdd(key, newConnection);
            if (connectionFromDict.client != client)
            {
                // Another thread already created a connection. Dispose this one.
                client.Dispose();
            }
            return connectionFromDict;
        }

        public static async Task<BookmakerRequestBase> SendRequestAsync(string ipAddress, int port, BookmakerRequestBase request)
        {
            var connection = await GetOrCreateConnectionAsync(ipAddress, port);

            // Ensure one request uses the connection at a time.
            await connection.semaphore.WaitAsync();
            try
            {
                // Prepare the message (you could use JSON or another serialization format)
                string message = $"{request.Id}|{request.GetType().Name}|{request.InitiatedTime:o}";
                byte[] data = Encoding.UTF8.GetBytes(message);
                await connection.stream.WriteAsync(data, 0, data.Length);
                await connection.stream.FlushAsync();

                // Read response (assuming the response is short)
                byte[] buffer = new byte[1024];
                int bytesRead = await connection.stream.ReadAsync(buffer, 0, buffer.Length);
                string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                // Optionally, you can log the response:
                // Console.WriteLine($"Response from {ipAddress}:{port} -> {response}");

                // Mark the completion time.
                request.ComputationDoneTime = DateTime.UtcNow;
                return request;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error contacting {ipAddress}:{port} - {ex.Message}");
                // On error, remove the connection from the cache so that it can be reestablished.
                string key = $"{ipAddress}:{port}";
                connections.TryRemove(key, out _);
                throw;
            }
            finally
            {
                connection.semaphore.Release();
            }
        }
    }

    #endregion

    #region Benchmark Classes (BenchmarkDotNet)

    public static class BenchmarkHelper
    {
        public static async Task<List<double>> RunBenchmarkTest(Func<int, Task<BookmakerRequestBase>> processFunc, int totalRequests)
        {
            var tasks = new Task<BookmakerRequestBase>[totalRequests];
            for (int i = 0; i < totalRequests; i++)
                tasks[i] = processFunc(i);
            var responses = await Task.WhenAll(tasks);
            return responses.Select(m => (m.ComputationDoneTime - m.InitiatedTime).TotalMilliseconds).ToList();
        }
    }

    // Distributed benchmark using the router (actor-based) approach.
    [MemoryDiagnoser]
    public class DistributedBenchmark
    {
        [Params(100, 1000)]
        public int TotalRequests { get; set; }

        [Benchmark]
        public async Task<List<double>> RunDistributedBenchmark()
        {
            return await BenchmarkHelper.RunBenchmarkTest(
                i => ActorProcessor.ProcessRequestAsync(BookmakerRequestFactory.Create(i)),
                TotalRequests);
        }
    }

    // Load balancer benchmark using the load balancer approach.
    [MemoryDiagnoser]
    public class LoadBalancerBenchmark
    {
        [Params(100, 1000)]
        public int TotalRequests { get; set; }

        [Benchmark]
        public async Task<List<double>> RunLoadBalancerBenchmark()
        {
            return await BenchmarkHelper.RunBenchmarkTest(
                i => LoadBalancerProcessor.ProcessRequestAsync(BookmakerRequestFactory.Create(i)),
                TotalRequests);
        }
    }

    #endregion

    #region Custom Benchmark Runner

    public static class CustomBenchmarkRunner
    {
        public struct Stats
        {
            public double Fastest;
            public double Slowest;
            public double Average;
            public double Q25;
            public double Median;
            public double Q75;
        }

        public static Stats ComputeStats(List<double> delays)
        {
            delays.Sort();
            int n = delays.Count;
            double fastest = delays.First();
            double slowest = delays.Last();
            double average = delays.Average();
            double q25 = GetQuantile(delays, 0.25);
            double median = GetQuantile(delays, 0.5);
            double q75 = GetQuantile(delays, 0.75);

            return new Stats
            {
                Fastest = fastest,
                Slowest = slowest,
                Average = average,
                Q25 = q25,
                Median = median,
                Q75 = q75
            };
        }

        private static double GetQuantile(List<double> sorted, double p)
        {
            int n = sorted.Count;
            if (n == 0) return double.NaN;
            double pos = (n - 1) * p;
            int index = (int)Math.Floor(pos);
            double fraction = pos - index;
            if (index + 1 < n)
                return sorted[index] * (1 - fraction) + sorted[index + 1] * fraction;
            else
                return sorted[index];
        }

        public static async Task RunAllCustomBenchmarks()
        {
            int[] requestCounts = { 100, 1000, 10000 };

            Console.WriteLine("=== Custom Benchmark Results ===");
            Console.WriteLine("Approach\tRequests\tFastest (ms)\tSlowest (ms)\tAverage (ms)\tQ25 (ms)\tMedian (ms)\tQ75 (ms)");

            foreach (var count in requestCounts)
            {
                var distStats = await RunCustomDistributedBenchmark(count);
                var lbStats = await RunCustomLoadBalancerBenchmark(count);

                Console.WriteLine($"Distributed\t{count}\t\t{distStats.Fastest:F2}\t\t{distStats.Slowest:F2}\t\t{distStats.Average:F2}\t\t{distStats.Q25:F2}\t\t{distStats.Median:F2}\t\t{distStats.Q75:F2}");
                Console.WriteLine($"LoadBalancer\t{count}\t\t{lbStats.Fastest:F2}\t\t{lbStats.Slowest:F2}\t\t{lbStats.Average:F2}\t\t{lbStats.Q25:F2}\t\t{lbStats.Median:F2}\t\t{lbStats.Q75:F2}");
            }
        }

        private static async Task<Stats> RunCustomDistributedBenchmark(int totalRequests)
        {
            var delays = await BenchmarkHelper.RunBenchmarkTest(
                i => ActorProcessor.ProcessRequestAsync(BookmakerRequestFactory.Create(i)),
                totalRequests);
            return ComputeStats(delays);
        }

        private static async Task<Stats> RunCustomLoadBalancerBenchmark(int totalRequests)
        {
            var delays = await BenchmarkHelper.RunBenchmarkTest(
                i => LoadBalancerProcessor.ProcessRequestAsync(BookmakerRequestFactory.Create(i)),
                totalRequests);
            return ComputeStats(delays);
        }
    }

    #endregion

    #region Program Entry Point

    public class BetPlacementBenchmarking
    {
        public static async Task Main(string[] args)
        {
            // Optionally run BenchmarkDotNet tests:
            // Console.WriteLine("=== Distributed Approach ===");
            // BenchmarkRunner.Run<DistributedBenchmark>();
            // Console.WriteLine("=== Load Balancer Approach ===");
            // BenchmarkRunner.Run<LoadBalancerBenchmark>();

            // Run custom benchmarks and print summary statistics.
            Console.WriteLine("=== Custom Benchmarks ===");
            await CustomBenchmarkRunner.RunAllCustomBenchmarks();
        }
    }

    #endregion
}
