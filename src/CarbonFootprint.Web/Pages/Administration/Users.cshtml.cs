using CarbonFootprint.Infrastructure.Identity;
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

    public UsersModel(
        UserManager<ApplicationUser> userManager,
        SystemAdministratorService systemAdministratorService)
    {
        _userManager = userManager;
        _systemAdministratorService = systemAdministratorService;
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
        if (!Guid.TryParse(_userManager.GetUserId(User), out var actorId))
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
