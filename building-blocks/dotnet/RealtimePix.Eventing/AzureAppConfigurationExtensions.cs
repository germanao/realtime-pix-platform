using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace RealtimePix.Eventing;

public static class AzureAppConfigurationExtensions
{
    public static ConfigurationManager AddRealtimePixAzureAppConfiguration(this ConfigurationManager configuration)
    {
        var endpoint = configuration["AppConfiguration:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return configuration;
        }

        var label = configuration["AppConfiguration:Label"];
        configuration.AddAzureAppConfiguration(options =>
        {
            options.Connect(new Uri(endpoint), new DefaultAzureCredential())
                .Select("RealtimePix:*", label)
                .Select("RealtimePix:*")
                .TrimKeyPrefix("RealtimePix:");
        });

        return configuration;
    }
}
