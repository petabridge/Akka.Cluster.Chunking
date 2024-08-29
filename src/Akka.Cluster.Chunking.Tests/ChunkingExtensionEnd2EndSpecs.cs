// -----------------------------------------------------------------------
//  <copyright file="ChunkingExtensionEnd2EndSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.Chunking;
using Akka.Cluster.Chunking.Configuration;
using Akka.Configuration;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Chunking.Tests;

public class ChunkingExtensionEnd2EndSpecs : TestKit.Xunit2.TestKit
{
    public static readonly Config ClusterConfig = ConfigurationFactory.ParseString(@"
        akka.loglevel = DEBUG
        akka.actor.provider = cluster
        akka.remote.dot-netty.tcp.port = 0
        akka.remote.dot-netty.tcp.hostname = localhost
    ").WithFallback(ChunkingConfiguration.DefaultHocon);
    
    public ChunkingExtensionEnd2EndSpecs(ITestOutputHelper output) : base(ClusterConfig, output: output)
    {
        Sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
        InitializeLogger(Sys2);
        Settings = ChunkingManagerSettings.Create(Sys) with { ChunkSize = 1 };
    }
    
    public ActorSystem Sys2 { get; }

    public ChunkingManagerSettings Settings { get; }

    protected override void AfterAll()
    {
        base.AfterAll();
        Shutdown(Sys2);
    }

    private async Task EnsureClusterFormed()
    {
        await WithinAsync(TimeSpan.FromSeconds(20),async () =>
        {
            var cluster1 = Akka.Cluster.Cluster.Get(Sys);
            var cluster2 = Akka.Cluster.Cluster.Get(Sys2);
            cluster1.JoinSeedNodes(
                new[] { cluster1.SelfAddress, cluster2.SelfAddress });
            cluster2.JoinSeedNodes(
                new[] { cluster1.SelfAddress, cluster2.SelfAddress });

            await AwaitConditionAsync(() => cluster1.State.Members.Count == 2);
        });

    }
    
    [Fact(DisplayName = "ChunkingManager should deliver message to each-other duplex fashion")]
    public async Task ShouldDeliverMessagesDuplex()
    {
        // arrange
        await EnsureClusterFormed();

        var dm1 = ChunkingManager.For(Sys);
        var dm2 = ChunkingManager.For(Sys2);

        var probe = CreateTestProbe(Sys);
        var probe2 = CreateTestProbe(Sys2);
        
        // get RemoteActorRefs to both systems
        var node2Probe = await Sys.ActorSelection(probe2.Ref.Path.ToStringWithAddress(Sys2.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress)).ResolveOne(RemainingOrDefault);
        var node1Probe = await Sys2.ActorSelection(probe.Ref.Path.ToStringWithAddress(Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress)).ResolveOne(RemainingOrDefault);

        // have Sys message Sys2
        await dm1.DeliverChunked("hello", node2Probe, probe.Ref);
        await probe2.ExpectMsgAsync("hello");
        probe2.Reply("ok");
        await probe.ExpectMsgAsync("ok");

        // have Sys2 message Sys
        await dm2.DeliverChunked("hello3", node1Probe, probe2.Ref);
        await probe.ExpectMsgAsync("hello3");
        probe.Reply("ok3");
        await probe2.ExpectMsgAsync("ok3");
    }
    
    [Fact(DisplayName = "ChunkingManager should deliver message to each-other duplex fashion with Ask")]
    public async Task ShouldDeliverMessagesDuplexWithAsk()
    {
        // arrange
        await EnsureClusterFormed();

        var dm1 = ChunkingManager.For(Sys);
        var dm2 = ChunkingManager.For(Sys2);

        var probe = CreateTestProbe(Sys);
        var probe2 = CreateTestProbe(Sys2);
        
        // get RemoteActorRefs to both systems
        var node2Probe = await Sys.ActorSelection(probe2.Ref.Path.ToStringWithAddress(Sys2.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress)).ResolveOne(RemainingOrDefault);
        var node1Probe = await Sys2.ActorSelection(probe.Ref.Path.ToStringWithAddress(Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress)).ResolveOne(RemainingOrDefault);

        // have Sys message Sys2 (no Ask)
        await dm1.DeliverChunked("hello", node2Probe, probe.Ref);
        await probe2.ExpectMsgAsync("hello");
        probe2.Reply("ok");
        await probe.ExpectMsgAsync("ok");
        
        // have Sys message Sys2 (Ask)
        
        // needs to run as a detached Task because we're blocking the current thread
        var resultTask = dm1.AskChunked<string>("hello", node2Probe);
        await probe2.ExpectMsgAsync("hello");
        probe2.Reply("ok");
        
        var result = await resultTask;
        result.Should().Be("ok");
        
        // have Sys2 message Sys (with Ask)
        var resultTask2 = dm2.AskChunked<string>("hello2", node1Probe);
        await probe.ExpectMsgAsync("hello2");
        probe.Reply("ok2");
        
        var result2 = await resultTask2;
        result2.Should().Be("ok2");
    }
}