using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Security;

namespace Umbricians.LiveView.Demo.DemoMembers;

/// <summary>
/// A bare test page at <c>/demo/members</c> for logging in and out as the test members.
/// Demo only: not part of the package.
/// </summary>
[Route("demo/members")]
public class DemoMembersController : Controller
{
    private readonly IMemberSignInManager _signInManager;
    private readonly IMemberManager _memberManager;
    private readonly IOptions<DemoMembersOptions> _options;

    public DemoMembersController(
        IMemberSignInManager signInManager,
        IMemberManager memberManager,
        IOptions<DemoMembersOptions> options)
    {
        _signInManager = signInManager;
        _memberManager = memberManager;
        _options = options;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? error = null)
    {
        MemberIdentityUser? current = await _memberManager.GetCurrentMemberAsync();
        return View(
            "~/Views/DemoMembers/Index.cshtml",
            new DemoMembersViewModel(current?.Name, current?.Email, _options.Value.Accounts.ToList(), error));
    }

    /// <summary>Logs in as a test member. The shared test password comes from configuration.</summary>
    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email)
    {
        if (string.IsNullOrEmpty(_options.Value.Password)
            || !_options.Value.Accounts.Any(a => a.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
        {
            return RedirectToAction(nameof(Index), new { error = "Unknown test member." });
        }

        var result = await _signInManager.PasswordSignInAsync(email, _options.Value.Password, isPersistent: false, lockoutOnFailure: false);
        return result.Succeeded
            ? RedirectToAction(nameof(Index))
            : RedirectToAction(nameof(Index), new { error = "Login failed." });
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Index));
    }
}

public record DemoMembersViewModel(
    string? CurrentName,
    string? CurrentEmail,
    IReadOnlyList<(string Email, string Name)> Accounts,
    string? Error);
