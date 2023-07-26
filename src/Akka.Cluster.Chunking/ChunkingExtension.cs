// -----------------------------------------------------------------------
//  <copyright file="ChunkingExtension.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.Chunking.Configuration;
using static Akka.Cluster.Chunking.ChunkingUtilities;

namespace Akka.Cluster.Chunking;

/// <summary>
/// <see cref="ActorSystem"/> extension designed to allow for chunking of large (5+mB) messages between
/// remote actor systems. Built on top of Akka.Delivery but hides all of the implementation details
/// and automatically connects ProducerControllers and ConsumerControllers on each nodes together.
/// </summary>
public sealed class ChunkingManager : IExtension
{
    private readonly IActorRef _deliveryManager;
    
    public ChunkingManagerSettings Settings { get; }

    public ChunkingManager(IActorRef deliveryManager, ChunkingManagerSettings settings)
    {
        _deliveryManager = deliveryManager;
        Settings = settings;
    }

    /// <summary>
    /// Delivers a message to a remote actor, chunking it into fixed size segments. Should only be used
    /// for large messages.
    /// </summary>
    /// <param name="message">The message to be delivered - must not be null.</param>
    /// <param name="recipient">The recipient who will receive the message.</param>
    /// <param name="sender">Optional - the actor who is the "sender" of this message.</param>
    /// <remarks>
    /// This operation will time out in accordance with the `akka.cluster.chunking.request-timeout` setting, which
    /// defaults to 5s. When this <see cref="Task"/> completes it means that the message has been accepted by the
    /// underlying Akka.Delivery system and is being transmitted - NOT THAT IT'S BEEN RECEIVED ON THE OTHER SIDE
    /// OF THE NETWORK.
    /// </remarks>
    public async Task DeliverChunked(object message, IActorRef recipient, IActorRef? sender = null)
    {
        var chunkedDelivery = new ChunkedDelivery(message, recipient, sender);
        var msg = await _deliveryManager.Ask(chunkedDelivery, Settings.RequestTimeout);
        if (msg is DeliveryQueuedNack nack)
        {
            throw new TimeoutException(nack.Message);
        }
    }
    
    /// <summary>
    /// Returns the <see cref="ChunkingManager"/> instance for the given <see cref="ActorSystem"/>.
    /// </summary>
    /// <param name="system">The <see cref="ActorSystem"/></param>
    /// <returns>The <see cref="ChunkingManager"/> instance that belongs to this <see cref="ActorSystem"/>.</returns>
    public static ChunkingManager For(ActorSystem system) => system.WithExtension<ChunkingManager, ChunkingManagerExtension>();
}

/// <summary>
/// INTERNAL API
///
/// Extension provider for <see cref="ChunkingManager"/>.
/// </summary>
public sealed class ChunkingManagerExtension : ExtensionIdProvider<ChunkingManager>
{
    public override ChunkingManager CreateExtension(ExtendedActorSystem system)
    {
        // inject default HOCON if none is provided
        if (!system.Settings.Config.HasPath("akka.cluster.chunking"))
        {
            system.Settings.InjectTopLevelFallback(ChunkingConfiguration.DefaultHocon);
        }
        
        if (system.Provider is not IClusterActorRefProvider)
        {
            throw new NotSupportedException("Akka.Cluster.Chunking can only be used with Akka.Cluster.");
        }
        
        var deliverManagerSettings = ChunkingManagerSettings.Create(system);
        var deliveryManager = system.SystemActorOf(Props.Create(() => new DeliveryManager(deliverManagerSettings)), ChunkerActorName);
        return new ChunkingManager(deliveryManager, deliverManagerSettings);
    }
}