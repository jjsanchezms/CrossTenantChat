namespace CrossTenantChat.Configuration
{
    public class AzureConfiguration
    {
        public AzureAdConfiguration AzureAd { get; set; } = new();
        public AzureCommunicationServicesConfiguration AzureCommunicationServices { get; set; } = new();
    }

    public class AzureAdConfiguration
    {
        public string Instance { get; set; } = "https://login.microsoftonline.com/";
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        
        // Cross-tenant configuration
        public string FabrikamTenantId { get; set; } = string.Empty;
        public string ContosoTenantId { get; set; } = string.Empty;
        
        // Scopes for ACS access
        public string[] Scopes { get; set; } = Array.Empty<string>();
        
        public string Authority => $"{Instance}{TenantId}";
        public string FabrikamAuthority => $"{Instance}{FabrikamTenantId}";
        public string ContosoAuthority => $"{Instance}{ContosoTenantId}";
    }

    public class AzureCommunicationServicesConfiguration
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string ResourceId { get; set; } = string.Empty;
        public string EndpointUrl { get; set; } = string.Empty;
    }
}
