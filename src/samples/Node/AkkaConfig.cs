using Akka.Cluster.Hosting;
using Akka.Remote.Hosting;

namespace SeedNode;

public class AkkaConfig
{
    public string ActorSystemName { get; set; } = "ClusterSystem";
    
    public RemoteOptions RemoteSettings { get; set; } = new RemoteOptions();
    
    public ClusterOptions ClusterSettings { get; set; } = new ClusterOptions();
}