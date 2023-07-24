// -----------------------------------------------------------------------
//  <copyright file="ChunkingHandle.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Remote.Transport;
using Akka.Util;
using Google.Protobuf;

namespace Akka.Remote.Chunking;

/// <summary>
/// INTERNAL API
/// </summary>
internal sealed class ChunkingHandle : AbstractTransportAdapterHandle
{
    public AtomicBoolean WriteAvailable { get; }
        
    public IActorRef OutboundChunkingActor { get; }
        
    public long MaxPayloadBytes { get; }
        
    public ChunkingHandle(AssociationHandle wrappedHandle, IActorRef outboundChunkingActor, long maxPayloadBytes, AtomicBoolean writeAvailable) : base(wrappedHandle, ChunkingTransportAdapter.SCHEME.AddedSchemeIdentifier)
    {
        OutboundChunkingActor = outboundChunkingActor;
        MaxPayloadBytes = maxPayloadBytes;
        WriteAvailable = writeAvailable;
    }

    public override bool Write(ByteString payload)
    {
        // if we're beneath the maximum payload size, just write it directly
        if (payload.Length <= MaxPayloadBytes)
            return WrappedHandle.Write(payload);

        // over - need to see if we have bandwidth to begin chunking
        /*
         * This value gets toggled according to the Akka.Delivery ProducerController
         * rules: https://getakka.net/articles/actors/reliable-delivery.html
         */
        if (!WriteAvailable.Value)
            return false;
            
        // we have bandwidth, so we're going to start chunking
        OutboundChunkingActor.Tell(payload);
        return true;
    }

    [Obsolete("Use the method that states reasons to make sure disassociation reasons are logged.")]
    public override void Disassociate()
    {
        OutboundChunkingActor.Tell(PoisonPill.Instance);
    }
}