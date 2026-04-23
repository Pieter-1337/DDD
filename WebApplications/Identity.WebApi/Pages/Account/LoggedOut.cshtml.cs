namespace Identity.WebApi.Pages.Account;

using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[AllowAnonymous]
public class LoggedOutModel : PageModel
{
    private readonly IIdentityServerInteractionService _interaction;

    public LoggedOutModel(IIdentityServerInteractionService interaction)
    {
        _interaction = interaction;
    }

    public string? PostLogoutRedirectUri { get; set; }
    public string? ClientName { get; set; }
    public bool AutomaticRedirectAfterSignOut => LogoutOptions.AutomaticRedirectAfterSignOut;

    public async Task<IActionResult> OnGet(string? logoutId)
    {
        var context = await _interaction.GetLogoutContextAsync(logoutId);

        PostLogoutRedirectUri = context?.PostLogoutRedirectUri;
        ClientName = context?.ClientName ?? context?.ClientId;

        // If we have a redirect URI and auto-redirect is on, the JS will handle it
        return Page();
    }
}
