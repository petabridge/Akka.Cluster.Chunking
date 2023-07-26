// -----------------------------------------------------------------------
//  <copyright file="SenderActor.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Chunking;
using Akka.Event;

namespace SeedNode.Actors;

public sealed class SenderActor : UntypedActor, IWithTimers{

    private class SendMsg
    {
        private SendMsg() { }
        public static SendMsg Instance { get; } = new();
    }

    private readonly List<IActorRef> _targets = new();
    private readonly Cluster _cluster = Cluster.Get(Context.System);
    private readonly ILoggingAdapter _log = Context.GetLogger();

    private byte[] GenerateRandomLargeMessage(int size)
    {
        var buffer = new byte[size];
        Random.Shared.NextBytes(buffer);
        return buffer;
    }

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case SendMsg when _targets.Count > 0:
                // generate random message between 12KB and 1MB
                var randomSize = Random.Shared.Next(12 * 1024, 1000 * 1024);
                var randomMessage = GenerateRandomLargeMessage(randomSize);
                var target = _targets[Random.Shared.Next(0, _targets.Count)];
                RunTask(async () =>
                {
                    await ChunkingManager.For(Context.System).DeliverChunked(randomMessage, target, Self);
                    _log.Info("Sent message containing [{0}] bytes to [{1}]", randomMessage.Length, target);
                });
                
                break;
            case SendMsg:
                _log.Warning("No targets available to send message to.");
                break;
            case ClusterEvent.MemberUp m when !m.Member.Address.Equals(_cluster.SelfAddress): // only care about non-self nodes
                var address = m.Member.Address;
                var pathToPrinter = new RootActorPath(address) / "user" / "printer";
                RunTask(async () =>
                {
                    var printer = await Context.ActorSelection(pathToPrinter).ResolveOne(TimeSpan.FromSeconds(5));
                    _targets.Add(printer);
                    Context.Watch(printer);
                });
                break;
            case Terminated t:
                _targets.Remove(t.ActorRef);
                break;
            case ClusterEvent.IClusterDomainEvent:
                // IGNORE
                break;
            default:
                Unhandled(message);
                break;
        }
    }
    
    public SenderActor()
    {
        Timers!.StartPeriodicTimer("send-msg", SendMsg.Instance, TimeSpan.FromSeconds(2));
        _cluster.Subscribe(Self, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents, typeof(ClusterEvent.IMemberEvent));
    }

    public ITimerScheduler Timers { get; set; }
}