using BuildingBlocks.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
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

                // Return 401 for API requests instead of redirecting to login page
                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context =>
                    {
                        if (context.Request.Headers.Accept.Any(h => h.Contains("application/json")))
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        }
                        context.Response.Redirect(context.RedirectUri);
                        return Task.CompletedTask;
                    }
                };
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
                options.Scope.Add("profile"); // Get name, email claims etc...
                options.Scope.Add("roles"); // Get role claims

                // Save tokens in cookie properties (access_token, refresh_token)
                // Set to false for cookie-only auth (tokens stay on server)
                options.SaveTokens = false; //Save tokens in the authentication properties

                // Map OIDC claims to .NET ClaimTypes
                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType = "role";

                // Map additional claims from ID token to cookie, Add more if needed
                options.ClaimActions.MapJsonKey("email", "email");
                options.ClaimActions.MapJsonKey("sub", "sub"); // Subject (user ID)

                // OIDC event hooks — listed in execution order, uncomment as needed
                options.Events = new Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectEvents
                {
                    // 1. Before redirecting to the Identity Provider login page
                    // Use for: adding extra query parameters, modifying the redirect URI
                    // OnRedirectToIdentityProvider = context =>
                    // {
                    //     return Task.CompletedTask;
                    // },

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
                        // Claims transformation can happen here if needed
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

                    // Logout flow — after the logout callback redirect from the auth server
                    // Use for: custom post-logout redirect logic
                    // OnSignedOutCallbackRedirect = context =>
                    // {
                    //     return Task.CompletedTask;
                    // },
                };
                //options.GetClaimsFromUserInfoEndpoint = true; //Get additional claims from the user info endpoint
            });

            // Register ICurrentUser for accessing current user information
            services.AddHttpContextAccessor();
            services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

            return services;
        }
    }
}
