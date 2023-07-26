using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Cluster.Chunking.Configuration;
using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Hosting;
using Akka.Remote.Hosting;
using Akka.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SeedNode;
using SeedNode.Actors;

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