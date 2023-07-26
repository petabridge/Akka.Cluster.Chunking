// -----------------------------------------------------------------------
//  <copyright file="ChunkingConfiguration.cs" company="Akka.NET Project">
//      Copyright (C) 2015-2023 .NET Petabridge, LLC
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Event;
using Akka.Hosting;

namespace Akka.Cluster.Chunking.Configuration;

public static class ChunkingConfiguration
{
    public static readonly Config DefaultHocon = ConfigurationFactory.FromResource<ChunkingManagerSettings>("Akka.Cluster.Chunking.Configuration.chunking.conf");
    
    /// <summary>
    /// Add ChunkingManager to the ActorSystem and start it automatically.
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static AkkaConfigurationBuilder AddChunkingManager(this AkkaConfigurationBuilder builder)
    {
        builder
            .AddHocon(DefaultHocon, HoconAddMode.Append)
            .AddStartup(((system, registry) =>
            {
                system.Log.Info("Starting Akka.Cluster.Chunking...");
                ChunkingManager.For(system);
                system.Log.Info("Akka.Cluster.Chunking Started.");
            }));
        return builder;
    }
}