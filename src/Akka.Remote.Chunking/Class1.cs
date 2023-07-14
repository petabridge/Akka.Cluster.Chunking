using System;
using Akka.Actor;
using Akka.Remote.Transport;

namespace Akka.Remote.Chunking
{
    public sealed class ChunkingTransportAdapterProvider : ITransportAdapterProvider
    {
        public Transport.Transport Create(Transport.Transport wrappedTransport, ExtendedActorSystem system)
        {
            throw new NotImplementedException();
        }
    }
}
