// -----------------------------------------------------------------------
//  <copyright file="ChunkingConfiguration.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Configuration;

namespace Akka.Cluster.Chunking.Configuration;

public static class ChunkingConfiguration
{
    public static readonly Config DefaultHocon = ConfigurationFactory.FromResource<ChunkingManagerSettings>("Akka.Cluster.Chunking.Configuration.chunking.conf");
}