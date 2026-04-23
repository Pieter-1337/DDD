namespace Identity.WebApi.Pages.Account;

using Duende.IdentityServer.Events;
using Duende.IdentityServer.Extensions;
using Duende.IdentityServer.Services;
using Identity.WebApi.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class LogoutModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IIdentityServerInteractionService _interaction;
    private readonly IEventService _events;
    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(
        SignInManager<ApplicationUser> signInManager,
        IIdentityServerInteractionService interaction,
        IEventService events,
        ILogger<LogoutModel> logger)
    {
        _signInManager = signInManager;
        _interaction = interaction;
        _events = events;
        _logger = logger;
    }

    [BindProperty]
    public string? LogoutId { get; set; }

    public async Task<IActionResult> OnGet(string? logoutId)
    {
        LogoutId = logoutId;

        // Check if we need to show a confirmation prompt
        var context = await _interaction.GetLogoutContextAsync(logoutId);

        if (!LogoutOptions.ShowLogoutPrompt || context?.ShowSignoutPrompt == false)
        {
            // No prompt needed — sign out directly
            return await OnPost();
        }

        return Page();
    }

    public async Task<IActionResult> OnPost()
    {
        // Get the logout context before we sign out (we need the user's claims)
        var logout = await _interaction.GetLogoutContextAsync(LogoutId);

        if (User.Identity?.IsAuthenticated == true)
        {
            var subjectId = User.GetSubjectId();

            // Sign out of ASP.NET Identity
            await _signInManager.SignOutAsync();

            // Raise the logout success event
            await _events.RaiseAsync(new UserLogoutSuccessEvent(subjectId, User.GetDisplayName()));

            _logger.LogInformation("User {SubjectId} logged out.", subjectId);
        }

        return RedirectToPage("/Account/LoggedOut", new { logoutId = LogoutId });
    }
}
