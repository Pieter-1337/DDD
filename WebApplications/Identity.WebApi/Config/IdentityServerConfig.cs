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
        /// Clients represent applications that can request tokens from this authorization server.
        /// </summary>
        public static IEnumerable<Client> Clients =>
        [
            new Client
            {
                ClientId = "billing-api",
                ClientName = "Billing API",
                ClientSecrets = { new Secret("scheduling-secret".Sha256()) },

                AllowedGrantTypes = GrantTypes.Code,
                RequirePkce = true,

                RedirectUris = { "https://localhost:7001/signin-oidc" },
                PostLogoutRedirectUris = { "https://localhost:7001/signout-callback-oidc" },

                AllowedScopes =
                {
                    IdentityServerConstants.StandardScopes.OpenId,
                    IdentityServerConstants.StandardScopes.Profile,
                    IdentityServerConstants.StandardScopes.Email,
                    "roles",
                    "scheduling_api"
                },

                AllowOfflineAccess = true // Enable refresh tokens
            },
            new Client
            {
                ClientId = "scheduling-api",
                ClientName = "Scheduling API",

                ClientSecrets = { new Secret("billing-secret".Sha256()) },

                AllowedGrantTypes = GrantTypes.Code,
                RequirePkce = true,

                RedirectUris = { "https://localhost:7002/signin-oidc" },
                PostLogoutRedirectUris = { "https://localhost:7002/signout-callback-oidc" },

                AllowedScopes =
                {
                    IdentityServerConstants.StandardScopes.OpenId,
                    IdentityServerConstants.StandardScopes.Profile,
                    IdentityServerConstants.StandardScopes.Email,
                    "roles",
                    "billing_api"
                },

                AllowOfflineAccess = true // Enable refresh tokens
            },
            new Client 
            {
                ClientId = "angular-spa",
                ClientName = "Angular SPA",

                AllowedGrantTypes = GrantTypes.Code,
                RequirePkce = true,
                RequireClientSecret = false, // Public client (SPA can't keep secrets)

                RedirectUris =
                {
                    "https://localhost:7003/callback",
                    "https://localhost:7003/silent-refresh.html"
                },
                PostLogoutRedirectUris = { "https://localhost:7003/" },
                AllowedCorsOrigins = { "https://localhost:7003" },

                AllowedScopes =
                {
                    IdentityServerConstants.StandardScopes.OpenId,
                    IdentityServerConstants.StandardScopes.Profile,
                    IdentityServerConstants.StandardScopes.Email,
                    "roles",
                    "scheduling_api",
                    "billing_api"
                },

                AllowOfflineAccess = true // Enable refresh tokens
            }
        ];
    }
}
