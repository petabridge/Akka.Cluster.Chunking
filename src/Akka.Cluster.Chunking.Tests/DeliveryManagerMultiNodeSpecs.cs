// -----------------------------------------------------------------------
//  <copyright file="DeliveryManagerMultiNodeSpecs.cs" company="Akka.NET Project">
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

public class DeliveryManagerMultiNodeSpecs : TestKit.Xunit2.TestKit
{
    public static readonly Config ClusterConfig = ConfigurationFactory.ParseString(@"
        akka.actor.provider = cluster
        akka.remote.dot-netty.tcp.port = 0
    ").WithFallback(ChunkingConfiguration.DefaultHocon);
    
    public DeliveryManagerMultiNodeSpecs(ITestOutputHelper output) : base(ClusterConfig, output: output)
    {
        Sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
        Sys3 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
        Settings = ChunkingManagerSettings.Create(Sys);
    }
    
    public ActorSystem Sys2 { get; }
    
    public ActorSystem Sys3 { get; }
    
    public ChunkingManagerSettings Settings { get; }

    protected override void AfterAll()
    {
        base.AfterAll();
        Shutdown(Sys2);
        Shutdown(Sys3);
    }

    private async Task EnsureClusterFormed()
    {
        await WithinAsync(TimeSpan.FromSeconds(20),async () =>
        {
            var cluster1 = Akka.Cluster.Cluster.Get(Sys);
            var cluster2 = Akka.Cluster.Cluster.Get(Sys2);
            var cluster3 = Akka.Cluster.Cluster.Get(Sys3);
            cluster1.JoinSeedNodes(
                new[] { cluster1.SelfAddress, cluster2.SelfAddress, cluster3.SelfAddress });
            cluster2.JoinSeedNodes(
                new[] { cluster1.SelfAddress, cluster2.SelfAddress, cluster3.SelfAddress });
            cluster3.JoinSeedNodes(
                new[] { cluster1.SelfAddress, cluster2.SelfAddress, cluster3.SelfAddress });

            await AwaitConditionAsync(() => cluster1.State.Members.Count == 3);
        });

    }

    private IActorRef CreateDeliveryManager(ActorSystem system)
    {
        var extended = (ExtendedActorSystem)system;
        
        var settings = ChunkingManagerSettings.Create(system);
        return extended.SystemActorOf(Props.Create(() => new DeliveryManager(Settings)), ChunkingUtilities.ChunkerActorName);
    }
    
    [Fact(DisplayName = "Three nodes should deliver message to each-other duplex fashion")]
    public async Task ShouldDeliverMessagesDuplex()
    {
        // arrange
        await EnsureClusterFormed();

        var dm1 = CreateDeliveryManager(Sys);
        var dm2 = CreateDeliveryManager(Sys2);
        var dm3 = CreateDeliveryManager(Sys3);

        var probe = CreateTestProbe(Sys);
        var probe2 = CreateTestProbe(Sys2);
        var probe3 = CreateTestProbe(Sys3);
        
        // have Sys message Sys2
        var msg = new ChunkedDelivery("hello", probe2.Ref, probe.Ref);
        dm1.Tell(msg);
        await probe2.ExpectMsgAsync("hello");
        probe2.Reply("ok");
        await probe.ExpectMsgAsync("ok");
        
        // have Sys2 message Sys3
        var msg2 = new ChunkedDelivery("hello2", probe3.Ref, probe2.Ref);
        dm2.Tell(msg2);
        await probe3.ExpectMsgAsync("hello2");
        probe3.Reply("ok2");
        await probe2.ExpectMsgAsync("ok2");
        
        // have Sys3 message Sys
        var msg3 = new ChunkedDelivery("hello3", probe.Ref, probe3.Ref);
        dm3.Tell(msg3);
        await probe.ExpectMsgAsync("hello3");
        probe.Reply("ok3");
        await probe3.ExpectMsgAsync("ok3");
    }

    [Fact(DisplayName = "Should deliver messages to node after a restart")]
    public async Task ShouldDeliverMessagesAfterNodeRestart()
    {
        // arrange
        await EnsureClusterFormed();

        var dm1 = CreateDeliveryManager(Sys);
        var dm2 = CreateDeliveryManager(Sys2);
        var dm3 = CreateDeliveryManager(Sys3);

        var probe = CreateTestProbe(Sys);
        var probe2 = CreateTestProbe(Sys2);
        var probe3 = CreateTestProbe(Sys3);

        // have Sys message Sys2
        var msg = new ChunkedDelivery("hello", probe2.Ref, probe.Ref);
        dm1.Tell(msg);
        await probe2.ExpectMsgAsync("hello");
        probe2.Reply("ok");
        await probe.ExpectMsgAsync("ok");

        // shut down Sys2
        await WithinAsync(TimeSpan.FromSeconds(10), async () =>
        {
            var cluster1 = Akka.Cluster.Cluster.Get(Sys);
            Shutdown(Sys2);
            await AwaitConditionAsync(() => cluster1.State.Members.Count == 2);
        });

        // have Sys message Sys3 (validate that we didn't break connections to healthy nodes)
        var msg2 = new ChunkedDelivery("hello2", probe3.Ref, probe.Ref);
        dm1.Tell(msg2);
        await probe3.ExpectMsgAsync("hello2");
        probe3.Reply("ok2");
        await probe.ExpectMsgAsync("ok2");

        // restart Sys2
        var newSys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
        var dm2New = CreateDeliveryManager(newSys2);
        var probe2New = CreateTestProbe(newSys2);

        // rejoin cluster
        await WithinAsync(TimeSpan.FromSeconds(10), async () =>
        {
            var cluster1 = Akka.Cluster.Cluster.Get(Sys);
            var cluster2 = Akka.Cluster.Cluster.Get(newSys2);
            var cluster3 = Akka.Cluster.Cluster.Get(Sys3);
            cluster2.JoinSeedNodes(
                new[] { cluster1.SelfAddress, cluster2.SelfAddress, cluster3.SelfAddress });
            await AwaitConditionAsync(() => cluster1.State.Members.Count == 3);
        });

        // message Sys2 from Sys3
        var msg3 = new ChunkedDelivery("hello3", probe2New.Ref, probe3.Ref);
        dm3.Tell(msg3);
        await probe2New.ExpectMsgAsync("hello3");
        probe2New.Reply("ok3");
        await probe3.ExpectMsgAsync("ok3");
    }
}