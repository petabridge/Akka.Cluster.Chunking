using System;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Cluster.Chunking;
using Akka.Delivery;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Chunking.Tests
{
    public class EndpointDeliveryManagerSpecs : TestKit.Xunit2.TestKit
    {
        public EndpointDeliveryManagerSpecs(ITestOutputHelper output) : base("akka.loglevel=DEBUG", output:output)
        {
        }
        
        private IActorRef CreateEndpointDeliveryManager(IActorRef otherDeliveryManager)
        {
            Func<Address, ActorPath> getPath = address => otherDeliveryManager.Path;;

            return Sys.ActorOf(Props.Create(() => new EndpointDeliveryManager(Address.AllSystems, Address.AllSystems, new DeliveryManagerSettings(), getPath)));
        }

        private IActorRef CreateNonChunkingProducerController(string? name = null)
        {
            var settings = ProducerController.Create<IDeliveryProtocol>(Sys, "test", Option<Props>.None);
            return Sys.ActorOf(settings, name);
        }

        [Fact]
        public async Task ShouldRegisterToRemoteAndRunAutomatically()
        {
            // Arrange
            var remoteDeliveryManagerProbe = CreateTestProbe();
            var senderProbe = CreateTestProbe();
            var receiverProbe = CreateTestProbe();
            var remoteDeliveryManager = CreateEndpointDeliveryManager(remoteDeliveryManagerProbe);
            
            // Act
            var registerConsumer = await remoteDeliveryManagerProbe.ExpectMsgAsync<RegisterConsumer>();
            remoteDeliveryManagerProbe.Reply(RegisterAck.Instance); // close the loop
            
            // create a ProducerController (no chunking yet)
            var producerController = CreateNonChunkingProducerController();
            producerController.Tell(new ProducerController.RegisterConsumer<IDeliveryProtocol>(registerConsumer.ConsumerController));
            producerController.Tell(new ProducerController.Start<IDeliveryProtocol>(senderProbe.Ref));

            var requestNext = await senderProbe.ExpectMsgAsync<ProducerController.RequestNext<IDeliveryProtocol>>();
            requestNext.SendNextTo.Tell(new ChunkedDelivery("foo", receiverProbe.Ref, senderProbe.Ref));
            
            // Assert
            await receiverProbe.ExpectMsgAsync("foo");
            receiverProbe.Sender.Should().Be(senderProbe.Ref);
        }
    }
}
