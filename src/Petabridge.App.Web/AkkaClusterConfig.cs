namespace Petabridge.App.Web;

/// <summary>
/// To be parsed from appsettings.json or environment variables
/// </summary>
public class AkkaClusterConfig
{
    public string? ActorSystemName { get; set; }
    public string? Hostname { get; set; }
    public int? Port { get;set; }

    public List<string>? Roles { get; set; }

    public List<string>? SeedNodes { get; set; }
}