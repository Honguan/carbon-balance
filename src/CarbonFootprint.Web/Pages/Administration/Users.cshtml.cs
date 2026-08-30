using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Web.Pages.Administration;

[Authorize(Roles = SystemRoles.Administrator)]
public sealed class UsersModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SystemAdministratorService _systemAdministratorService;
    private readonly IAuthorizationService _authorizationService;

    public UsersModel(
        UserManager<ApplicationUser> userManager,
        SystemAdministratorService systemAdministratorService,
        IAuthorizationService authorizationService)
    {
        _userManager = userManager;
        _systemAdministratorService = systemAdministratorService;
        _authorizationService = authorizationService;
    }

    public IReadOnlyList<ApplicationUser> Users { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Users = await _userManager.Users
            .AsNoTracking()
            .OrderBy(user => user.Email)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostGrantAdministratorAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var mfa = await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            new MfaEnabledRequirement());
        if (!mfa.Succeeded || !Guid.TryParse(_userManager.GetUserId(User), out var actorId))
        {
            return Forbid();
        }

        var result = await _systemAdministratorService.GrantAdministratorAsync(
            actorId,
            userId,
            HttpContext.TraceIdentifier,
            cancellationToken);
        if (!result.Succeeded)
        {
            return Forbid();
        }

        StatusMessage = "系統管理者角色已指派。";
        return RedirectToPage();
    }
}
