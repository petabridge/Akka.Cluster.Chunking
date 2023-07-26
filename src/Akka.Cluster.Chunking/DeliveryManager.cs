// -----------------------------------------------------------------------
//  <copyright file="EndpointDeliveryManager.cs" company="Akka.NET Project">
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
/// Settings for <see cref="DeliveryManager"/>.
/// </summary>
public sealed record DeliveryManagerSettings()
{
    /// <summary>
    /// Chunk size to use over the wire.
    /// </summary>
    /// <remarks>
    ///  Defaults to 64kb.
    /// </remarks>
    public int ChunkSize { get; init; } = 1024 * 64;

    /// <summary>
    /// Request timeout in the queue.
    /// </summary>
    /// <remarks>
    /// Defaults to 5 seconds.
    /// </remarks>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Number of messages that can be buffered when there's no demand from ProducerController.
    /// </summary>
    /// <remarks>
    /// Defaults to 20.
    /// </remarks>
    public int OutboundQueueCapacity { get; init; } = 20;
}

public sealed class DeliveryManager : UntypedActor
{
    private readonly Dictionary<Address, IActorRef> _endpointManagers;

    protected override void OnReceive(object message)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// INTERNAL API
///
/// Actor responsible for managing ProducerController and ConsumerController instances
/// for a single remote endpoint.
/// </summary>
public sealed class EndpointDeliveryManager : UntypedActor, IWithTimers
{
    private readonly Address _localAddress;
    private readonly Address _remoteAddress;

    private IActorRef? _consumerController;
    private IActorRef? _inboundDeliveryHandler;
    private IActorRef? _outboundDeliveryHandler;

    private readonly DeliveryManagerSettings _settings;
    private readonly ILoggingAdapter _log = Context.GetLogger();
    
    private readonly Func<Address, ActorPath> _chunkerPathFunc;

    private class RegisterToRemote
    {
        // make singleton
        public static readonly RegisterToRemote Instance = new();
        private RegisterToRemote() { }
    }

    public EndpointDeliveryManager(Address localAddress, Address remoteAddress, DeliveryManagerSettings settings, Func<Address, ActorPath>? chunkerPath = null)
    {
        _localAddress = localAddress;
        _remoteAddress = remoteAddress;
        _settings = settings;
        _chunkerPathFunc = chunkerPath ?? ComputeRemoteChunkerPath;
    }

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case ChunkedDelivery chunkedDelivery: // forward regardless of registration status
                _outboundDeliveryHandler.Forward(chunkedDelivery);
                break;
            case RegisterToRemote: // need to register our consumer to the remote endpoint
                var remoteChunkerPath = _chunkerPathFunc(_remoteAddress);
                if (_log.IsDebugEnabled)
                {
                    _log.Debug("Sending RegisterToRemote to [{0}] for our local address [{1}]", remoteChunkerPath, _localAddress);
                }
                Context.ActorSelection(remoteChunkerPath).Tell(new RegisterConsumer(_consumerController!));
                
                // start the periodic timer to re-register us to the remote endpoint
                Timers.StartSingleTimer("remote-registered", RegisterToRemote.Instance, TimeSpan.FromSeconds(3));
                break;
            case RegisterAck: // remote endpoint has registered us
                Timers.Cancel("remote-registered");
                if(_log.IsDebugEnabled)
                    _log.Debug("Successfully registered our consumer with remote endpoint [{0}]", _remoteAddress);
                break;
            case RegisterConsumer r:
                _outboundDeliveryHandler.Forward(r);
                break;
        }
    }

    protected override void PreStart()
    {
        CreateHandlers();
        Self.Tell(RegisterToRemote.Instance); // begin registration process
    }

    // method that creates the InboundDeliveryHandler, OutboundDeliveryHandler, and ConsumerController
    // and wires them together
    private void CreateHandlers()
    {
        _inboundDeliveryHandler =
            Context.ActorOf(Props.Create(() => new InboundDeliveryHandler(_remoteAddress)), "inbound");
        _outboundDeliveryHandler = Context.ActorOf(Props.Create(() =>
                new OutboundDeliveryHandler(_remoteAddress, _localAddress, _settings.ChunkSize,
                    _settings.RequestTimeout, _settings.OutboundQueueCapacity)),
            "outbound");

        var consumerControllerSettings = ConsumerController.Settings.Create(Context.System);
        var consumerControllerProps =
            ConsumerController.Create<IDeliveryProtocol>(Context, Option<IActorRef>.None,
                consumerControllerSettings);
        
        _consumerController = Context.ActorOf(consumerControllerProps, "consumer-controller");
        _consumerController.Tell(new ConsumerController.Start<IDeliveryProtocol>(_inboundDeliveryHandler));
    }

    public ITimerScheduler Timers { get; set; } = null!;
}