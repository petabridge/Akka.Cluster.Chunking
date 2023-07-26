// -----------------------------------------------------------------------
//  <copyright file="ChunkedInboundAssociation.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Cluster.Chunking;

/// <summary>
/// INTERNAL API
/// </summary>
internal static class ChunkingUtilities
{
    private static readonly AtomicCounterLong ProducerIdCounter = new(0L);
    public const string ChunkerActorName = "chunker";

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

    /// <summary>
    /// Used to compute the remote path for the ProducerController / ConsumerController on the remote side.
    /// </summary>
    public static ActorPath ComputeRemoteChunkerPath(Address remoteAddress)
    {
        // have to swap local and remote for the remote path
        return new RootActorPath(remoteAddress) / "system" / ChunkerActorName;
    }
}