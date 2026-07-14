using RealtimePix.Web;

namespace IdentityPresence.Api;

public static class CorsOrigins
{
    public static bool IsAllowed(string? origin, IConfiguration configuration)
        => BrowserOriginPolicy.IsAllowed(
            origin,
            configuration,
            ["http://localhost:3000", "https://realtime-pix-web.vercel.app"]);
}
