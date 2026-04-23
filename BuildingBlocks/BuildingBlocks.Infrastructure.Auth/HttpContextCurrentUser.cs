using BuildingBlocks.Application.Auth;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace BuildingBlocks.Infrastructure.Auth;

/// <summary>
/// Provides access to the current authenticated user from the HTTP context.
/// </summary>
internal sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Gets the current user's unique identifier (sub claim).
    /// </summary>
    public string? UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return null;

            return user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub");
        }
    }

    /// <summary>
    /// Gets the current user's email address.
    /// </summary>
    public string? Email
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return null;

            return user.FindFirstValue(ClaimTypes.Email)
                ?? user.FindFirstValue("email");
        }
    }

    /// <summary>
    /// Gets the current user's display name.
    /// </summary>
    public string? Name
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return null;

            return user.FindFirstValue(ClaimTypes.Name)
                ?? user.FindFirstValue("name");
        }
    }

    /// <summary>
    /// Gets the current user's roles.
    /// </summary>
    public IReadOnlyList<string> Roles
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return Array.Empty<string>();

            // Check both the SOAP/WS-Fed mapped URI and the raw "role" JWT claim —
            // .NET 9 uses JsonWebTokenHandler which does NOT remap inbound claim names,
            // so roles arrive as "role" rather than ClaimTypes.Role.
            var mapped = user.FindAll(ClaimTypes.Role).Select(c => c.Value);
            var raw = user.FindAll("role").Select(c => c.Value);
            return mapped.Concat(raw).Distinct().ToList();
        }
    }

    /// <summary>
    /// Gets whether the current user is authenticated.
    /// </summary>
    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
}
