using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CarbonFootprint.Web.Pages;

[Authorize]
public sealed class AcceptInvitationModel : PageModel
{
    private readonly OrganizationInvitationService _invitationService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public AcceptInvitationModel(
        OrganizationInvitationService invitationService,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        _invitationService = invitationService;
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [BindProperty(SupportsGet = true)]
    public string Token { get; set; } = string.Empty;

    public IActionResult OnGet()
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            return NotFound();
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        try
        {
            await _invitationService.AcceptAsync(user, Token, cancellationToken);
            await _signInManager.RefreshSignInAsync(user);
            return RedirectToPage("/Workspace");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }
}
