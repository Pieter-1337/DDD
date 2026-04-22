namespace Auth
{
    /// <summary>
    /// Centralized role constants used across all bounded contexts.
    /// These roles must match the roles configured in IdentityServer (see doc 02).
    /// Used by UserValidator&lt;T&gt; in the application layer for role-based authorization (see doc 06).
    /// </summary>
    public static class AppRoles
    {
        public const string Admin = "Admin";
        public const string Doctor = "Doctor";
        public const string Nurse = "Nurse";
    }
}
