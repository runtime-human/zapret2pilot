using System.ComponentModel.DataAnnotations;

namespace Zapret2Pilot.App.Hosting;

public enum DeploymentFlavor
{
    Installed,
    Portable,
    Development
}

public sealed class Z2PApplicationOptions
{
    [Required]
    public string StorageDatabasePath { get; set; } = default!;

    public DeploymentFlavor DeploymentFlavor { get; set; } = DeploymentFlavor.Development;

    public bool DeveloperMode { get; set; }

    [Required]
    public string TrustedTufRoot { get; set; } = default!;
}
