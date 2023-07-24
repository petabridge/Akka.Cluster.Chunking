// -----------------------------------------------------------------------
//  <copyright file="ChunkedInboundAssociation.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Delivery;
using Akka.Remote.Transport;
using Akka.Util;
using Akka.Util.Internal;
using static Akka.Remote.Chunking.ChunkingTransportManager;

namespace Akka.Remote.Chunking;

/// <summary>
/// INTERNAL API
///
/// Actor responsible for handling the outbound chunking of messages to the remote transport
/// </summary>
internal sealed class ChunkedAssociationOwner : ReceiveActor
{
    #region Internal Messages

    /// <summary>
    /// Producer died for some reason - we're going to assume due to a transient error and try to restart it.
    /// </summary>
    public sealed class ProducerDied
    {
        public ProducerDied(IActorRef producer)
        {
            Producer = producer;
        }

        public IActorRef Producer { get; }
    }

    #endregion 
    
    private static readonly AtomicCounterLong ProducerIdCounter = new AtomicCounterLong(0L);
    public const string ProducerControllerName = "chunkProducer";
    public const string ConsumerControllerName = "chunkConsumer";
    
    public static string ComputeProducerId(Address localAddress, Address remoteAddress)
    {
        // we mix in a counter here to ensure that ProducerController restarts are handled properly
        return Uri.EscapeDataString($"p-{localAddress}-{remoteAddress}-{ProducerIdCounter.IncrementAndGet()}");
    }
    
    public static string ComputeConsumerId(Address localAddress, Address remoteAddress)
    {
        // no need to randomize consumer IDs
        return Uri.EscapeDataString($"c-{localAddress}-{remoteAddress}");
    }
    
    /// <summary>
    /// ChunkedAssociation actors and their producer/consumer controllers need to have predictable names
    /// in order for this to work during association.
    /// </summary>
    /// <param name="localAddress">Our local address</param>
    /// <param name="remoteAddress">Their remote address.</param>
    /// <returns>A Uri-escaped actor name.</returns>
    public static string ComputeChunkedAssociationName(Address localAddress, Address remoteAddress)
    {
        return Uri.EscapeDataString($"{NakedAddress(localAddress)}-{NakedAddress(remoteAddress)}");
    }
    
    /// <summary>
    /// Used to compute the remote path for the ProducerController / ConsumerController on the remote side.
    /// </summary>
    public static string ComputeRemoteControllerPath(Address localAddress, Address remoteAddress, IActorRef chunkingManager, string actorName)
    {
        // have to swap local and remote for the remote path
        var chunkedAssociationName = ComputeChunkedAssociationName(remoteAddress, localAddress);
        return (chunkingManager.Path / chunkedAssociationName / actorName).ToStringWithAddress(remoteAddress);
    }

    /// <summary>
    /// passed in externally in order to ensure that the TransportHandle can signal 
    /// </summary>
    private readonly AtomicBoolean _writeAvailable;

    private readonly IActorRef _manager;
    private readonly IAssociationEventListener _listener;
    private readonly AssociationHandle _originalHandle;
    private readonly int _maxChunkSize;
    private bool _isInboundParty;

    private IActorRef _consumer; // the consumer actor
    private IActorRef _consumerController; // consumer controller
    private IActorRef _producerController; // producer controller (WE are the producer)

    public ChunkedAssociationOwner(AtomicBoolean writeAvailable, IActorRef manager, IAssociationEventListener listener,
        AssociationHandle originalHandle, bool isInboundParty, int maxChunkSize)
    {
        _writeAvailable = writeAvailable;
        _manager = manager;
        _listener = listener;
        _originalHandle = originalHandle;
        _isInboundParty = isInboundParty;
        _maxChunkSize = maxChunkSize;
    }

    protected override void PreStart()
    {
        // create ChunkedInboundAssociation
        var props = Props.Create(() => new ChunkedInboundAssociation(_listener, _originalHandle));
        _consumer = Context.ActorOf(props, "inboundConsumer");

        /*
         * All producers and consumers are designed to handle byte arrays
         */
        CreateAndStartProducerController();

        var consumerSettings = ConsumerController.Settings.Create(Context.System);
        var consumerControllerProps =
            ConsumerController.Create<byte[]>(Context, Option<IActorRef>.None, consumerSettings);
        _consumerController = Context.ActorOf(consumerControllerProps, ConsumerControllerName);
        _consumerController.Tell(new ConsumerController.Start<byte[]>(_consumer));
    }

    private void CreateAndStartProducerController()
    {
        var producerSettings = ProducerController.Settings.Create(Context.System) with
        {
            ChunkLargeMessagesBytes =
            _maxChunkSize // critical: have to set chunk size or it defeats the purpose of this entire plugin
        };


        var producerController = ProducerController.Create<byte[]>(Context,
            ComputeProducerId(_originalHandle.LocalAddress, _originalHandle.RemoteAddress), Option<Props>.None,
            producerSettings);

        _producerController = Context.ActorOf(producerController, ProducerControllerName);

        // register ourselves as the Producer
        _producerController.Tell(new ProducerController.Start<byte[]>(Self));

        if (_isInboundParty)
        {
            // compute address of the ConsumerController<byte> on the other side and register it with this producer
            var consumerControllerPath = ComputeRemoteControllerPath(_originalHandle.LocalAddress,
                _originalHandle.RemoteAddress, _manager, ConsumerControllerName);
            Context.ActorSelection(consumerControllerPath)
                .Tell(new ConsumerController.RegisterToProducerController<byte[]>(_producerController));
        }
    }
}

/// <summary>
/// INTERNAL API
///
/// Actor responsible for handing the inbound chunking of messages from the remote transport
/// </summary>
internal sealed class ChunkedInboundAssociation : ReceiveActor
{
    private readonly IAssociationEventListener _listener;
    private readonly AssociationHandle _originalHandle;

    public ChunkedInboundAssociation(IAssociationEventListener listener, AssociationHandle originalHandle)
    {
        _listener = listener;
        _originalHandle = originalHandle;
    }
}