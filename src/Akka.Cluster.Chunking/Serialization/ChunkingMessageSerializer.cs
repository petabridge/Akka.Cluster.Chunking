// -----------------------------------------------------------------------
//  <copyright file="ChunkingMessageSerializer.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.Chunking.Proto;
using Akka.Serialization;
using Akka.Util;
using Google.Protobuf;

namespace Akka.Cluster.Chunking.Serialization;

/// <summary>
/// INTERNAL API
/// </summary>
internal sealed class WrappedPayloadSupport
{
    private readonly ExtendedActorSystem _system;

    public WrappedPayloadSupport(ExtendedActorSystem system)
    {
        _system = system;
    }

    public Payload PayloadToProto(object? payload)
    {
        if (payload == null) // TODO: handle null messages
            return new Payload();

        var payloadProto = new Payload();
        var serializer = _system.Serialization.FindSerializerFor(payload);

        payloadProto.Message = ByteString.CopyFrom(serializer.ToBinary(payload));
        payloadProto.SerializerId = serializer.Identifier;

        // get manifest

        if (serializer is SerializerWithStringManifest manifestSerializer)
        {
            var manifest = manifestSerializer.Manifest(payload);
            if (!string.IsNullOrEmpty(manifest))
                payloadProto.MessageManifest = ByteString.CopyFromUtf8(manifest);
        }
        else
        {
            if (serializer.IncludeManifest)
                payloadProto.MessageManifest = ByteString.CopyFromUtf8(payload.GetType().TypeQualifiedName());
        }

        return payloadProto;
    }

    public object PayloadFrom(Payload payload)
    {
        var manifest = !payload.MessageManifest.IsEmpty
            ? payload.MessageManifest.ToStringUtf8()
            : string.Empty;

        return _system.Serialization.Deserialize(
            payload.Message.ToByteArray(),
            payload.SerializerId,
            manifest);
    }
}

public sealed class ChunkingMessageSerializer : SerializerWithStringManifest
{
    public ChunkingMessageSerializer(ExtendedActorSystem system) : base(system)
    {
        _wrappedPayloadSupport = new WrappedPayloadSupport(system);
    }

    private readonly WrappedPayloadSupport _wrappedPayloadSupport;

    public override int Identifier { get; } = 771;
    
    // create constant string manifests for all INetworkedChunkedMessage types
    private const string ChunkedDeliveryManifest = "cd";
    private const string RegisterConsumerManifest = "rc";
    private const string RegisterAckManifest = "ra";
    private const string RegisterNackManifest = "rn";

    public override byte[] ToBinary(object obj)
    {
        // convert all INetworkedChunkedMessage types to binary using their Protobuf representations
        return obj switch
        {
            ChunkedDelivery cd => ToProto(cd).ToByteArray(),
            RegisterConsumer rc => ToProto(rc).ToByteArray(),
            RegisterAck ra => Array.Empty<byte>(),
            RegisterNack rn => Array.Empty<byte>(),
            _ => throw new ArgumentException($"Cannot serialize object of type [{obj.GetType()}] in [{GetType()}]")
        };
    }

    private static Proto.RegisterConsumer ToProto(RegisterConsumer rc)
    {
        var recipient = Akka.Serialization.Serialization.SerializedActorPath(rc.ConsumerController);

        return new Proto.RegisterConsumer()
        {
            Consumer = recipient
        };
    }

    private Proto.ChunkedDelivery ToProto(ChunkedDelivery cd)
    {
        var recipient = Akka.Serialization.Serialization.SerializedActorPath(cd.Recipient);
        var sender = cd.ReplyTo is null ? string.Empty : Akka.Serialization.Serialization.SerializedActorPath(cd.ReplyTo);
        
        return new Proto.ChunkedDelivery()
        {
            Payload = _wrappedPayloadSupport.PayloadToProto(cd.Payload),
            Recipient = recipient,
            Sender = sender
        };
    }

    public override object FromBinary(byte[] bytes, string manifest)
    {
        switch (manifest)
        {
            // generate cases for all manifests
            case ChunkedDeliveryManifest:
                return FromProto(Proto.ChunkedDelivery.Parser.ParseFrom(bytes));
            case RegisterConsumerManifest:
                return FromProto(Proto.RegisterConsumer.Parser.ParseFrom(bytes));
            case RegisterAckManifest:
                return RegisterAck.Instance;
            case RegisterNackManifest:
                return RegisterNack.Instance;
            default:
                throw new ArgumentException(
                    $"No deserialization support for message manifest [{manifest}] in [{GetType()}]");
        }
    }

    private RegisterConsumer FromProto(Proto.RegisterConsumer parseFrom)
    {
        var recipient = system.Provider.ResolveActorRef(parseFrom.Consumer);

        return new RegisterConsumer(recipient);
    }

    private ChunkedDelivery FromProto(Proto.ChunkedDelivery parseFrom)
    {
        var recipient = system.Provider.ResolveActorRef(parseFrom.Recipient);
        var sender = string.IsNullOrEmpty(parseFrom.Sender) ? ActorRefs.NoSender : system.Provider.ResolveActorRef(parseFrom.Sender);

        return new ChunkedDelivery(
            _wrappedPayloadSupport.PayloadFrom(parseFrom.Payload),
            recipient,
            sender);
    }

    public override string Manifest(object o)
    {
        return o switch
        {
            ChunkedDelivery cd => ChunkedDeliveryManifest,
            RegisterConsumer rc => RegisterConsumerManifest,
            RegisterAck ra => RegisterAckManifest,
            RegisterNack rn => RegisterNackManifest,
            _ => throw new ArgumentException($"Cannot serialize object of type [{o.GetType()}] in [{GetType()}]")
        };
    }
}