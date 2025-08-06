// -----------------------------------------------------------------------
//  <copyright file="ChunkingExtensionEnd2EndSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.Chunking;
using Akka.Cluster.Chunking.Configuration;
using Akka.Configuration;
using Akka.Delivery;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Chunking.Tests;

public class DeliveryManagerFailureSpecs : TestKit.Xunit2.TestKit
{
    public static readonly Config ClusterConfig = ConfigurationFactory.ParseString(@"
        akka.loglevel = DEBUG
        akka.actor.provider = cluster
        akka.remote.dot-netty.tcp.port = 0
        akka.cluster.chunking.chunk-size = 512b
    ").WithFallback(ChunkingConfiguration.DefaultHocon);
    
    public DeliveryManagerFailureSpecs(ITestOutputHelper output) : base(ClusterConfig, output: output)
    {
        Sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
        InitializeLogger(Sys2, "SYS-2: ");
        Settings = ChunkingManagerSettings.Create(Sys);
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
                [cluster1.SelfAddress, cluster2.SelfAddress]);
            cluster2.JoinSeedNodes(
                [cluster1.SelfAddress, cluster2.SelfAddress]);

            await AwaitConditionAsync(() => cluster1.State.Members.Count == 2);
        });

    }

    private IActorRef CreateDeliveryManager(
        ActorSystem system,
        Func<object, double>? inboundFuzzer = null,
        Func<object, double>? outboundFuzzer = null)
    {
        var extended = (ExtendedActorSystem)system;
        
        var settings = ChunkingManagerSettings.Create(system);
        return extended.SystemActorOf(DeliveryManager.CreatePropsWithFuzzing(settings, inboundFuzzer, outboundFuzzer), ChunkingUtilities.ChunkerActorName);
    }
    
    [Fact(DisplayName = "Should deliver messages to node after a mid-chunk consumer restart")]
    public async Task ShouldDeliverMessagesAfterMidChunkConsumerNodeRestart()
    {
        var crashed = false;
        var restarted = false;
        
        await EnsureClusterFormed();

        var dm1 = CreateDeliveryManager(Sys);
        var dm2 = CreateDeliveryManager(Sys2, inboundFuzzer: obj =>
        {
            if (restarted)
                return 0.0;
            
            if (crashed)
                return 1.0;

            if (obj is ConsumerController.SequencedMessage<IDeliveryProtocol> { SeqNr: 3 })
            {
                Output.WriteLine(">>>>>>>>>> Crashing SYS-2");
                crashed = true;
                //Sys2.Terminate();
                ((ExtendedActorSystem)Sys2).Abort();
                return 1.0;
            }

            return 0.0;
        });

        var probe = CreateTestProbe(Sys2, "chunkProbe");
        var probeRef = await GetActorRefOfRemoteRef(Sys, Sys2, probe);
        
        // big payload, this should be split into chunks
        var largePayload = new string('a', 4000);
        
        await WatchAsync(probeRef);
        
        // have Sys message Sys2
        var msg = new ChunkedDelivery(largePayload, probeRef, TestActor);
        dm1.Tell(msg, TestActor);
        ExpectMsg<DeliveryQueuedAck>(3.Seconds());
        
        // Sys2 crash by design
        await Sys2.WhenTerminated.WaitAsync(10.Seconds());
        
        var cluster1 = Akka.Cluster.Cluster.Get(Sys);
        await AwaitConditionAsync(async () =>
        {
            return cluster1.State.Members.Count == 2;
        }, max: 30.Seconds());

        restarted = true;
        // restart Sys2
        var newSys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
        var dm2New = CreateDeliveryManager(newSys2, inboundFuzzer: obj =>
        {
            if (restarted)
                return 0.0;
            
            if (crashed)
                return 1.0;

            if (obj is ConsumerController.SequencedMessage<IDeliveryProtocol> { SeqNr: 3 })
            {
                Output.WriteLine(">>>>>>>>>> Crashing SYS-2");
                crashed = true;
                //Sys2.Terminate();
                ((ExtendedActorSystem)Sys2).Abort();
                return 1.0;
            }

            return 0.0;
        });
        var probeNew = CreateTestProbe(newSys2);
        var probeRefNew = await GetActorRefOfRemoteRef(Sys, newSys2, probeNew);
        
        // rejoin cluster
        await WithinAsync(TimeSpan.FromSeconds(10), async () =>
        {
            var cluster2 = Akka.Cluster.Cluster.Get(newSys2);
            cluster2.JoinSeedNodes( [cluster1.SelfAddress, cluster2.SelfAddress] );
            await AwaitConditionAsync(() => cluster1.State.Members.Count == 3);
        });

        // Resend message, should reach the newly created Sys2
        var msg2 = new ChunkedDelivery(largePayload, probeRefNew, TestActor);
        dm1.Tell(msg2, TestActor);
        ExpectMsg<DeliveryQueuedAck>();
        
        await probeNew.ExpectMsgAsync(largePayload);
        probeNew.Reply("ok");
        await ExpectMsgAsync("ok");
    }

    [Fact(DisplayName = "Should deliver messages to node after a mid-chunk producer restart")]
    public async Task ShouldDeliverMessagesAfterMidChunkProducerNodeRestart()
    {
        var sent = 0;
        var crashed = false;
        var restarted = false;
        
        await EnsureClusterFormed();

        var dm1 = CreateDeliveryManager(Sys);
        var dm2 = CreateDeliveryManager(Sys2, outboundFuzzer: obj =>
        {
            if (restarted)
                return 0.0;
            
            if (crashed)
                return 1.0;

            if (obj.GetType().Name.Contains("SendChunk"))
                sent++;
            
            if(sent > 4)
            {
                Output.WriteLine(">>>>>>>>>> Crashing SYS-2");
                crashed = true;
                //Sys2.Terminate();
                ((ExtendedActorSystem)Sys2).Abort();
                return 1.0;
            }

            return 0.0;
        });

        var probe2 = CreateTestProbe(Sys2, "chunkProbe");
        
        var probe = CreateTestProbe("chunkProbe");
        var probeRef = await GetActorRefOfRemoteRef(Sys2, Sys, probe);
        
        // big payload, this should be split into chunks
        var largePayload = new string('a', 4000);
        
        // have Sys2 message Sys
        var msg = new ChunkedDelivery(largePayload, probeRef, probe2);
        dm2.Tell(msg, probe2);
        probe2.ExpectMsg<DeliveryQueuedAck>(3.Seconds());
        
        // Sys2 crash by design
        await Sys2.WhenTerminated.WaitAsync(10.Seconds());
        
        var cluster1 = Akka.Cluster.Cluster.Get(Sys);
        await AwaitConditionAsync(async () =>
        {
            return cluster1.State.Members.Count == 2;
        }, max: 30.Seconds());

        restarted = true;
        // restart Sys2
        var newSys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
        var dm2New = CreateDeliveryManager(newSys2, outboundFuzzer: obj =>
        {
            if (restarted)
                return 0.0;
            
            if (crashed)
                return 1.0;

            if (obj.GetType().Name.Contains("SendChunk"))
                sent++;
            
            if(sent > 4)
            {
                Output.WriteLine(">>>>>>>>>> Crashing SYS-2");
                crashed = true;
                //Sys2.Terminate();
                ((ExtendedActorSystem)Sys2).Abort();
                return 1.0;
            }

            return 0.0;
        });
        var probe2New = CreateTestProbe(newSys2);
        var probeRefNew = await GetActorRefOfRemoteRef(newSys2, Sys, probe);
        
        // rejoin cluster
        await WithinAsync(TimeSpan.FromSeconds(10), async () =>
        {
            var cluster2 = Akka.Cluster.Cluster.Get(newSys2);
            cluster2.JoinSeedNodes( [cluster1.SelfAddress, cluster2.SelfAddress] );
            await AwaitConditionAsync(() => cluster1.State.Members.Count == 3);
        });

        // Resend message
        var msg2 = new ChunkedDelivery(largePayload, probeRefNew, probe2New);
        dm2New.Tell(msg2, probe2New);
        probe2New.ExpectMsg<DeliveryQueuedAck>(3.Seconds());
        
        await probe.ExpectMsgAsync(largePayload);
        probe.Reply("ok");
        await probe2New.ExpectMsgAsync("ok");
    }
    
    
    private static async Task<IActorRef> GetActorRefOfRemoteRef(ActorSystem local, ActorSystem remote, IActorRef actor)
    {
        var cluster = Cluster.Cluster.Get(remote);
        var selection = local.ActorSelection(cluster.SelfAddress + actor.Path.ToStringWithoutAddress());
        var identity = await selection.Ask<ActorIdentity>(new Identify(0));
        var remoteRef = identity.Subject;
        remoteRef.Should().NotBeNull();
        return remoteRef;
    }
}