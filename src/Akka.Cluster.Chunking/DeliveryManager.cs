// -----------------------------------------------------------------------
//  <copyright file="EndpointDeliveryManager.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Cluster.Chunking;

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
public sealed class EndpointDeliveryManager : UntypedActor
{
    private readonly Address _localAddress;
    private readonly Address _remoteAddress;

    private IActorRef? _consumerController;
    private IActorRef? _inboundDeliveryHandler;
    private IActorRef? _outboundDeliveryHandler;

    protected override void OnReceive(object message)
    {
        throw new NotImplementedException();
    }
}