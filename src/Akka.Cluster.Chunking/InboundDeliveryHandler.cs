// -----------------------------------------------------------------------
//  <copyright file="InboundDeliveryHandler.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Delivery;
using Akka.Event;
using Akka.Util;
using static Akka.Cluster.Chunking.ChunkingUtilities;

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
/// Producer actor responsible for transmitting outbound chunks of data to remote system.
/// </summary>
public sealed class OutboundDeliveryHandler : UntypedActor, IWithStash
{
    private readonly Address _remoteAddress;
    private readonly Address _selfAddress;
    private readonly int _chunkSize;
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly TimeSpan _requestTimeout;

    private Queue<(ChunkedDelivery delivery, IActorRef sender, Deadline timeout)> _pendingDeliveries;

    private IActorRef _producer;

    public OutboundDeliveryHandler(Address remoteAddress, Address selfAddress, int chunkSize, TimeSpan requestTimeout, int queueCapacity = 20)
    {
        _remoteAddress = remoteAddress;
        _chunkSize = chunkSize;
        _selfAddress = selfAddress;
        _requestTimeout = requestTimeout;
        _pendingDeliveries = new(queueCapacity);
        
        // assert that ChunkSize must be at least 512b
        if (_chunkSize < 512)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be at least 512b.");
    }

    // waiting for ConsumerController
    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case ReG
        }
    }

    private void Ready(object message)
    {
        
    }

    protected override void PreStart()
    {
        _producer = CreateProducer();
    }

    private IActorRef CreateProducer()
    {
        // create ProducerController<IDeliveryProtocol>
        var producerId = ComputeProducerId(_selfAddress, _remoteAddress);
        var producerControllerSettings = ProducerController.Settings.Create(Context.System) with
        {
            ChunkLargeMessagesBytes = _chunkSize
        };
        var producerControllerProps =
            ProducerController.Create<IDeliveryProtocol>(Context.System, producerId, Option<Props>.None,
                producerControllerSettings);

        var producer = Context.ActorOf(producerControllerProps, "producerController");
        Context.Watch(producer);
        
        // register ourselves for message production
        producer.Tell(new ProducerController.Start<IDeliveryProtocol>(Self));
    }

    public IStash Stash { get; set; } = null!;
}

/// <summary>
/// INTERNAL API
///
/// Consumer actor responsible for handling inbound chunks of data from the remote system.
/// </summary>
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
                        chunkedDelivery.Recipient, chunkedDelivery.Sender ?? ActorRefs.NoSender, _remoteAddress);

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