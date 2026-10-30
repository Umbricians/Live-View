using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Security;
using Umbricians.LiveView.Messaging.Members;

namespace Umbricians.LiveView.Demo.DemoMembers;

/// <summary>
/// A bare test page at <c>/demo/members</c> for logging in and out as the test members, and for seeing member
/// messages sent from the Live View dashboard. Demo only: not part of the package.
/// </summary>
[Route("demo/members")]
public class DemoMembersController : Controller
{
    private readonly IMemberSignInManager _signInManager;
    private readonly IMemberManager _memberManager;
    private readonly IMemberMessageInbox _inbox;
    private readonly IOptions<DemoMembersOptions> _options;

    public DemoMembersController(
        IMemberSignInManager signInManager,
        IMemberManager memberManager,
        IMemberMessageInbox inbox,
        IOptions<DemoMembersOptions> options)
    {
        _signInManager = signInManager;
        _memberManager = memberManager;
        _inbox = inbox;
        _options = options;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? error = null)
    {
        MemberIdentityUser? current = await _memberManager.GetCurrentMemberAsync();
        IReadOnlyList<MemberMessage> banners = current is null ? [] : _inbox.GetUnread(current.Key);

        return View(
            "~/Views/DemoMembers/Index.cshtml",
            new DemoMembersViewModel(current?.Name, current?.Email, _options.Value.Accounts.ToList(), banners, error));
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

    /// <summary>Hides a banner message for the current member.</summary>
    [HttpPost("dismiss/{messageKey:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dismiss(Guid messageKey)
    {
        if (await _memberManager.GetCurrentMemberAsync() is { } current)
        {
            _inbox.MarkRead(current.Key, messageKey);
        }

        return RedirectToAction(nameof(Index));
    }
}

public record DemoMembersViewModel(
    string? CurrentName,
    string? CurrentEmail,
    IReadOnlyList<(string Email, string Name)> Accounts,
    IReadOnlyList<MemberMessage> Banners,
    string? Error);
