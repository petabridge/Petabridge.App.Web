namespace Petabridge.App.Web;

/// <summary>
/// To be parsed from appsettings.json or environment variables
/// </summary>
public class AkkaClusterConfig
{
    public string ActorSystemName { get; set; } = "ActorSystem";
    public string? Hostname { get; set; }
    public int? Port { get;set; }

    public string[]? Roles { get; set; } = Array.Empty<string>();

    public string[]? SeedNodes { get; set; } = Array.Empty<string>();
}
