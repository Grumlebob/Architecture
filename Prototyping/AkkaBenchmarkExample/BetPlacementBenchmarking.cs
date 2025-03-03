using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace AkkaBenchmarkExample
{
    #region Bookmaker Request Types

    // Abstract base class with common properties.
    public abstract class BookmakerRequestBase
    {
        public int Id { get; set; }
        public DateTime SendTime { get; set; }
        public DateTime ReceiveTime { get; set; }
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
                0 => new BetanoBookmaker { Id = i, SendTime = DateTime.UtcNow },
                1 => new GetLuckyBookmaker { Id = i, SendTime = DateTime.UtcNow },
                2 => new DanskeSpilBookmaker { Id = i, SendTime = DateTime.UtcNow },
                3 => new NordicBetBookmaker { Id = i, SendTime = DateTime.UtcNow },
                4 => new LeoVegasBookmaker { Id = i, SendTime = DateTime.UtcNow },
                _ => throw new Exception("Unexpected case")
            };
    }

    #endregion

    #region Distributed Approach Actors

    // Generic actor for a specific bookmaker that simulates 2 seconds of work.
    public class BookmakerActor<T> : ReceiveActor where T : BookmakerRequestBase
    {
        public BookmakerActor()
        {
            // Use ReceiveAsync to simulate work.
            ReceiveAsync<T>(async msg =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                msg.ReceiveTime = DateTime.UtcNow;
                Sender.Tell(msg);
            });
        }
    }

    // Router actor that forwards requests to the correct bookmaker actor.
    public class BookmakerRouter : ReceiveActor
    {
        public BookmakerRouter(
            IActorRef betanoActor,
            IActorRef getLuckyActor,
            IActorRef danskeSpilActor,
            IActorRef nordicBetActor,
            IActorRef leoVegasActor)
        {
            Receive<BookmakerRequestBase>(msg =>
            {
                switch (msg)
                {
                    case BetanoBookmaker _:
                        betanoActor.Forward(msg);
                        break;
                    case GetLuckyBookmaker _:
                        getLuckyActor.Forward(msg);
                        break;
                    case DanskeSpilBookmaker _:
                        danskeSpilActor.Forward(msg);
                        break;
                    case NordicBetBookmaker _:
                        nordicBetActor.Forward(msg);
                        break;
                    case LeoVegasBookmaker _:
                        leoVegasActor.Forward(msg);
                        break;
                    default:
                        Unhandled(msg);
                        break;
                }
            });
        }
    }

    // Factory for the distributed approach (router with five dedicated actors).
    public static class DistributedActorSystemFactory
    {
        public static (ActorSystem System, IActorRef Router) CreateSystemAndRouter()
        {
            var system = ActorSystem.Create("DistributedBookmakerSystem");

            var betanoActor = system.ActorOf(Props.Create(() => new BookmakerActor<BetanoBookmaker>()), "betanoActor");
            var getLuckyActor = system.ActorOf(Props.Create(() => new BookmakerActor<GetLuckyBookmaker>()), "getLuckyActor");
            var danskeSpilActor = system.ActorOf(Props.Create(() => new BookmakerActor<DanskeSpilBookmaker>()), "danskeSpilActor");
            var nordicBetActor = system.ActorOf(Props.Create(() => new BookmakerActor<NordicBetBookmaker>()), "nordicBetActor");
            var leoVegasActor = system.ActorOf(Props.Create(() => new BookmakerActor<LeoVegasBookmaker>()), "leoVegasActor");

            var router = system.ActorOf(Props.Create(() => new BookmakerRouter(
                betanoActor, getLuckyActor, danskeSpilActor, nordicBetActor, leoVegasActor)), "router");

            return (system, router);
        }
    }

    #endregion

    #region Single Server Approach Actors

    // A single server actor that handles all requests and simulates 2 seconds of work per request.
    public class SingleServerActor : ReceiveActor
    {
        public SingleServerActor()
        {
            ReceiveAsync<BookmakerRequestBase>(async msg =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                msg.ReceiveTime = DateTime.UtcNow;
                Sender.Tell(msg);
            });
        }
    }

    // Factory for the single server approach.
    public static class SingleServerActorSystemFactory
    {
        public static (ActorSystem System, IActorRef Server) CreateSystemAndServer()
        {
            var system = ActorSystem.Create("SingleServerSystem");
            var server = system.ActorOf(Props.Create(() => new SingleServerActor()), "server");
            return (system, server);
        }
    }

    #endregion

    #region Plain Server Approach (No Akka.NET)

    // A simple in-memory processor that simulates 2 seconds of work per request.
    public static class PlainServerProcessor
    {
        public static async Task<BookmakerRequestBase> ProcessRequestAsync(BookmakerRequestBase req)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            req.ReceiveTime = DateTime.UtcNow;
            return req;
        }
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
            return responses.Select(m => (m.ReceiveTime - m.SendTime).TotalMilliseconds).ToList();
        }
    }

    // Distributed benchmark using the router approach.
    [MemoryDiagnoser]
    public class DistributedBenchmark
    {
        private ActorSystem system;
        private IActorRef router;

        [Params(100, 1000, 10000)]
        public int TotalRequests { get; set; }

        [GlobalSetup]
        public void Setup() =>
            (system, router) = DistributedActorSystemFactory.CreateSystemAndRouter();

        [GlobalCleanup]
        public void Cleanup() =>
            system.Terminate().Wait();

        [Benchmark]
        public async Task<List<double>> RunDistributedBenchmark() =>
            await BenchmarkHelper.RunBenchmarkTest(
                i => router.Ask<BookmakerRequestBase>(BookmakerRequestFactory.Create(i)), TotalRequests);
    }

    // Single server benchmark using a single actor.
    [MemoryDiagnoser]
    public class SingleServerBenchmark
    {
        private ActorSystem system;
        private IActorRef server;

        [Params(100, 1000, 10000)]
        public int TotalRequests { get; set; }

        [GlobalSetup]
        public void Setup() =>
            (system, server) = SingleServerActorSystemFactory.CreateSystemAndServer();

        [GlobalCleanup]
        public void Cleanup() =>
            system.Terminate().Wait();

        [Benchmark]
        public async Task<List<double>> RunSingleServerBenchmark() =>
            await BenchmarkHelper.RunBenchmarkTest(
                i => server.Ask<BookmakerRequestBase>(BookmakerRequestFactory.Create(i)), TotalRequests);
    }

    // Plain server benchmark without using Akka.NET.
    [MemoryDiagnoser]
    public class PlainServerBenchmark
    {
        [Params(100, 1000, 10000)]
        public int TotalRequests { get; set; }

        [Benchmark]
        public async Task<List<double>> RunPlainServerBenchmark() =>
            await BenchmarkHelper.RunBenchmarkTest(
                i => PlainServerProcessor.ProcessRequestAsync(BookmakerRequestFactory.Create(i)), TotalRequests);
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
                // Distributed Approach
                var distStats = await RunCustomDistributedBenchmark(count);
                // Single Server Approach
                var singleStats = await RunCustomSingleServerBenchmark(count);
                // Plain Server Approach
                var plainStats = await RunCustomPlainServerBenchmark(count);

                Console.WriteLine($"Distributed\t{count}\t\t{distStats.Fastest:F2}\t\t{distStats.Slowest:F2}\t\t{distStats.Average:F2}\t\t{distStats.Q25:F2}\t\t{distStats.Median:F2}\t\t{distStats.Q75:F2}");
                Console.WriteLine($"SingleSrv\t{count}\t\t{singleStats.Fastest:F2}\t\t{singleStats.Slowest:F2}\t\t{singleStats.Average:F2}\t\t{singleStats.Q25:F2}\t\t{singleStats.Median:F2}\t\t{singleStats.Q75:F2}");
                Console.WriteLine($"PlainSrv\t{count}\t\t{plainStats.Fastest:F2}\t\t{plainStats.Slowest:F2}\t\t{plainStats.Average:F2}\t\t{plainStats.Q25:F2}\t\t{plainStats.Median:F2}\t\t{plainStats.Q75:F2}");
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
            var (system, router) = DistributedActorSystemFactory.CreateSystemAndRouter();
            var delays = await BenchmarkHelper.RunBenchmarkTest(
                i => router.Ask<BookmakerRequestBase>(BookmakerRequestFactory.Create(i)), totalRequests);
            var stats = ComputeStats(delays);
            await system.Terminate();
            return stats;
        }

        // Single server approach custom benchmark.
        private static async Task<Stats> RunCustomSingleServerBenchmark(int totalRequests)
        {
            var (system, server) = SingleServerActorSystemFactory.CreateSystemAndServer();
            var delays = await BenchmarkHelper.RunBenchmarkTest(
                i => server.Ask<BookmakerRequestBase>(BookmakerRequestFactory.Create(i)), totalRequests);
            var stats = ComputeStats(delays);
            await system.Terminate();
            return stats;
        }

        // Plain server approach custom benchmark.
        private static async Task<Stats> RunCustomPlainServerBenchmark(int totalRequests)
        {
            var delays = await BenchmarkHelper.RunBenchmarkTest(
                i => PlainServerProcessor.ProcessRequestAsync(BookmakerRequestFactory.Create(i)), totalRequests);
            return ComputeStats(delays);
        }
    }

    #endregion

    #region Program Entry Point

    public class BetPlacementBenchmarking
    {
        public static async Task Main(string[] args)
        {
            // Run BenchmarkDotNet tests.
            Console.WriteLine("=== Distributed Approach ===");
            BenchmarkRunner.Run<DistributedBenchmark>();
            Console.WriteLine("=== Single Server Approach ===");
            BenchmarkRunner.Run<SingleServerBenchmark>();
            Console.WriteLine("=== Plain Server Approach (No Akka.NET) ===");
            BenchmarkRunner.Run<PlainServerBenchmark>();

            // Run custom benchmarks and print summary statistics.
            await CustomBenchmarkRunner.RunAllCustomBenchmarks();
        }
    }

    #endregion
}
