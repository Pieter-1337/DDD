using BuildingBlocks.Application.Auth;
using BuildingBlocks.Enumerations;
using FluentValidation;

namespace BuildingBlocks.Application.Validators
{
    /// <summary>
    /// Base validator that provides role-based authorization validation.
    /// Validators can specify allowed role groups using AND/OR logic.
    /// </summary>
    /// <typeparam name="T">The type being validated (typically a command or query).</typeparam>
    public abstract class UserValidator<T> : AbstractValidator<T>
    {
        /// <summary>
        /// The current authenticated user. Available to derived validators for custom logic.
        /// </summary>
        protected readonly ICurrentUser CurrentUser;

        /// <summary>
        /// The allowed role groups for this validator.
        /// Outer collection is OR, inner collection is AND.
        /// Example: [["Admin"], ["Doctor", "Nurse"]] means "Admin" OR ("Doctor" AND "Nurse").
        /// </summary>
        private readonly IEnumerable<IEnumerable<string>> _allowedRoleGroups;

        /// <summary>
        /// Constructor for validators that require role-based validation.
        /// </summary>
        /// <param name="currentUser">The current user context.</param>
        /// <param name="allowedRoleGroups">
        /// One or more role groups. A user must have ALL roles in at least ONE group to pass validation.
        /// Example: new[] { "Admin" }, new[] { "Doctor", "Nurse" } means "Admin" OR ("Doctor" AND "Nurse").
        /// </param>
        protected UserValidator(ICurrentUser currentUser, params IEnumerable<string>[] allowedRoleGroups)
        {
            CurrentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));

            // Filter out null or empty role groups
            _allowedRoleGroups = allowedRoleGroups
                .Where(g => g != null && g.Any(role => !string.IsNullOrWhiteSpace(role)))
                .Select(g => g.Where(role => !string.IsNullOrWhiteSpace(role)))
                .ToList();

            // Auto-register the role check so derived validators can't silently bypass it
            // by forgetting to invoke it. Runs before any rules registered in the derived ctor.
            RuleFor(r => r)
                .Must(_ => HaveAValidRole())
                    .WithMessage(ErrorCode.Forbidden.Message)
                    .WithErrorCode(ErrorCode.Forbidden.Value);
        }

        /// <summary>
        /// Checks if the current user satisfies at least one role group.
        /// A user must have ALL roles in at least ONE group to pass.
        /// </summary>
        private bool HaveAValidRole()
        {
            foreach (var group in _allowedRoleGroups)
            {
                if (group.All(role => CurrentUser.HasRole(role)))
                    return true;
            }

            return false;
        }
    }
}
