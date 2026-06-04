using BuildingBlocks.WorktreeSlots;
using Duende.IdentityServer;
using Duende.IdentityServer.Models;

namespace Identity.WebApi.Config
{
    /// <summary>
    /// Duende IdentityServer configuration for identity resources, API scopes, and clients.
    /// </summary>
    public class IdentityServerConfig
    {
        /// <summary>
        /// Identity resources define user identity data that can be requested via scopes.
        /// </summary>
        public static IEnumerable<IdentityResource> IdentityResources =>
        [
            new IdentityResources.OpenId(),
            new IdentityResources.Profile(),
            new IdentityResources.Email(),
            new IdentityResource("roles", "User roles", new[] { "role" })
        ];

        /// <summary>
        /// API scopes define permissions that clients can request for accessing APIs.
        /// </summary>
        public static IEnumerable<ApiScope> ApiScopes =>
        [
            new ApiScope("scheduling_api", "Scheduling API"),
            new ApiScope("billing_api", "Billing API")
        ];

        /// <summary>
        /// Returns the OIDC clients for this slot only, with redirect/post-logout/CORS URLs
        /// derived from the slot number. Slot 1 produces the canonical localhost URLs;
        /// slots 2–5 shift all ports by 100*(slot-1) (same formula as WorktreeSlot.Port).
        /// Each slot's Identity seeds only its own URLs into its own IdentityDb_S{N}.
        /// </summary>
        public static IEnumerable<Client> Clients(int slot = 1)
        {
            var schedulingPort = WorktreeSlot.Port(WorktreeSlot.SchedulingBasePort, slot);
            var billingPort = WorktreeSlot.Port(WorktreeSlot.BillingBasePort, slot);
            var spaPort = WorktreeSlot.Port(WorktreeSlot.SpaBasePort, slot);
            var vuePort = WorktreeSlot.Port(WorktreeSlot.VueBasePort, slot);

            return
            [
                new Client
                {
                    ClientId = "billing-api",
                    ClientName = "Billing API",
                    ClientSecrets = { new Secret("billing-secret".Sha256()) },

                    AllowedGrantTypes = GrantTypes.Code,
                    RequirePkce = true,

                    RedirectUris = { $"https://localhost:{billingPort}/signin-oidc" },
                    PostLogoutRedirectUris = { $"https://localhost:{billingPort}/signout-callback-oidc" },

                    AllowedScopes =
                    {
                        IdentityServerConstants.StandardScopes.OpenId,
                        IdentityServerConstants.StandardScopes.Profile,
                        IdentityServerConstants.StandardScopes.Email,
                        "roles",
                        "billing_api"
                    },

                    AllowOfflineAccess = true
                },
                new Client
                {
                    ClientId = "scheduling-api",
                    ClientName = "Scheduling API",

                    ClientSecrets = { new Secret("scheduling-secret".Sha256()) },

                    AllowedGrantTypes = GrantTypes.Code,
                    RequirePkce = true,

                    RedirectUris = { $"https://localhost:{schedulingPort}/signin-oidc" },
                    PostLogoutRedirectUris = { $"https://localhost:{schedulingPort}/signout-callback-oidc" },

                    AllowedScopes =
                    {
                        IdentityServerConstants.StandardScopes.OpenId,
                        IdentityServerConstants.StandardScopes.Profile,
                        IdentityServerConstants.StandardScopes.Email,
                        "roles",
                        "scheduling_api"
                    },

                    AllowOfflineAccess = true
                },
                new Client
                {
                    ClientId = "angular-spa",
                    ClientName = "Angular SPA",

                    AllowedGrantTypes = GrantTypes.Code,
                    RequirePkce = true,
                    RequireClientSecret = false,

                    RedirectUris =
                    {
                        $"https://localhost:{spaPort}/callback",
                        $"https://localhost:{spaPort}/silent-refresh.html",
                        $"https://localhost:{vuePort}/callback",
                        $"https://localhost:{vuePort}/silent-refresh.html"
                    },
                    PostLogoutRedirectUris = { $"https://localhost:{spaPort}/", $"https://localhost:{vuePort}/" },
                    AllowedCorsOrigins = { $"https://localhost:{spaPort}", $"https://localhost:{vuePort}" },

                    AllowedScopes =
                    {
                        IdentityServerConstants.StandardScopes.OpenId,
                        IdentityServerConstants.StandardScopes.Profile,
                        IdentityServerConstants.StandardScopes.Email,
                        "roles",
                        "scheduling_api",
                        "billing_api"
                    },

                    AllowOfflineAccess = true
                }
            ];
        }
    }
}
