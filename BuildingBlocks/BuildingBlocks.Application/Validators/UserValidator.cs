using FluentValidation;

namespace BuildingBlocks.Application.Validators
{
    public abstract class UserValidator<T> : AbstractValidator<T>
    {
        //protected readonly IUserContext UserContext;

        //private IEnumerable<IEnumerable<string>> _allowedRoleGroups;

        ///// <summary>
        ///// Validates the roles of the current user.
        ///// Example of parameter: [roleA and roleB and roleC],OR [roleX]
        ///// </summary>
        ///// <param name="allowedRoleGroups">OR params lists of 'AND list roles'</param>
        //protected UserValidator(IUserContext userContext, params IEnumerable<string>[] allowedRoleGroups)
        //{
        //    UserContext = userContext;

        //    _allowedRoleGroups = allowedRoleGroups.Where(g => g != null && g.Any(role => !role.IsNullOrWhitespace()))//exclude null and empty groups
        //        .Select(g => g.Where(role => !role.IsNullOrWhitespace()));//exclude null and empty roles
        //}
        //protected IRuleBuilderOptions<T, T> RuleForUserValidation()
        //{
        //    return RuleFor(r => r)
        //        .Must(r => HaveAValidRole(_allowedRoleGroups))
        //            .WithMessage(m => Resources.ValidationMessages.role_notallowed)
        //            .WithErrorCode(Resources.ValidationCodes.http_403_forbidden);
        //}

        //private bool HaveAValidRole(IEnumerable<IEnumerable<string>> allowedRoleGroups)
        //{
        //    if (!allowedRoleGroups.Any())
        //        return true;

        //    foreach (var allowedRoleGroup in allowedRoleGroups)
        //    {
        //        if (allowedRoleGroup.All(role => UserContext.HasApplicationRole(role)))
        //            return true;
        //    }
        //    return false;
        //}

        ///// <summary>
        ///// ! Should only be used for tests !
        ///// </summary>
        //public IEnumerable<IEnumerable<string>> GetAllowedRoleGroups() => _allowedRoleGroups;
        ///// <summary>
        ///// ! Should only be used for tests !
        ///// </summary>
        //public void SetAllowedRoleGroups(params IEnumerable<string>[] allowedRoleGroups) => _allowedRoleGroups = allowedRoleGroups;
    }
}
