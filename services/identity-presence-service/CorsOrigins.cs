public static class CorsOrigins
{
    public static bool IsAllowed(string? origin, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        var configured = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:3000", "https://realtime-pix-web.vercel.app"];

        if (configured.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase)
            && uri.Host.StartsWith("realtime-pix-web", StringComparison.OrdinalIgnoreCase);
    }
}
