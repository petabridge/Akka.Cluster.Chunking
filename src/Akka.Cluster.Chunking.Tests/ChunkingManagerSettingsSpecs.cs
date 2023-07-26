// -----------------------------------------------------------------------
//  <copyright file="ChunkingManagerSettingsSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Cluster.Chunking;
using Akka.Cluster.Chunking.Configuration;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Chunking.Tests;

public class ChunkingManagerSettingsSpecs
{
    [Fact]
    public void ShouldUseCorrectDefaults()
    {
        // arrange
        var chunkingSettings = ChunkingManagerSettings.Create(ChunkingConfiguration.DefaultHocon.GetConfig("akka.cluster.chunking"));
        
        // assert
        chunkingSettings.ChunkSize.Should().Be(64000);
        chunkingSettings.RequestTimeout.Should().Be(5.Seconds());
        chunkingSettings.OutboundQueueCapacity.Should().Be(20);
    }
}