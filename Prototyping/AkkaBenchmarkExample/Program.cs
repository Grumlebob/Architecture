using Akka.Actor;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace AkkaBenchmarkExample;

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

#region Benchmark Classes

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

#region Program Entry Point

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Distributed Approach ===");
        BenchmarkRunner.Run<DistributedBenchmark>();
        Console.WriteLine("=== Single Server Approach ===");
        BenchmarkRunner.Run<SingleServerBenchmark>();
        Console.WriteLine("=== Plain Server Approach (No Akka.NET) ===");
        BenchmarkRunner.Run<PlainServerBenchmark>();

        // Optionally, custom benchmarks can be run here.
    }
}

#endregion