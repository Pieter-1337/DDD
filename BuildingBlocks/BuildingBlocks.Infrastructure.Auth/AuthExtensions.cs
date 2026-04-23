using BuildingBlocks.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.Auth
{
    /// <summary>
    /// Extension methods for configuring cookie-based authentication with OIDC.
    /// </summary>
    public static class AuthExtensions
    {
        /// <summary>
        /// Adds cookie authentication with OpenID Connect (OIDC) client configured for any OIDC-compliant authorization server (e.g., Duende IdentityServer).
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration containing Auth:Authority, Auth:ClientId, Auth:ClientSecret, and Auth:SharedKeysPath.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddOidcCookieAuth(
            this IServiceCollection services,
             IConfiguration configuration)
        {
            //Read auth configuration
            var authority = configuration["Auth:Authority"] ?? throw new InvalidOperationException("Auth:Authority is not configured but required.");
            var clientId = configuration["Auth:ClientId"] ?? throw new InvalidOperationException("Auth:ClientId is not configured but required.");
            var clientSecret = configuration["Auth:ClientSecret"] ?? throw new InvalidOperationException("Auth:ClientSecret is not configured but required.");
            var sharedKeysPath = configuration["Auth:SharedKeysPath"];

            //1. Configure cookie auth as the default scheme
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = "oidc"; // Custom name for OIDC handler
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options => 
            {
                options.Cookie.Name = "DDD.Auth";
                options.Cookie.HttpOnly = true; //Prevent javascript accessing the cookie => XSS protection
                options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax; //CSRF protection while allowing cross-origin for top-level navigation
                options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always; // HTTPS only
                options.ExpireTimeSpan = TimeSpan.FromHours(8); //Cookie lifetime
                options.SlidingExpiration = true; //Refresh cookie on activity

                //Redirect unauthenticated requests to login
                options.LoginPath = "/auth/login";
                options.LogoutPath = "/auth/logout";
                options.AccessDeniedPath = "/auth/access-denied";

                // options.Events = new CookieAuthenticationEvents
                // {
                //     OnRedirectToLogin = context => { ... }
                // };
            })
            //2. Configure OIDC handler for authentication challenges
            .AddOpenIdConnect("oidc", options =>
            {
                options.Authority = authority; //IDP
                options.ClientId = clientId; //Client ID registered with the IDP
                options.ClientSecret = clientSecret; //Client secret registered with the IDP
                options.ResponseType = "code"; //Authorization Code flow
                options.RequireHttpsMetadata = true; //Require HTTPS for metadata endpoint

                // Request scopes (must match what Auth server allows for this client)
                options.Scope.Clear();
                options.Scope.Add("openid"); // Required for OIDC
                options.Scope.Add("profile"); // Get name, given_name, family_name, etc.
                options.Scope.Add("email"); // Get email claim (not included in profile scope)
                options.Scope.Add("roles"); // Get role claims

                // Save tokens in cookie properties (access_token, refresh_token)
                // Set to false for cookie-only auth (tokens stay on server)
                options.SaveTokens = false;

                // Map OIDC claims to .NET ClaimTypes
                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType = "role";

                // ClaimActions run on the userinfo JSON (called when GetClaimsFromUserInfoEndpoint=true)
                // and copy claims into the cookie identity. Anything not mapped here is dropped.
                // MapJsonKey handles arrays — for multi-valued claims like 'role' it emits one
                // Claim per array element.
                options.ClaimActions.MapJsonKey("sub", "sub");
                options.ClaimActions.MapJsonKey("name", "name");
                options.ClaimActions.MapJsonKey("email", "email");
                options.ClaimActions.MapJsonKey("email_verified", "email_verified");
                options.ClaimActions.MapJsonKey("role", "role");

                // OIDC event hooks — listed in execution order, uncomment as needed
                options.Events = new Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectEvents
                {
                    // 1. Before redirecting to the Identity Provider login page
                    // Return 401 for AJAX requests instead of redirecting to IdentityServer.
                    // The DefaultChallengeScheme is "oidc", so [Authorize] failures trigger
                    // this event directly — bypassing the cookie's OnRedirectToLogin.
                    // Angular's auth interceptor sends X-Requested-With: XMLHttpRequest on all requests.
                    OnRedirectToIdentityProvider = context =>
                    {
                        if (context.Request.Headers.XRequestedWith == "XMLHttpRequest")
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            context.HandleResponse();
                            return Task.CompletedTask;
                        }
                        return Task.CompletedTask;
                    },

                    // 2. After receiving the authorization code, before exchanging it for tokens
                    // Use for: inspecting or modifying the code exchange
                    // OnAuthorizationCodeReceived = context =>
                    // {
                    //     return Task.CompletedTask;
                    // },

                    // 3. After receiving tokens from the token endpoint
                    // Use for: storing tokens, logging token metadata
                    // OnTokenResponseReceived = context =>
                    // {
                    //     return Task.CompletedTask;
                    // },

                    // 4. After ID token is validated, before claims are saved to cookie
                    // Use for: transforming/adding/removing claims (e.g., map IdentityServer claims to your app's claim structure)
                    OnTokenValidated = context =>
                    {
                        // Store ONLY the id_token in auth properties so it can be used as
                        // id_token_hint on logout. Duende's end-session validator requires
                        // id_token_hint to populate the logout context (client_id alone is
                        // not enough). SaveTokens=false means we skip access/refresh tokens,
                        // keeping the cookie small.
                        var idToken = context.TokenEndpointResponse?.IdToken;
                        if (!string.IsNullOrEmpty(idToken) && context.Properties is not null)
                        {
                            context.Properties.StoreTokens(new[]
                            {
                                new AuthenticationToken { Name = "id_token", Value = idToken }
                            });
                        }
                        return Task.CompletedTask;
                    },

                    // 5. After calling the userinfo endpoint (if enabled)
                    // Use for: merging additional claims from the userinfo response
                    // OnUserInformationReceived = context =>
                    // {
                    //     return Task.CompletedTask;
                    // },

                    // Error handler — fires when something goes wrong at any point in the flow
                    // Use for: custom error handling, redirecting to an error page
                    // OnRemoteFailure = context =>
                    // {
                    //     return Task.CompletedTask;
                    // },

                    // Before redirecting to the auth server's end-session endpoint for sign-out
                    // Pull the id_token stored by OnTokenValidated from the cookie and set it
                    // as id_token_hint. Duende 7.x only populates the logout context (including
                    // PostLogoutRedirectUri) when id_token_hint is present — client_id alone
                    // is ignored by EndSessionRequestValidator.
                    OnRedirectToIdentityProviderForSignOut = async context =>
                    {
                        var authResult = await context.HttpContext.AuthenticateAsync(
                            CookieAuthenticationDefaults.AuthenticationScheme);
                        var idToken = authResult?.Properties?.GetTokenValue("id_token");
                        if (!string.IsNullOrEmpty(idToken))
                        {
                            context.ProtocolMessage.IdTokenHint = idToken;
                        }
                    },

                    // Logout flow — after the logout callback redirect from the auth server
                    // Use for: custom post-logout redirect logic
                    // OnSignedOutCallbackRedirect = context =>
                    // {
                    //     return Task.CompletedTask;
                    // },
                };
                // Fetch identity claims (name, email, role, ...) from the userinfo endpoint.
                // Duende's default for authorization code flow is to keep the id_token lean:
                // only 'sub' is included when an access_token is also issued. The OIDC middleware
                // will call /connect/userinfo after the code exchange and merge the claims into
                // the identity.
                options.GetClaimsFromUserInfoEndpoint = true;
            });

            // 3. Configure Data Protection for shared cookie encryption
            var dataProtection = services.AddDataProtection()
                .SetApplicationName("DDD.WebApis"); // MUST be same across all APIs

            if (!string.IsNullOrEmpty(sharedKeysPath))
            {
                dataProtection.PersistKeysToFileSystem(new DirectoryInfo(sharedKeysPath));
            }

            // Register ICurrentUser for accessing current user information
            services.AddHttpContextAccessor();
            services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

            return services;
        }
    }
}
