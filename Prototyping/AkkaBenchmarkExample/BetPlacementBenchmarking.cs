//using Akka.Actor;
using System.Security.Cryptography.X509Certificates;
using BenchmarkDotNet.Attributes;

namespace AkkaBenchmarkExample;

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

#region Distributed Approach Actors



// Router actor that forwards requests to the correct bookmaker actor.
public class BookmakerRouter
{
    public BookmakerRouter(
        string betanoActor,
        string getLuckyActor,
        string danskeSpilActor,
        string nordicBetActor,
        string leoVegasActor)
    {
        // set up connections to actors
    }
    public void Invoke(object msg)
    {
        // Create five dedicated actors for each bookmaker.
        switch (msg)
        {
            case BetanoBookmaker _:
                //send to Betano Actor
                break;
            case GetLuckyBookmaker _:
                //send to GetLucky Actor
                break;
            case DanskeSpilBookmaker _:
                // send to DanskeSpil Actor
                break;
            case NordicBetBookmaker _:
                //send to NordicBet Actor
                break;
            case LeoVegasBookmaker _:
                // send to LeoVegas Actor
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

    }

}

// Factory for the distributed approach (router with five dedicated actors).
public static class DistributedActorSystemFactory
{

}


#endregion

#region LoadBalancer. Send to the least busy server.



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

// Distributed benchmark using the router approach.
[MemoryDiagnoser]
public class DistributedBenchmark
{

    [Params(100, 1000)]
    public int TotalRequests { get; set; }

    //[GlobalSetup]
    //public void Setup() => 

    //[GlobalCleanup]
    //public void Cleanup() =>

    //[Benchmark]
    //public async Task<List<double>> RunDistributedBenchmark() =>
}

// Single server benchmark using a single actor.
[MemoryDiagnoser]
public class LoadBalancerBenchmark
{

    [Params(100, 1000)]
    public int TotalRequests { get; set; }

    //[GlobalSetup]
    //public void Setup() =>

    //[GlobalCleanup]
    //public void Cleanup() =>

    //[Benchmark]
    //public async Task<List<double>> RunLoadBalancerBenchmark() =>
}



#endregion

#region Custom Benchmark Runner (Custom Statistics)

public static class CustomBenchmarkRunner
{
    // Run custom benchmarks for each approach and compute statistics.
    public static async Task RunAllCustomBenchmarks()
    {
        // Define the request counts you want to test.
        int[] requestCounts = [100, 1000, 10000];

        Console.WriteLine("=== Custom Benchmark Results ===");
        Console.WriteLine("Approach\tRequests\tFastest (ms)\tSlowest (ms)\tAverage (ms)\tQ25 (ms)\tMedian (ms)\tQ75 (ms)");

        // Run benchmarks for each approach.
        foreach (var count in requestCounts)
        {
            // Distributed Approach
            var distStats = await RunCustomDistributedBenchmark(count);
            // Single Server Approach
            var loadBalancerStats = await RunCustomLoadBalancerBenchmark(count);

            Console.WriteLine($"Distributed\t{count}\t\t{distStats.Fastest:F2}\t\t{distStats.Slowest:F2}\t\t{distStats.Average:F2}\t\t{distStats.Q25:F2}\t\t{distStats.Median:F2}\t\t{distStats.Q75:F2}");

            Console.WriteLine($"SingleSrv\t{count}\t\t{loadBalancerStats.Fastest:F2}\t\t{loadBalancerStats.Slowest:F2}\t\t{loadBalancerStats.Average:F2}\t\t{loadBalancerStats.Q25:F2}\t\t{loadBalancerStats.Median:F2}\t\t{loadBalancerStats.Q75:F2}");
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
        //...
        //return stats;
    }

    // Single server approach custom benchmark.
    private static async Task<Stats> RunCustomLoadBalancerBenchmark(int totalRequests)
    {
        //...
        //return stats;
    }

    // Plain server approach custom benchmark.
    private static async Task<Stats> RunCustomPlainServerBenchmark(int totalRequests)
    {
        //var delays = await BenchmarkHelper.RunBenchmarkTest(
        //    i => PlainServerProcessor.ProcessRequestAsync(BookmakerRequestFactory.Create(i)), totalRequests);
        //return ComputeStats(delays);
    }
}

#endregion

#region Program Entry Point

public class BetPlacementBenchmarking
{
    public static async Task Main(string[] args)
    {
        /*/ Run BenchmarkDotNet tests.
        Console.WriteLine("=== Distributed Approach ===");
        BenchmarkRunner.Run<DistributedBenchmark>();
        Console.WriteLine("=== Single Server Approach ===");
        BenchmarkRunner.Run<LoadBalancerBenchmark>();
        */
        // Run custom benchmarks and print summary statistics.
        Console.WriteLine("=== Custom Benchmarks ===");
        await CustomBenchmarkRunner.RunAllCustomBenchmarks();
    }
}

#endregion
