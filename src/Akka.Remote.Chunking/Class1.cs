using System;
using Akka.Actor;
using Akka.Remote.Transport;
using Akka.Util.Internal;

namespace Akka.Remote.Chunking
{
    public sealed class ChunkingTransportAdapterProvider : ITransportAdapterProvider
    {
        public Transport.Transport Create(Transport.Transport wrappedTransport, ExtendedActorSystem system)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// Responsible for managing chunking transport adapters for each connection.
    ///
    /// One of these exists per <see cref="ActorSystem"/>.
    /// </summary>
    internal sealed class ChunkingTransportManager : ActorTransportAdapterManager
    {
        private readonly Transport.Transport _wrappedTransport;

        public ChunkingTransportManager(Transport.Transport wrappedTransport)
        {
            _wrappedTransport = wrappedTransport;
        }
        
        internal static Address NakedAddress(Address address)
        {
            return address.WithProtocol(string.Empty)
                .WithSystem(string.Empty);
        }

        protected override void Ready(object message)
        {
            switch (message)
            {
                case InboundAssociation ia:
                {
                    var wrappedHandle = WrapHandle(ia.Association, AssociationListener, true);
                    wrappedHandle.ThrottlerActor.Tell(new Handle(wrappedHandle));
                    return;
                }
                
                case AssociateUnderlying ua:
                {
                    // Slight modification of PipeTo, only success is sent, failure is propagated to a separate Task
                    var associateTask = WrappedTransport.Associate(ua.RemoteAddress);
                    var self = Self;
                    associateTask.ContinueWith(tr =>
                    {
                        if (tr.IsFaulted)
                        {
                            ua.StatusPromise.SetException(tr.Exception ?? new Exception("association failed"));
                        }
                        else
                        {
                            self.Tell(new AssociateResult(tr.Result, ua.StatusPromise));
                        }

                    }, TaskContinuationOptions.ExecuteSynchronously);
                    return;
                }
                
                // Finished outbound association and got back the handle
                case AssociateResult ar:
                {
                    var wrappedHandle = WrapHandle(ar.AssociationHandle, AssociationListener, false);
                    var naked = NakedAddress(ar.AssociationHandle.RemoteAddress);
                    var inMode = GetInboundMode(naked);
                    wrappedHandle.OutboundThrottleMode.Value = GetOutboundMode(naked);
                    wrappedHandle.ReadHandlerSource.Task.ContinueWith(
                            tr => new ListenerAndMode(tr.Result, inMode), 
                            TaskContinuationOptions.ExecuteSynchronously)
                        .PipeTo(wrappedHandle.ThrottlerActor);
                    _handleTable.Add((naked, wrappedHandle));
                    ar.StatusPromise.SetResult(wrappedHandle);
                    return;
                }
                
                case SetThrottle st:
                {
                    var naked = NakedAddress(st.Address);
                    _throttlingModes[naked] = (st.Mode, st.Direction);
                    var modes = new List<Task<SetThrottleAck>>
                    {
                        Task.FromResult(SetThrottleAck.Instance)
                    };
                    
                    foreach (var (address, throttlerHandle) in _handleTable)
                    {
                        if (address == naked)
                            modes.Add(SetMode(throttlerHandle, st.Mode, st.Direction));
                    }

                    var sender = Sender;
                    Task.WhenAll(modes).ContinueWith(
                            _ => SetThrottleAck.Instance, 
                            TaskContinuationOptions.ExecuteSynchronously)
                        .PipeTo(sender);
                    return;
                }
                
                case ForceDisassociate fd:
                {
                    var naked = NakedAddress(fd.Address);
                    foreach (var handle in _handleTable)
                    {
                        if (handle.Item1 == naked)
#pragma warning disable CS0618
                                handle.Item2.Disassociate();
#pragma warning restore CS0618
                        }

                        /*
                         * NOTE: Important difference between Akka.NET and Akka here.
                         * In canonical Akka, ThrottleHandlers are never removed from
                         * the _handleTable. The reason is because Ask-ing a terminated ActorRef
                         * doesn't cause any exceptions to be thrown upstream - it just times out
                         * and propagates a failed Future.
                         * 
                         * In the CLR, a CancellationException gets thrown and causes all
                         * parent tasks chaining back to the EndPointManager to fail due
                         * to an Ask timeout.
                         * 
                         * So in order to avoid this problem, we remove any disassociated handles
                         * from the _handleTable.
                         * 
                         * Questions? Ask @Aaronontheweb
                         */
                        _handleTable.RemoveAll(tuple => tuple.Item1 == naked);
                    Sender.Tell(ForceDisassociateAck.Instance);
                    return;
                }
                
                case ForceDisassociateExplicitly fde:
                {
                    var naked = NakedAddress(fde.Address);
                    foreach (var (address, throttlerHandle) in _handleTable)
                    {
                        if (address == naked)
                            throttlerHandle.DisassociateWithFailure(fde.Reason);
                    }

                    /*
                     * NOTE: Important difference between Akka.NET and Akka here.
                     * In canonical Akka, ThrottleHandlers are never removed from
                     * the _handleTable. The reason is because Ask-ing a terminated ActorRef
                     * doesn't cause any exceptions to be thrown upstream - it just times out
                     * and propagates a failed Future.
                     * 
                     * In the CLR, a CancellationException gets thrown and causes all
                     * parent tasks chaining back to the EndPointManager to fail due
                     * to an Ask timeout.
                     * 
                     * So in order to avoid this problem, we remove any disassociated handles
                     * from the _handleTable.
                     * 
                     * Questions? Ask @Aaronontheweb
                     */
                    _handleTable.RemoveAll(tuple => tuple.Item1 == naked);
                    Sender.Tell(ForceDisassociateAck.Instance);
                    return;
                }
                
                case Checkin chkin:
                {
                    var naked = NakedAddress(chkin.Origin);
                    _handleTable.Add((naked, chkin.ThrottlerHandle));
                    SetMode(naked, chkin.ThrottlerHandle);
                    return;
                }
            }
        }
    }

    public sealed class ChunkingTransportAdapter : ActorTransportAdapter
    {
        public ChunkingTransportAdapter(Transport.Transport wrappedTransport, ActorSystem system) : base(
            wrappedTransport, system)
        {
        }
        
        internal const string SchemeIdentifier = "chunk";
        internal static readonly SchemeAugmenter SCHEME = new SchemeAugmenter(SchemeIdentifier);
        private static readonly AtomicCounter UniqueId = new(0);

        protected override SchemeAugmenter SchemeAugmenter => SCHEME;

        /// <summary>
        /// The name of the actor managing the throttler
        /// </summary>
        protected override string ManagerName =>
            "chunkingmanager";

        protected override Props ManagerProps { get; }
    }
}