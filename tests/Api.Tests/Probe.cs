using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MockBucket.Core;

namespace MockBucket.Tests;

/// <summary>
/// One host per instance; in-memory state is per host. Tweak lets a test replace CaptureOptions
/// before first resolution (ConfigureTestServices runs after the app's own registrations, last wins).
/// </summary>
public sealed class Probe : WebApplicationFactory<Program>
{
    public Func<CaptureOptions, CaptureOptions>? Tweak { get; init; }

    public HttpClient Client => CreateClient();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            var options = CaptureOptions.FromEnvironment();
            if (Tweak is not null) options = Tweak(options);
            services.AddSingleton(options);
        });
}
