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
/// Deadline struct in C# computed from the current time and the timeout
/// value. The deadline is used to determine if a request has timed out.
/// </summary>
public readonly struct Deadline
{
    public Deadline(TimeSpan timeout)
    {
        Timeout = timeout;
        DeadlineTime = DateTime.UtcNow + timeout;
    }

    public TimeSpan Timeout { get; }
    public DateTime DeadlineTime { get; }

    public bool IsOverdue => DeadlineTime < DateTime.UtcNow;
}

/// <summary>
/// INTERNAL API
///
/// Consumer actor responsible for handling inbound chunks of data from the remote system.
/// </summary>
/// <remarks>
/// ConsumerController is created externally, by this actor's parent.
/// </remarks>
public sealed class InboundDeliveryHandler : UntypedActor
{
    private readonly Address _remoteAddress;
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
                        chunkedDelivery.Recipient, chunkedDelivery.ReplyTo ?? ActorRefs.NoSender, _remoteAddress);

                // deliver the message to the correct local recipient
                chunkedDelivery.Recipient.Tell(chunkedDelivery.Payload);
                // confirm messages in the buffer
                d.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
                break;
            }
            default:
                Unhandled(message);
                break;
        }
    }
}