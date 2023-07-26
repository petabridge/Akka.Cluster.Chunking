// -----------------------------------------------------------------------
//  <copyright file="ChunkingSerializationSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Cluster.Chunking;
using Akka.Cluster.Chunking.Configuration;
using Akka.Cluster.Chunking.Serialization;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Chunking.Tests;

public class ChunkingSerializationSpecs : TestKit.Xunit2.TestKit
{
    public ChunkingSerializationSpecs(ITestOutputHelper output) : base(ChunkingConfiguration.DefaultHocon, output: output)
    {
    }
    
    [Fact]
    public void ShouldSerializeChunkedDelivery()
    {
        var msg = new ChunkedDelivery("hello", TestActor);
        VerifySerialization(msg);
    }
    
    [Fact]
    public void ShouldSerializeRegisterConsumer()
    {
        var msg = new RegisterConsumer(TestActor);
        VerifySerialization(msg);
    }
    
    [Fact]
    public void ShouldSerializeRegisterAck()
    {
        var msg = RegisterAck.Instance;
        VerifySerialization(msg);
    }
    
    [Fact]
    public void ShouldSerializeRegisterNack()
    {
        var msg = RegisterNack.Instance;
        VerifySerialization(msg);
    }

    private void VerifySerialization(object msg)
    {
        var serializer = Sys.Serialization.FindSerializerFor(msg);
        serializer.Should().BeOfType<ChunkingMessageSerializer>();
        var chunker = (ChunkingMessageSerializer) serializer;
        var m1 = chunker.FromBinary(chunker.ToBinary(msg), chunker.Manifest(msg));
        m1.Should().BeEquivalentTo(msg);
    }
}