using Microsoft.Extensions.Configuration;
using RealtimePix.Web;
using Xunit;

namespace WebHosting.Tests;

public sealed class BrowserOriginPolicyTests
{
    private const string PreviewOrigin = "https://realtime-pix-mbq993a9o-germanaos-projects.vercel.app";

    [Fact]
    public void Exact_configured_origin_is_allowed()
    {
        var configuration = Configuration(("Cors:AllowedOrigins:0", "https://realtime-pix-web.vercel.app"));

        Assert.True(BrowserOriginPolicy.IsAllowed(
            "https://realtime-pix-web.vercel.app",
            configuration,
            []));
    }

    [Fact]
    public void Preview_for_configured_project_and_scope_is_allowed()
    {
        var configuration = PreviewConfiguration();

        Assert.True(BrowserOriginPolicy.IsAllowed(PreviewOrigin, configuration, []));
    }

    [Theory]
    [InlineData("https://realtime-pix-mbq993a9o-another-scope.vercel.app")]
    [InlineData("https://another-project-mbq993a9o-germanaos-projects.vercel.app")]
    [InlineData("http://realtime-pix-mbq993a9o-germanaos-projects.vercel.app")]
    [InlineData("https://realtime-pix-mbq993a9o-germanaos-projects.vercel.app:444")]
    [InlineData("https://realtime-pix-mbq993a9o-germanaos-projects.vercel.app/path")]
    public void Origin_outside_exact_project_scope_is_denied(string origin)
    {
        Assert.False(BrowserOriginPolicy.IsAllowed(origin, PreviewConfiguration(), []));
    }

    [Fact]
    public void Preview_is_denied_when_feature_is_disabled()
    {
        var configuration = Configuration(
            ("Cors:AllowVercelPreviews", "false"),
            ("Cors:VercelProjectName", "realtime-pix"),
            ("Cors:VercelScopeSlug", "germanaos-projects"));

        Assert.False(BrowserOriginPolicy.IsAllowed(PreviewOrigin, configuration, []));
    }

    private static IConfiguration PreviewConfiguration() => Configuration(
        ("Cors:AllowVercelPreviews", "true"),
        ("Cors:VercelProjectName", "realtime-pix"),
        ("Cors:VercelScopeSlug", "germanaos-projects"));

    private static IConfiguration Configuration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value =>
                new KeyValuePair<string, string?>(value.Key, value.Value)))
            .Build();
}
