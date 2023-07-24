// -----------------------------------------------------------------------
//  <copyright file="ChunkedInboundAssociation.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Remote.Transport;
using Akka.Util;

namespace Akka.Remote.Chunking;

/// <summary>
/// INTERNAL API
///
/// Actor responsible for handling the outbound chunking of messages to the remote transport
/// </summary>
internal sealed class ChunkedOutboundAssociation : ReceiveActor
{
    /// <summary>
    /// passed in externally in order to ensure that the TransportHandle can signal 
    /// </summary>
    private readonly AtomicBoolean _writeAvailable;

    private readonly IActorRef _manager;
    private readonly IAssociationEventListener _listener;
    private readonly AssociationHandle _originalHandle;
    private bool _isInboundParty;

    private IActorRef _producerController;

    public ChunkedOutboundAssociation(AtomicBoolean writeAvailable, IActorRef manager, IAssociationEventListener listener, AssociationHandle originalHandle, bool isInboundParty)
    {
        _writeAvailable = writeAvailable;
        _manager = manager;
        _listener = listener;
        _originalHandle = originalHandle;
        _isInboundParty = isInboundParty;
    }
}

/// <summary>
/// INTERNAL API
///
/// Actor responsible for handing the inbound chunking of messages from the remote transport
/// </summary>
internal sealed class ChunkedInboundAssociation : ReceiveActor
{
    
}