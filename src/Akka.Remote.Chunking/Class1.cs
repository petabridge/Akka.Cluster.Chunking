using System;
using Akka.Actor;
using Akka.Remote.Transport;
using Akka.Util;
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
        public interface IChunkingManagerProtocol : INoSerializationVerificationNeeded
        {
        }

        public sealed class AssociateResult : IChunkingManagerProtocol
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="associationHandle">TBD</param>
            /// <param name="statusPromise">TBD</param>
            public AssociateResult(AssociationHandle associationHandle,
                TaskCompletionSource<AssociationHandle> statusPromise)
            {
                StatusPromise = statusPromise;
                AssociationHandle = associationHandle;
            }

            /// <summary>
            /// The outbound association handle
            /// </summary>
            public AssociationHandle AssociationHandle { get; }

            /// <summary>
            /// Used to complete the outbound association promise
            /// </summary>
            public TaskCompletionSource<AssociationHandle> StatusPromise { get; }
        }

        public sealed class Handle : IChunkingManagerProtocol
        {
            public Handle(ChunkingHandle chunkingHandle)
            {
                ChunkingHandle = chunkingHandle;
            }

            public ChunkingHandle ChunkingHandle { get; }
        }

        private readonly Transport.Transport _wrappedTransport;
        private readonly RemoteSettings _remoteSettings;
        
        private Dictionary<Address, ChunkingHandle> _handles = new();

        public ChunkingTransportManager(Transport.Transport wrappedTransport)
        {
            _wrappedTransport = wrappedTransport;
            _remoteSettings = wrappedTransport.System.AsInstanceOf<ExtendedActorSystem>().Provider
                .AsInstanceOf<IRemoteActorRefProvider>().RemoteSettings;
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
                    wrappedHandle.ChunkedAssociationOwner.Tell(new Handle(wrappedHandle));
                    return;
                }

                case AssociateUnderlying ua:
                {
                    // Slight modification of PipeTo, only success is sent, failure is propagated to a separate Task
                    async Task ProcessAssociate(AssociateUnderlying a)
                    {
                        var self = Self;
                        try
                        {
                            await _wrappedTransport.Associate(a.RemoteAddress);
                            self.Tell();
                        }
                        catch (Exception ex)
                        {
                            a.StatusPromise.SetException(ex);
                        }
                    }
                    var associateTask = _wrappedTransport.Associate(ua.RemoteAddress);
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

        private ChunkingHandle WrapHandle(AssociationHandle originalHandle, IAssociationEventListener listener,
            bool inbound)
        {
            var managerRef = Self;
            var atomicBool = new AtomicBoolean(true);
            var maxPayloadBytes = _wrappedTransport.MaximumPayloadBytes;
            var chunkSize = (int)maxPayloadBytes / 4; // do 25% of the max payload size

            return new ChunkingHandle(originalHandle, Context.ActorOf(
                _remoteSettings.ConfigureDispatcher(Props.Create(() =>
                        new ChunkedAssociationOwner(atomicBool, managerRef, listener, originalHandle, inbound, chunkSize))
                    .WithDeploy(Deploy.Local)),
                ChunkedAssociationOwner.ComputeChunkedAssociationName(originalHandle.LocalAddress,
                    originalHandle.RemoteAddress)), maxPayloadBytes, atomicBool);
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