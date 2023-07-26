# Akka.Cluster.Chunking

This is a plugin for [Akka.NET](https://getakka.net/) that aims at making it easier to pass really large messages over Akka.Remote with minimal fuss.

## Why?

With Akka.NET v1.5 and earlier - there's a famous problem with [Large Messages and Sockets in Akka.Remote](https://petabridge.com/blog/large-messages-and-sockets-in-akkadotnet/), namely that this renders the system vulnerable to "[head of line blocking](https://en.wikipedia.org/wiki/Head-of-line_blocking)." 

Akka.NET v1.6 and later will work around this issue via connection multiplexing, but since v1.5 and earlier use a single TCP connection between each node: head-of-line blocking is still a problem.

Enter [Akka.Delivery](https://getakka.net/articles/actors/reliable-delivery.html) - a new feature introduced in Akka.NET v1.5 that enables reliable delivery of messages between Akka.Remote nodes and supports "chunking" of large messages at the application layer in order to eliminate head-of-line blocking.

Akka.Delivery is very reliable and powerful, but it also requires deep integration between its messaging + actor management protocol and your application.

Akka.Cluster.Chunking is a turnkey solution for head-of-line blocking that uses Akka.Delivery and Akka.Cluster to automatically establish chunked message delivery between nodes - it just works via a tiny API.

**If you have large (5mB+) messages that you need to deliver over Akka.Remote, this plugin is for you. Please note, however, that this plugin requires Akka.Cluster in order to run.**

## Use

To use, install the [Akka.Cluster.Chunking NuGet package](https://www.nuget.org/packages/Akka.Cluster.Chunking):

```
dotnet add package Akka.Cluster.Chunking --version 0.1.0
```

### Configuration

To use Akka.Cluster.Chunking, you first need to configure the plugin and automatically start it:

#### Using Akka.Hosting (Recommended)

We strongly recommend you use the [Akka.Hosting APIs](https://github.com/akkadotnet/Akka.Hosting) to configure Akka.Cluster.Chunking:

```csharp
var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((hostingContext, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables();
    })
    .ConfigureServices((hostContext, services) =>
    {
        var akkaConfig = hostContext.Configuration.GetSection("AkkaConfiguration").Get<AkkaConfig>();
        services.AddAkka(akkaConfig.ActorSystemName, builder =>
        {
            builder
                .WithRemoting(akkaConfig.RemoteSettings)
                .WithClustering(akkaConfig.ClusterSettings)
                .AddChunkingManager() // MUST LAUNCH CHUNKING PLUGIN AT STARTUP ON ALL NODES
                .WithActors((system, registry, arg3) =>
            {
                system.ActorOf(Props.Create(() => new PrinterActor()), "printer");
                system.ActorOf(Props.Create(() => new SenderActor()), "sender");
            });
        });
    })
    .Build();
    
await host.RunAsync();
```

#### Using HOCON

To configure Akka.Cluster.Chunking via pure HOCON:

```hocon
akka.cluster.chunking{
    # The size of the chunks used by Akka.Delivery between nodes
    chunk-size = 64kB
    
    # The maximum number of messages waiting to be chunked
    outbound-queue-capacity = 20
    
    # The maximum amount of time a message can be queued before it is dropped
    request-timeout = 5s
}

akka{
  extensions = ["Akka.Cluster.Chunking.ChunkingManagerExtension,Akka.Cluster.Chunking"]
  actor {
    serializers {
      cluster-chunking = "Akka.Cluster.Chunking.Serialization.ChunkingMessageSerializer, Akka.Cluster.Chunking"
    }

    serialization-bindings {
      "Akka.Cluster.Chunking.INetworkedDeliveryProtocol, Akka.Cluster.Chunking" = cluster-chunking
    }

    serialization-identifiers {
      "Akka.Cluster.Chunking.Serialization.ChunkingMessageSerializer, Akka.Cluster.Chunking" = 771
    }
  }
}
```

### APIs

To deliver a chunked message from one remote endpoint to another, make the following API call:

```csharp
// your method inside an Actor or elsewhere
public void UseChunked(ActorSystem system, object message, IActorRef recipient, IActorRef? sender = null){
    ChunkingManager chunker = ChunkingManager.For(system);

    // task completes once message is accepted by chunker, not when it's delivered
    await chunker.DeliverChunked(message, recipient, sender);
}
```

That's it - the message will be chunked over the network and delivered to the remote recipient and will be shown has having been sent by the `IActorRef` specified in the `sender` field.

## Lifecycle

This plugin is currently an experiment and is available on an as-is basis.
