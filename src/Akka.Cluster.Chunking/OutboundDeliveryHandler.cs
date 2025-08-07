// -----------------------------------------------------------------------
//  <copyright file="OutboundDeliveryHandler.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Delivery;
using Akka.Event;
using Akka.Util;

namespace Akka.Cluster.Chunking;

/// <summary>
/// INTERNAL API
///
/// Producer actor responsible for transmitting outbound chunks of data to remote system.
/// </summary>
internal sealed class OutboundDeliveryHandler : UntypedActor, IWithStash
{
    private readonly Address _remoteAddress;
    private readonly Address _selfAddress;
    private readonly int _chunkSize;
    private readonly int _capacity;
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly TimeSpan _requestTimeout;

    private readonly Queue<(ChunkedDelivery delivery, IActorRef sender, Deadline timeout)> _pendingDeliveries;

    private IActorRef? _producer = null;
    private ProducerController.RequestNext<IDeliveryProtocol>? _requestNext = null;
    private readonly Func<object, double>? _fuzzingControl;

    public OutboundDeliveryHandler(Address remoteAddress, Address selfAddress, int chunkSize, TimeSpan requestTimeout, int queueCapacity = 20, Func<object, double>? fuzzingControl = null)
    {
        _remoteAddress = remoteAddress;
        _chunkSize = chunkSize;
        _selfAddress = selfAddress;
        _requestTimeout = requestTimeout;
        _capacity = queueCapacity;
        _fuzzingControl = fuzzingControl;
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
            case RegisterConsumer r:
                _producer.Tell(new ProducerController.RegisterConsumer<IDeliveryProtocol>(r.ConsumerController));
                Sender.Tell(RegisterAck.Instance); // close the loop
                Become(Ready);
                Stash.UnstashAll();
                break;
            default:
                Stash.Stash();
                break;
        }
    }

    private void Ready(object message)
    {
        switch (message)
        {
            case ProducerController.RequestNext<IDeliveryProtocol> requestNext:
                while (_pendingDeliveries.TryDequeue(out var tuple))
                {
                    var (del, sender, timeout) = tuple;
                    if (timeout.IsOverdue) // uh-oh - took too long to reach this message
                    {
                        TimeoutMsg(del, sender);
                        continue; // dequeue next message
                    }
                    
                    // we have a valid message to send
                    DeliverMessage(requestNext, del, sender);
                    return;
                }
                
                // if we made it this far - we have demand without anything to deliver. Cache it.
                _requestNext = requestNext;
                
                break;
            case ChunkedDelivery chunkedDelivery when _requestNext != null:
            {
                // have bandwidth - can deliver right away without queueing
                DeliverMessage(_requestNext, chunkedDelivery, Sender);
                _requestNext = null; // need to clear so we don't get false positives
                break;
            }
            case ChunkedDelivery chunkedDelivery:
            {
                QueueDelivery(chunkedDelivery);
                break;
            }
            case Terminated t:
            {
                // ProducerController died, for some reason
                _log.Warning("ProducerController terminated unexpectedly. Recreating...");
                
                // recreate the Producer without dropping queued messages
                _producer = CreateProducer();
                break;
            }
            case RegisterConsumer r: // replacing the consumer controller
                _producer.Tell(new ProducerController.RegisterConsumer<IDeliveryProtocol>(r.ConsumerController));
                break;
        }
    }

    private void QueueDelivery(ChunkedDelivery chunkedDelivery)
    {
        // no bandwidth - have to queue
        if (_pendingDeliveries.Count < _capacity)
        {
            // queue has room - can add items
            _pendingDeliveries.Enqueue((chunkedDelivery, Sender, new Deadline(_requestTimeout)));
        }
        else
        {
            // queue is full - check to see if there are expired items at the front
            while (_pendingDeliveries.TryPeek(out var tuple) && tuple.timeout.IsOverdue)
            {
                _pendingDeliveries.TryDequeue(out _); // remove the expired item
                TimeoutMsg(tuple.delivery, tuple.sender);
            }

            // now check to see if there's room in the queue
            if (_pendingDeliveries.Count < _capacity)
            {
                // queue has room - can add items
                _pendingDeliveries.Enqueue((chunkedDelivery, Sender, new Deadline(_requestTimeout)));
            }
            else
            {
                // queue is still full - we have to drop the message
                var msg =
                    $"Unable to deliver [{chunkedDelivery}] to [{chunkedDelivery.Recipient}] from [{chunkedDelivery.ReplyTo}] - queue is full.";
                _log.Warning(msg);
                Context.System.DeadLetters.Tell(chunkedDelivery.Payload);
                Sender.Tell(new DeliveryQueuedNack(DeliveryNackReason.BufferFull, msg));
            }
        }
    }

    private void TimeoutMsg(ChunkedDelivery del, IActorRef sender)
    {
        var msg = $"{del} timed out waiting for delivery [{_requestTimeout}]";
        _log.Warning(msg);
        Context.System.DeadLetters.Tell(del);
        sender.Tell(new DeliveryQueuedNack(DeliveryNackReason.Timeout, msg));
    }

    private void DeliverMessage(ProducerController.RequestNext<IDeliveryProtocol> requestNext, ChunkedDelivery del, IActorRef sender)
    {
        requestNext.SendNextTo.Tell(del);
        sender.Tell(DeliveryQueuedAck.Instance);
        if (_log.IsDebugEnabled)
            _log.Debug("Began chunking [{0}] to [{1}] from [{2}][{3}]", del.Payload,
                del.Recipient, del.ReplyTo ?? ActorRefs.NoSender, _remoteAddress);
    }

    protected override void PreStart()
    {
        _producer = CreateProducer();
    }

    private IActorRef CreateProducer()
    {
        // create ProducerController<IDeliveryProtocol>
        var producerId = ChunkingUtilities.ComputeProducerId(_selfAddress, _remoteAddress);
        var producerControllerSettings = ProducerController.Settings.Create(Context.System) with
        {
            ChunkLargeMessagesBytes = _chunkSize
        };
        var producerControllerProps =
            ProducerController.CreateWithFuzzing<IDeliveryProtocol>(Context.System, producerId, _fuzzingControl!,
                Option<Props>.None, producerControllerSettings);

        var producer = Context.ActorOf(producerControllerProps, "producerController");
        Context.Watch(producer);
        
        // register ourselves for message production
        producer.Tell(new ProducerController.Start<IDeliveryProtocol>(Self));
        return producer;
    }

    public IStash Stash { get; set; } = null!;

    protected override void PreRestart(Exception reason, object message)
    {
        // fail all messages in the queue
        while (_pendingDeliveries.TryDequeue(out var tuple))
        {
            var (del, sender, _) = tuple;
            var msg = $"{del} not being delivered due to restart of OutboundDeliveryHandler.";
            Context.System.DeadLetters.Tell(del);
            sender.Tell(new DeliveryQueuedNack(DeliveryNackReason.SendingTerminated, msg));
        }
    }
    
    protected override void PostStop()
    {
        // fail all messages in the queue
        while (_pendingDeliveries.TryDequeue(out var tuple))
        {
            var (del, sender, _) = tuple;
            var msg = $"{del} not being delivered due to shutdown of OutboundDeliveryHandler.";
            sender.Tell(new DeliveryQueuedNack(DeliveryNackReason.SendingTerminated, msg));
            Context.System.DeadLetters.Tell(del);
        }
    }
}