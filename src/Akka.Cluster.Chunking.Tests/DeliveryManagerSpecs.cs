// -----------------------------------------------------------------------
//  <copyright file="DeliveryManagerSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.Chunking.Configuration;
using Akka.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Chunking.Tests;

public class DeliveryManagerSpecs : TestKit.Xunit2.TestKit
{
    public static readonly Config ClusterConfig = ConfigurationFactory.ParseString(@"
        akka.actor.provider = cluster
        akka.remote.dot-netty.tcp.port = 0
    ").WithFallback(ChunkingConfiguration.DefaultHocon);
    
    public DeliveryManagerSpecs(ITestOutputHelper output) : base(ClusterConfig, output: output)
    {
        Sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
    }
    
    public ActorSystem Sys2 { get; }

    protected override void AfterAll()
    {
        base.AfterAll();
        Shutdown(Sys2);
    }

    private async Task EnsureClusterFormed()
    {
        var cluster1 = Akka.Cluster.Cluster.Get(Sys);
        var cluster2 = Akka.Cluster.Cluster.Get(Sys2);
        await cluster1.JoinSeedNodesAsync(new[] { cluster1.SelfAddress, cluster2.SelfAddress });
    }
}