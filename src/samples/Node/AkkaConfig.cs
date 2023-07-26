using Akka.Cluster.Hosting;
using Akka.Remote.Hosting;

namespace SeedNode;

public class AkkaConfig
{
    public string ActorSystemName { get; set; }
    
    public RemoteOptions RemoteSettings { get; set; }
    
    public ClusterOptions ClusterSettings { get; set; }
}