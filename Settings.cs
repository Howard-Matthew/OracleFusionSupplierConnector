using Microsoft.Extensions.Configuration;

namespace OracleFusionSupplierConnector;

public class Settings
{
    // Graph settings
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? TenantId { get; set; }
    // Oracle Fusion settings
    // OAuth 2.0 settings
    public string? OracleClientId { get; set; }
    public string? OracleClientSecret { get; set; }
    public string? OracleScope { get; set; }
    public string? OracleAccessTokenUrl { get; set; }

    // Oracle Fusion Supplier Url
    public string? OracleSupplierUrl { get; set; }
    
    public static Settings LoadSettings()
    {
        // Load settings
        IConfiguration config = new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .Build();

        return config.GetRequiredSection("Settings").Get<Settings>() ??
            throw new Exception("Could not load app settings. See README for configuration instructions.");
    }
}