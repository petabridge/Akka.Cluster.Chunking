using Akka.Cluster;
using Akka.Cluster.Hosting;
using Akka.Remote.Hosting;

namespace Shared;

public class AkkaConfig
{
    public required string ActorSystemName { get; set; }
    
    public required RemoteOptions RemoteSettings { get; set; }
    
    public required ClusterOptions ClusterSettings { get; set; }
}