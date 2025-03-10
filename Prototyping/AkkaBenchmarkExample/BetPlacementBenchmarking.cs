using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace AkkaBenchmarkExample
{
    #region Bookmaker Request Types

    // Abstract base class with common properties.
    public abstract class BookmakerRequestBase
    {
        public int Id { get; set; }
        public DateTime InitiatedTime { get; set; }
        public DateTime ComputationDoneTime { get; set; }
    }

    // Five distinct bookmaker request types.
    public sealed class BetanoBookmaker : BookmakerRequestBase { }
    public sealed class GetLuckyBookmaker : BookmakerRequestBase { }
    public sealed class DanskeSpilBookmaker : BookmakerRequestBase { }
    public sealed class NordicBetBookmaker : BookmakerRequestBase { }
    public sealed class LeoVegasBookmaker : BookmakerRequestBase { }

    // Factory helper to create requests based on an index.
    public static class BookmakerRequestFactory
    {
        public static BookmakerRequestBase Create(int i) =>
            (i % 5) switch
            {
                0 => new BetanoBookmaker { Id = i, InitiatedTime = DateTime.UtcNow },
                1 => new GetLuckyBookmaker { Id = i, InitiatedTime = DateTime.UtcNow },
                2 => new DanskeSpilBookmaker { Id = i, InitiatedTime = DateTime.UtcNow },
                3 => new NordicBetBookmaker { Id = i, InitiatedTime = DateTime.UtcNow },
                4 => new LeoVegasBookmaker { Id = i, InitiatedTime = DateTime.UtcNow },
                _ => throw new Exception("Unexpected case")
            };
    }

    #endregion

    #region Distributed Approach (Actors Simulation)

    // Simulated router that in a real system would route to remote actors.
    public class ActorRouter
    {
        // The constructor would normally initialize connections to remote actors.
        public ActorRouter(
            string betanoActor,
            string getLuckyActor,
            string danskeSpilActor,
            string nordicBetActor,
            string leoVegasActor)
        {
            // Set up connections to remote actors (simulation)
        }

        // For simulation we simply switch based on the type.
        public Task<BookmakerRequestBase> InvokeAsync(BookmakerRequestBase request)
        {
            // In a real actor system, you’d send the request to the proper actor.
            // Here we simulate by calling a dedicated method.
            return request switch
            {
                BetanoBookmaker _ => DistributedActorProcessor.ProcessRequestAsync(request),
                GetLuckyBookmaker _ => DistributedActorProcessor.ProcessRequestAsync(request),
                DanskeSpilBookmaker _ => DistributedActorProcessor.ProcessRequestAsync(request),
                NordicBetBookmaker _ => DistributedActorProcessor.ProcessRequestAsync(request),
                LeoVegasBookmaker _ => DistributedActorProcessor.ProcessRequestAsync(request),
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }

    // Simulated remote processing for the actor approach.
    public static class DistributedActorProcessor
    {
        public static async Task<BookmakerRequestBase> ProcessRequestAsync(BookmakerRequestBase request)
        {
            // Simulate network delay (actors are on remote machines)
            int delay = Random.Shared.Next(50, 150); // delay in milliseconds
            await Task.Delay(delay);
            request.ComputationDoneTime = DateTime.UtcNow;
            return request;
        }
    }

    // A factory for the distributed actor system (could return a configured router)
    public static class DistributedActorSystemFactory
    {
        public static ActorRouter CreateRouter()
        {
            // In a real system these would be IP addresses/actor paths
            return new ActorRouter("192.168.1.2", "192.168.1.3", "192.168.1.4", "192.168.1.5", "192.168.1.6");
        }
    }

    #endregion

    #region Load Balancer Approach

    // Simulated load balancer that forwards requests to the least busy server.
    public static class LoadBalancerProcessor
    {
        // Simulate a load balancer with multiple remote servers.
        private static readonly List<RemoteServer> Servers = new List<RemoteServer>
        {
            new RemoteServer("192.168.1.7"),
            new RemoteServer("192.168.1.8")
        };

        public static async Task<BookmakerRequestBase> ProcessRequestAsync(BookmakerRequestBase request)
        {
            // Choose the least busy server (for simulation we simply choose one at random)
            // In a real system, you would check current load metrics.
            RemoteServer chosenServer = Servers.OrderBy(s => s.CurrentLoad).First();
            chosenServer.IncrementLoad();
            var result = await chosenServer.ProcessRequestAsync(request);
            chosenServer.DecrementLoad();
            return result;
        }
    }

    // Simulated remote server for the load balancer approach.
    public class RemoteServer
    {
        public string IpAddress { get; }
        public int CurrentLoad { get; private set; } = 0;

        public RemoteServer(string ipAddress)
        {
            IpAddress = ipAddress;
        }

        public async Task<BookmakerRequestBase> ProcessRequestAsync(BookmakerRequestBase request)
        {
            // Simulate network delay with a slight additional load-balancing overhead.
            int delay = Random.Shared.Next(60, 160);
            await Task.Delay(delay);
            request.ComputationDoneTime = DateTime.UtcNow;
            return request;
        }

        public void IncrementLoad() => CurrentLoad++;
        public void DecrementLoad() => CurrentLoad = Math.Max(CurrentLoad - 1, 0);
    }

    #endregion

    #region Benchmark Classes (BenchmarkDotNet)

    // Common helper for running a benchmark test.
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

        private ActorRouter router;

        [GlobalSetup]
        public void Setup()
        {
            router = DistributedActorSystemFactory.CreateRouter();
        }

        [Benchmark]
        public async Task<List<double>> RunDistributedBenchmark()
        {
            return await BenchmarkHelper.RunBenchmarkTest(
                i => router.InvokeAsync(BookmakerRequestFactory.Create(i)),
                TotalRequests);
        }
    }

    // Single server benchmark using the load balancer approach.
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

    #region Custom Benchmark Runner (Custom Statistics)

    public static class CustomBenchmarkRunner
    {
        // Run custom benchmarks for each approach and compute statistics.
        public static async Task RunAllCustomBenchmarks()
        {
            // Define the request counts you want to test.
            int[] requestCounts = { 100, 1000, 10000 };

            Console.WriteLine("=== Custom Benchmark Results ===");
            Console.WriteLine("Approach\tRequests\tFastest (ms)\tSlowest (ms)\tAverage (ms)\tQ25 (ms)\tMedian (ms)\tQ75 (ms)");

            // Run benchmarks for each approach.
            foreach (var count in requestCounts)
            {
                // Distributed Approach (Actors)
                var distStats = await RunCustomDistributedBenchmark(count);
                // Load Balancer Approach
                var loadBalancerStats = await RunCustomLoadBalancerBenchmark(count);

                Console.WriteLine($"Distributed\t{count}\t\t{distStats.Fastest:F2}\t\t{distStats.Slowest:F2}\t\t{distStats.Average:F2}\t\t{distStats.Q25:F2}\t\t{distStats.Median:F2}\t\t{distStats.Q75:F2}");
                Console.WriteLine($"LoadBalancer\t{count}\t\t{loadBalancerStats.Fastest:F2}\t\t{loadBalancerStats.Slowest:F2}\t\t{loadBalancerStats.Average:F2}\t\t{loadBalancerStats.Q25:F2}\t\t{loadBalancerStats.Median:F2}\t\t{loadBalancerStats.Q75:F2}");
            }
        }

        // Structure to hold statistics.
        public struct Stats
        {
            public double Fastest;
            public double Slowest;
            public double Average;
            public double Q25;
            public double Median;
            public double Q75;
        }

        // Compute statistics from a list of delays.
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

        // Get quantile from a sorted list.
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

        // Distributed approach custom benchmark.
        private static async Task<Stats> RunCustomDistributedBenchmark(int totalRequests)
        {
            var delays = await BenchmarkHelper.RunBenchmarkTest(
                i => DistributedActorProcessor.ProcessRequestAsync(BookmakerRequestFactory.Create(i)),
                totalRequests);
            return ComputeStats(delays);
        }

        // Load balancer approach custom benchmark.
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
