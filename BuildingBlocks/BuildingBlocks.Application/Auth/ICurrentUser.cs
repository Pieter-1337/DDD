namespace BuildingBlocks.Application.Auth;

/// <summary>
/// Provides access to the current authenticated user's information.
/// Implemented by infrastructure (e.g., HttpContextCurrentUser) and consumed
/// by application layer services (validators, handlers).
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// Gets the unique identifier of the current user (from "sub" claim).
    /// Null if not authenticated.
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// Gets the display name of the current user (from "name" claim).
    /// Null if not authenticated.
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// Gets the email address of the current user (from "email" claim).
    /// Null if not authenticated.
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// Gets the roles assigned to the current user (from "role" claims).
    /// Empty collection if not authenticated.
    /// </summary>
    IReadOnlyList<string> Roles { get; }

    /// <summary>
    /// Indicates whether the current user is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Checks if the current user has a specific role.
    /// </summary>
    /// <param name="role">The role name to check (use AppRoles constants).</param>
    /// <returns>True if the user has the role, false otherwise.</returns>
    bool HasRole(string role) => Roles.Contains(role);
}
