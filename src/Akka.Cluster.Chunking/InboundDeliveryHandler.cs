// -----------------------------------------------------------------------
//  <copyright file="InboundDeliveryHandler.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Delivery;
using Akka.Event;

namespace Akka.Cluster.Chunking;

/// <summary>
/// INTERNAL API
///
/// Consumer actor responsible for handling inbound chunks of data from the remote system.
/// </summary>
public sealed class InboundDeliveryHandler : UntypedActor
{
    private readonly Address _remoteAddress;
    private readonly Cluster _cluster = Cluster.Get(Context.System);
    private readonly ILoggingAdapter _log = Context.GetLogger();

    public InboundDeliveryHandler(Address remoteAddress)
    {
        _remoteAddress = remoteAddress;
    }

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case ConsumerController.Delivery<IDeliveryProtocol> { Message: ChunkedDelivery chunkedDelivery } d:
            {
                // IF DEBUG LOGGING IS ENABLED, LOG THE PAYLOAD AND THE RECIPIENT
                if (_log.IsDebugEnabled)
                    _log.Debug("Delivering [{0}] to [{1}] from [{2}][{3}]", chunkedDelivery.Payload,
                        chunkedDelivery.Recipient, chunkedDelivery.Sender ?? ActorRefs.NoSender, _remoteAddress);

                // deliver the message to the correct local recipient
                chunkedDelivery.Recipient.Tell(chunkedDelivery.Payload);
                // confirm messages in the buffer
                d.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
                break;
            }
            case ClusterEvent.MemberRemoved removed when removed.Member.Address.Equals(_remoteAddress):
            {
                _log.Info("Remote delivery producer [{0}] removed from cluster - terminating...", _remoteAddress);
                Context.Stop(Self);
                break;
            }
            case ClusterEvent.IClusterDomainEvent:
                // ignore
                break;
            default:
                Unhandled(message);
                break;
        }
    }

    protected override void PreStart()
    {
        _cluster.Subscribe(Self, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents, typeof(ClusterEvent.MemberRemoved));
    }
}