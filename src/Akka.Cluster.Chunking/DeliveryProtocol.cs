﻿// -----------------------------------------------------------------------
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

/// <summary>
/// Input to chunked delivery system.
/// </summary>
/// <param name="Payload">The real, underlying message.</param>
/// <param name="Recipient">The recipient (must not be null)</param>
/// <param name="ReplyTo">The sender, as far as the remote actor knows (optional)</param>
public sealed record ChunkedDelivery(object Payload, IActorRef Recipient, IActorRef? ReplyTo = null) : IDeliveryProtocol;

/// <summary>
/// Registers a consumer node with a producer node.
/// </summary>
/// <param name="ConsumerController">A reference pointing to the ConsumerController.</param>
public sealed record RegisterConsumer(IActorRef ConsumerController) : IDeliveryProtocol;

/// <summary>
/// User to confirm that both nodes are now registered with one another.
/// </summary>
public sealed class RegisterAck : IDeliveryProtocol
{
    // make singleton
    public static readonly RegisterAck Instance = new();
    private RegisterAck(){}
}

/// <summary>
/// Local response message indicating that the message has been queued for delivery.
/// </summary>
public sealed class DeliveryQueuedAck : IDeliveryProtocol
{
    public static readonly DeliveryQueuedAck Instance = new();
    private DeliveryQueuedAck(){}
}

public enum DeliveryNackReason
{
    Timeout = 0,
    BufferFull = 1,
    SendingTerminated = 2,
}

/// <summary>
/// Unable to queue message for chunked delivery - usually due to buffer being full.
/// </summary>
/// <param name="Reason">Why the message was rejected.</param>
/// <param name="Message">Optional - human-readable description of the failure.</param>
public sealed record DeliveryQueuedNack(DeliveryNackReason Reason, string Message = "") : IDeliveryProtocol;