using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RealtimePix.ApiGateway.Application;

namespace RealtimePix.ApiGateway.Infrastructure;

public sealed class ConfiguredBackendClient(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<ConfiguredBackendClient> logger) : IBackendClient
{
    public async Task<BackendResponse> SendAsync(
        BackendRequest request,
        CancellationToken cancellationToken)
    {
        var baseUrl = configuration[$"Services:{request.ServiceName}"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return new BackendResponse(
                503,
                JsonSerializer.Serialize(new
                {
                    title = "Backend service is not configured",
                    service = request.ServiceName
                }),
                "application/problem+json");
        }

        var target = $"{baseUrl.TrimEnd('/')}{request.Path}{request.QueryString}";
        using var message = new HttpRequestMessage(request.Method, target);
        if (request.Body is not null)
        {
            message.Content = new StringContent(
                request.Body,
                Encoding.UTF8,
                request.ContentType ?? "application/json");
        }

        try
        {
            using var response = await httpClient.SendAsync(message, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            return new BackendResponse((int)response.StatusCode, content, contentType);
        }
        catch (Exception exception) when (
            exception is HttpRequestException ||
            exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Backend service {ServiceName} is unavailable.", request.ServiceName);
            return new BackendResponse(
                503,
                JsonSerializer.Serialize(new
                {
                    title = "Backend service is unavailable",
                    service = request.ServiceName
                }),
                "application/problem+json");
        }
    }
}
