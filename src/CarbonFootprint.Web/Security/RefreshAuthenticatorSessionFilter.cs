using CarbonFootprint.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CarbonFootprint.Web.Security;

public sealed class RefreshAuthenticatorSessionFilter(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(
        PageHandlerExecutingContext context,
        PageHandlerExecutionDelegate next)
    {
        var user = await userManager.GetUserAsync(context.HttpContext.User);
        var originalStamp = user?.SecurityStamp;
        var executed = await next();

        // The built-in enrollment page rotates the stamp without refreshing its own session.
        // Refresh only this authenticated session; other cookies remain invalidated, and amr is preserved.
        if (user is not null && user.SecurityStamp != originalStamp
            && executed.Exception is null && !executed.Canceled
            && executed.Result is PageResult or RedirectToPageResult)
        {
            await signInManager.RefreshSignInAsync(user);
        }
    }
}
