// -----------------------------------------------------------------------
//  <copyright file="DeliveryProtocol.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Cluster.Chunking;

/// <summary>
/// Messages for doing point-to-point chunked message delivery for large messages.
/// </summary>
public interface IDeliveryProtocol
{
    
}

public record ChunkedDelivery(object Payload, IActorRef Recipient, IActorRef? Sender = null) : IDeliveryProtocol;