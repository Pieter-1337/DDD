using Identity.WebApi.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace Identity.WebApi.Tests;

[TestClass]
public sealed class IdentityServerConfigTests
{
    // -----------------------------------------------------------------------
    // Slot 1: canonical URLs (unchanged from before slot awareness)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Clients_Slot1_UsesCanonicalPorts()
    {
        var clients = IdentityServerConfig.Clients(1).ToList();

        var scheduling = clients.Single(c => c.ClientId == "scheduling-api");
        scheduling.RedirectUris.Single().ShouldBe("https://localhost:7001/signin-oidc");
        scheduling.PostLogoutRedirectUris.Single().ShouldBe("https://localhost:7001/signout-callback-oidc");

        var billing = clients.Single(c => c.ClientId == "billing-api");
        billing.RedirectUris.Single().ShouldBe("https://localhost:7002/signin-oidc");
        billing.PostLogoutRedirectUris.Single().ShouldBe("https://localhost:7002/signout-callback-oidc");

        var spa = clients.Single(c => c.ClientId == "angular-spa");
        spa.AllowedCorsOrigins.Single().ShouldBe("https://localhost:7003");
        spa.PostLogoutRedirectUris.Single().ShouldBe("https://localhost:7003/");
    }

    [TestMethod]
    public void Clients_DefaultParameter_EqualsSlot1()
    {
        var withDefault = IdentityServerConfig.Clients().ToList();
        var withExplicit1 = IdentityServerConfig.Clients(1).ToList();

        withDefault.Select(c => c.ClientId)
            .ShouldBe(withExplicit1.Select(c => c.ClientId));

        var defaultSpa = withDefault.Single(c => c.ClientId == "angular-spa");
        var explicitSpa = withExplicit1.Single(c => c.ClientId == "angular-spa");
        defaultSpa.AllowedCorsOrigins.Single()
            .ShouldBe(explicitSpa.AllowedCorsOrigins.Single());
    }

    // -----------------------------------------------------------------------
    // Slot 2: ports shifted by +100
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Clients_Slot2_UsesShiftedPorts()
    {
        var clients = IdentityServerConfig.Clients(2).ToList();

        var scheduling = clients.Single(c => c.ClientId == "scheduling-api");
        scheduling.RedirectUris.Single().ShouldBe("https://localhost:7101/signin-oidc");
        scheduling.PostLogoutRedirectUris.Single().ShouldBe("https://localhost:7101/signout-callback-oidc");

        var billing = clients.Single(c => c.ClientId == "billing-api");
        billing.RedirectUris.Single().ShouldBe("https://localhost:7102/signin-oidc");
        billing.PostLogoutRedirectUris.Single().ShouldBe("https://localhost:7102/signout-callback-oidc");

        var spa = clients.Single(c => c.ClientId == "angular-spa");
        spa.AllowedCorsOrigins.Single().ShouldBe("https://localhost:7103");
        spa.PostLogoutRedirectUris.Single().ShouldBe("https://localhost:7103/");
    }

    // -----------------------------------------------------------------------
    // Slot 5: ports shifted by +400
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Clients_Slot5_UsesShiftedPorts()
    {
        var clients = IdentityServerConfig.Clients(5).ToList();

        var scheduling = clients.Single(c => c.ClientId == "scheduling-api");
        scheduling.RedirectUris.Single().ShouldBe("https://localhost:7401/signin-oidc");

        var billing = clients.Single(c => c.ClientId == "billing-api");
        billing.RedirectUris.Single().ShouldBe("https://localhost:7402/signin-oidc");

        var spa = clients.Single(c => c.ClientId == "angular-spa");
        spa.AllowedCorsOrigins.Single().ShouldBe("https://localhost:7403");
    }

    // -----------------------------------------------------------------------
    // No static per-slot list: each slot returns exactly 3 clients
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Clients_EachSlot_ReturnsExactlyThreeClients()
    {
        for (var slot = 1; slot <= 5; slot++)
        {
            IdentityServerConfig.Clients(slot).Count().ShouldBe(3, $"slot {slot}");
        }
    }
}
