// -----------------------------------------------------------------------
//  <copyright file="ChunkedInboundAssociation.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Cluster.Chunking;

public static class ChunkingUtilities
{
    private static readonly AtomicCounterLong ProducerIdCounter = new AtomicCounterLong(0L);
    public const string ProducerControllerName = "chunkProducer";
    public const string ConsumerControllerName = "chunkConsumer";
    
    /// <summary>
    /// Generates a producerId based on address pairs + a counter, so a unique Id will be produced every time.
    /// </summary>
    /// <param name="localAddress"></param>
    /// <param name="remoteAddress"></param>
    /// <remarks>
    /// Designed to generate new, unique producerIds each time there's a restart - so consumers can detect.
    /// </remarks>
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
        return Uri.EscapeDataString($"{(localAddress)}-{(remoteAddress)}");
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
}