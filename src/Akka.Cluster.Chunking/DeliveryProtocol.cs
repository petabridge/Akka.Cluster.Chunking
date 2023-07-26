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

/// <summary>
/// Marker interface for messages going over the wire in the chunked delivery system.
/// </summary>
public interface INetworkedDeliveryProtocol : IDeliveryProtocol{ }

/// <summary>
/// Input to chunked delivery system.
/// </summary>
public sealed record ChunkedDelivery : INetworkedDeliveryProtocol
{
    public ChunkedDelivery(object payload, IActorRef recipient, IActorRef? replyTo = null)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Recipient = recipient ?? throw new ArgumentNullException(nameof(recipient));
        ReplyTo = replyTo;
    }

    /// <summary>
    /// The sender, as far as the remote actor knows (optional)
    /// </summary>
    public IActorRef? ReplyTo { get; init; }

    /// <summary>
    /// The recipient of the message.
    /// </summary>
    /// <remarks>
    /// Must not be null.
    /// </remarks>
    public IActorRef Recipient { get; init; }

    /// <summary>
    /// The real, underlying message.
    /// </summary>
    /// <remarks>
    /// Must not be null.
    /// </remarks>
    public object Payload { get; init; }
}

/// <summary>
/// Registers a consumer node with a producer node.
/// </summary>
/// <param name="ConsumerController">A reference pointing to the ConsumerController.</param>
public sealed record RegisterConsumer(IActorRef ConsumerController) :INetworkedDeliveryProtocol;

/// <summary>
/// User to confirm that both nodes are now registered with one another.
/// </summary>
public sealed class RegisterAck : INetworkedDeliveryProtocol
{
    // make singleton
    public static readonly RegisterAck Instance = new();
    private RegisterAck(){}
}

public sealed class RegisterNack : INetworkedDeliveryProtocol
{
    // make singleton
    public static readonly RegisterNack Instance = new();
    private RegisterNack(){}
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
    
    /// <summary>
    /// <see cref="Address"/> isn't part of the cluster.
    /// </summary>
    IllegalAddress = 3,
}

/// <summary>
/// Unable to queue message for chunked delivery - usually due to buffer being full.
/// </summary>
/// <param name="Reason">Why the message was rejected.</param>
/// <param name="Message">Optional - human-readable description of the failure.</param>
public sealed record DeliveryQueuedNack(DeliveryNackReason Reason, string Message = "") : IDeliveryProtocol;