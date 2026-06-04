using Duende.IdentityServer.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace Identity.WebApi.Tests;

/// <summary>
/// Guards the mechanism the dev-only cookie isolation relies on: setting
/// IdentityServerOptions.Authentication.CheckSessionCookieName via a post-hoc
/// Services.Configure&lt;IdentityServerOptions&gt;(…) (not inside the AddIdentityServer
/// lambda) must still land when IOptions&lt;IdentityServerOptions&gt; is resolved —
/// i.e. Duende must not reset it with a later PostConfigure. This reproduces the
/// exact registration shape of Identity.WebApi/Program.cs + WorktreeSlotCookieIsolation.
/// </summary>
[TestClass]
public sealed class CheckSessionCookieConfigureTests
{
    [TestMethod]
    public void PostConfigure_CheckSessionCookieName_SurvivesAddIdentityServer()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Mimic Program.cs: the AddIdentityServer lambda no longer touches CheckSessionCookieName.
        services.AddIdentityServer(options =>
        {
            options.EmitStaticAudienceClaim = true;
        });

        // Mimic AddWorktreeSlotCookieIsolation() for slot 2.
        services.Configure<IdentityServerOptions>(o => o.Authentication.CheckSessionCookieName = "idsrv.session.S2");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<IdentityServerOptions>>().Value;

        options.Authentication.CheckSessionCookieName.ShouldBe("idsrv.session.S2");
    }
}
