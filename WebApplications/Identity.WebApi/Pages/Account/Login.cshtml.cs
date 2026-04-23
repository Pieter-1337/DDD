namespace Identity.WebApi.Pages.Account;

using System.ComponentModel.DataAnnotations;
using Duende.IdentityServer.Events;
using Duende.IdentityServer.Services;
using Identity.WebApi.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IIdentityServerInteractionService _interaction;
    private readonly IEventService _events;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        IIdentityServerInteractionService interaction,
        IEventService events,
        ILogger<LoginModel> logger)
    {
        _signInManager = signInManager;
        _interaction = interaction;
        _events = events;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    public string? ReturnUrl { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public bool AllowCancel { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = default!;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = default!;

        [Display(Name = "Remember me?")]
        public bool RememberMe { get; set; }
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            ModelState.AddModelError(string.Empty, ErrorMessage);
        }

        returnUrl ??= Url.Content("~/");

        // Check if this login was triggered by an OIDC authorization request
        var context = await _interaction.GetAuthorizationContextAsync(returnUrl);
        AllowCancel = context != null;

        // Clear the existing external cookie to ensure a clean login process
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null, string? button = null)
    {
        returnUrl ??= Url.Content("~/");

        // Check OIDC context
        var context = await _interaction.GetAuthorizationContextAsync(returnUrl);

        // Handle cancel button
        if (button == "cancel")
        {
            if (context != null)
            {
                // Deny the authorization request — sends error back to client
                await _interaction.DenyAuthorizationAsync(context, Duende.IdentityServer.Models.AuthorizationError.AccessDenied);
                return Redirect(returnUrl);
            }

            return Redirect("~/");
        }

        if (ModelState.IsValid)
        {
            var result = await _signInManager.PasswordSignInAsync(
                Input.Email,
                Input.Password,
                Input.RememberMe,
                lockoutOnFailure: false);

            if (result.Succeeded)
            {
                var user = await _signInManager.UserManager.FindByEmailAsync(Input.Email);
                await _events.RaiseAsync(new UserLoginSuccessEvent(
                    user!.UserName, user.Id, user.UserName, clientId: context?.Client.ClientId));

                _logger.LogInformation("User {Email} logged in.", Input.Email);
                return Redirect(returnUrl);
            }
            if (result.RequiresTwoFactor)
            {
                return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, Input.RememberMe });
            }
            if (result.IsLockedOut)
            {
                _logger.LogWarning("User account locked out.");
                return RedirectToPage("./Lockout");
            }

            await _events.RaiseAsync(new UserLoginFailureEvent(
                Input.Email, "Invalid credentials", clientId: context?.Client.ClientId));

            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        }

        // Redisplay form
        AllowCancel = context != null;
        return Page();
    }
}
