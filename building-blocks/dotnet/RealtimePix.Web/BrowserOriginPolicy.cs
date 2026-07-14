using Microsoft.Extensions.Configuration;

namespace RealtimePix.Web;

public static class BrowserOriginPolicy
{
    public static bool IsAllowed(
        string? origin,
        IConfiguration configuration,
        IReadOnlyCollection<string> defaultOrigins)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        var configuredOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? [.. defaultOrigins];
        if (configuredOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return configuration.GetValue("Cors:AllowVercelPreviews", false)
            && IsVercelProjectPreview(
                origin,
                configuration["Cors:VercelProjectName"],
                configuration["Cors:VercelScopeSlug"]);
    }

    public static bool IsVercelProjectPreview(string origin, string? projectName, string? scopeSlug)
    {
        if (!IsDnsLabel(projectName) || !IsDnsLabel(scopeSlug))
        {
            return false;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/")
        {
            return false;
        }

        var host = uri.IdnHost;
        var prefix = $"{projectName}-";
        var suffix = $"-{scopeSlug}.vercel.app";
        if (!host.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var deploymentPartLength = host.Length - prefix.Length - suffix.Length;
        return deploymentPartLength > 0
            && IsDnsLabel(host.Substring(prefix.Length, deploymentPartLength));
    }

    private static bool IsDnsLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 63
            || !char.IsLetterOrDigit(value[0])
            || !char.IsLetterOrDigit(value[^1]))
        {
            return false;
        }

        return value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }
}
