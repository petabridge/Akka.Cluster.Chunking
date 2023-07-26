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

        [Fact]
        public async Task TwoManagersShouldRegisterToEachOther()
        {
            // Arrange
            var receiver1 = CreateTestProbe();
            var receiver2 = CreateTestProbe();
            var remoteManager1 = CreateTestProbe();
            var remoteDeliveryManager1 = CreateEndpointDeliveryManager(remoteManager1);
            
            // Act
            var registerConsumer1 = await remoteManager1.ExpectMsgAsync<RegisterConsumer>();
            var remoteDeliveryManager2 = CreateEndpointDeliveryManager(remoteDeliveryManager1);
            remoteManager1.Forward(remoteDeliveryManager2);
            
            remoteDeliveryManager1.Tell(new ChunkedDelivery("del1", receiver1.Ref));
            remoteDeliveryManager2.Tell(new ChunkedDelivery("del2", receiver2.Ref));
            
            // Assert
            await receiver1.ExpectMsgAsync("del1");
            await receiver2.ExpectMsgAsync("del2");
        }
    }
}
