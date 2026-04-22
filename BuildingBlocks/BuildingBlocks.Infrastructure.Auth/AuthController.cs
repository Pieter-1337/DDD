using BuildingBlocks.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildingBlocks.Infrastructure.Auth
{
    /// <summary>
    /// Handles authentication flows: login, logout, and current user info.
    /// </summary>
    [ApiController]
    [Route("auth")]
    public class AuthController : ControllerBase
    {
        /// <summary>
        /// Initiates OIDC login flow by redirecting to the Auth Server.
        /// </summary>
        /// Invoked by the cookie middleware's LoginPath (configured in AddOidcCookieAuth).
        /// <param name="returnUrl">URL to redirect to after successful authentication.</param>
        /// <returns>Challenge result that redirects to Auth Server login page.</returns>
        [HttpGet("login")]
        public IActionResult Login([FromQuery] string? returnUrl = null)
        {
            //If already authenticated, just redirect to returnUrl
            if (User.Identity?.IsAuthenticated == true)
            {
                return Redirect(returnUrl ?? "/");
            }

            //Trigger OIDC Challenge (redirect to Auth Server)
            var authProperties = new AuthenticationProperties
            {
                RedirectUri = returnUrl ?? "/",
                IsPersistent = true //cookie persists across browser sessions
            };

            return Challenge(authProperties, "oidc");
        }

        /// <summary>
        /// Signs out the user locally and from the Auth Server.
        /// </summary>
        /// Invoked by the cookie middleware's LogoutPath (configured in AddOidcCookieAuth).
        /// <returns>Sign out result that clears cookie and redirects to Auth Server logout.</returns>
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            // Signs out of both cookie and OIDC (redirects to Auth Server logout endpoint)
            return SignOut(new AuthenticationProperties(), CookieAuthenticationDefaults.AuthenticationScheme, "oidc");
        }

        /// <summary>
        /// Returns current authenticated user information from cookie claims.
        /// </summary>
        [HttpGet("current-user")]
        [Authorize] //Only authenticated users can access their current info
        public IActionResult GetCurrentUser([FromServices] ICurrentUser currentUser)
        {
            return Ok(new
            {
                userId = currentUser.UserId,
                name = currentUser.Name,
                email = currentUser.Email,
                roles = currentUser.Roles
            });
        }

        /// <summary>
        /// Fallback endpoint for access denied scenarios.
        /// </summary>
        /// Invoked by the cookie middleware's AccessDeniedPath (configured in AddOidcCookieAuth).
        [HttpGet("access-denied")]
        public IActionResult AccessDenied()
        {
            return StatusCode(403, new { message = "Access denied. You do not have permission to access this resource." });
        }
    }
}
