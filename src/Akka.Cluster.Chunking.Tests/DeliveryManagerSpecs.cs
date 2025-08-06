// -----------------------------------------------------------------------
//  <copyright file="ChunkingExtensionEnd2EndSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.Chunking;
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
        Settings = ChunkingManagerSettings.Create(Sys);
    }
    
    public ChunkingManagerSettings Settings { get; }
    
    private IActorRef CreateDeliveryManager(ActorSystem system)
    {
        var extended = (ExtendedActorSystem)system;
        
        return extended.SystemActorOf(DeliveryManager.CreateProps(Settings), ChunkingUtilities.ChunkerActorName);
    }
    
    [Fact(DisplayName = "Should deliver message to local actor")]
    public async Task ShouldDeliverMessageToLocalActor()
    {
        // arrange
        var dm = CreateDeliveryManager(Sys);
        var probe = CreateTestProbe(Sys);
        
        // act
        var msg = new ChunkedDelivery("hello", probe.Ref);
        dm.Tell(msg);
        
        // assert
        await probe.ExpectMsgAsync("hello");
    }
}