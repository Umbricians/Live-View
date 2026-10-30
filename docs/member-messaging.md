# Messaging members

The Live View dashboard can send a message to the members logged in on your website, or to every member of the site:
"We're restarting at 18:00", "New members area launching Monday", and so on.

How a message actually reaches members is different on every site, so the package leaves that to you. You write
one or more small **senders** (a banner, a live pop-up, an email, a push notification, whatever suits the site), and
the dashboard offers them as delivery options.

- [How it works](#how-it-works)
- [Quick start: your first sender](#quick-start-your-first-sender)
- [Example 1: a banner on the next page view](#example-1-a-banner-on-the-next-page-view)
- [Example 2: a live pop-up with SignalR](#example-2-a-live-pop-up-with-signalr)
- [Example 3: email every member](#example-3-email-every-member)
- [More ideas](#more-ideas)
- [Reference](#reference)
- [Good practice](#good-practice)
- [Try it in the demo site](#try-it-in-the-demo-site)

## How it works

**Member messaging is off by default.** The package ships no senders, so until you register one the members table
has no checkboxes and there is no **Message members…** button.

Backoffice users also need the **Message members** permission (and **View Live View**) on one of their user groups.
Administrators get it automatically; grant it to other groups under **Users → User groups → (a group) → Live View**.
See [Permissions](getting-started.md#permissions).

Once at least one sender is registered:

1. On **Settings → Live View**, the backoffice user ticks members in the **Members online** table (or clicks
   **Select all N online**), then clicks **Message members…**.
2. In the slide-out they choose who it is for: **Selected members**, **Everyone online** or **Every member**. They
   tick the delivery methods to use (one for each of your senders), then write a headline, message, type and how
   long it stays relevant.
3. The package works out the recipients and calls each chosen sender with the message. Senders run side by side,
   each with 30 seconds to finish, and the dashboard shows what each one did, or that it failed.
4. Each send is written to the Umbraco log: who sent it, to whom, and through which senders.

Your sender only has to answer one question: *given this message and these members, how do I get it to them?*

## Quick start: your first sender

The smallest useful sender writes messages to the log. It is enough to see member messaging appear on the dashboard:

```csharp
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbricians.LiveView.Extensions;
using Umbricians.LiveView.Messaging.Members;

public class LogMemberMessageSender(ILogger<LogMemberMessageSender> logger) : IMemberMessageSender
{
    public string Alias => "log";
    public string Name => "Write to the log";
    public string? Description => "For testing: records the message in the Umbraco log.";

    public Task<MemberMessageResult> SendAsync(MemberMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Member message '{Headline}' for {Audience} ({Count} members)",
            message.Headline, message.Audience, message.MemberKeys.Count);

        return Task.FromResult(new MemberMessageResult(message.MemberKeys.Count, "logged"));
    }
}

public class MemberMessagingComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) => builder.AddMemberMessageSender<LogMemberMessageSender>();
}
```

Restart the site and open **Settings → Live View** as an administrator: the members table now has checkboxes, and
**Message members…** offers "Write to the log".

`Name` and `Description` are what backoffice users see, so word them for them. Register as many senders as you
like; each becomes a delivery option.

## Example 1: a banner on the next page view

A banner is shown at the top of the next page a member views, until they dismiss it or it expires. It reaches members
who are offline too: they see it next time they visit.

The package includes `IMemberMessageInbox` for exactly this: an in-memory store of messages waiting for each member.
You need three pieces: a sender that adds to the inbox, a view component that shows unread messages, and an endpoint
to dismiss them.

**The sender**

```csharp
using Umbricians.LiveView.Messaging.Members;

public class BannerMemberMessageSender(IMemberMessageInbox inbox) : IMemberMessageSender
{
    public string Alias => "banner";
    public string Name => "Banner on next page";
    public string? Description => "Shown at the top of the next page each member views, until dismissed or it expires.";
    public bool ReachesOfflineMembers => true;

    public Task<MemberMessageResult> SendAsync(MemberMessage message, CancellationToken cancellationToken)
    {
        inbox.Add(message);
        int? queuedFor = message.Audience == MemberMessageAudience.AllMembers ? null : message.MemberKeys.Count;
        return Task.FromResult(new MemberMessageResult(queuedFor, "queued for their next page view"));
    }
}
```

Register it with `builder.AddMemberMessageSender<BannerMemberMessageSender>();`.

**The view component**, which reads the current member's unread messages:

```csharp
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Security;
using Umbricians.LiveView.Messaging.Members;

public class MemberMessagesViewComponent(IMemberManager memberManager, IMemberMessageInbox inbox) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        MemberIdentityUser? member = await memberManager.GetCurrentMemberAsync();
        IReadOnlyList<MemberMessage> messages = member is null ? [] : inbox.GetUnread(member.Key);
        return View(messages);
    }
}
```

**Its view**, `Views/Shared/Components/MemberMessages/Default.cshtml`:

```cshtml
@model IReadOnlyList<Umbricians.LiveView.Messaging.Members.MemberMessage>

@foreach (var message in Model)
{
    <div class="member-message member-message--@message.Type.ToString().ToLowerInvariant()" role="status">
        <strong>@message.Headline</strong>
        @if (!string.IsNullOrEmpty(message.Body))
        {
            <p>@message.Body</p>
        }
        <form method="post" action="/member-messages/dismiss/@message.Key">
            @Html.AntiForgeryToken()
            <input type="hidden" name="returnUrl" value="@(Context.Request.Path + Context.Request.QueryString)" />
            <button type="submit">Dismiss</button>
        </form>
    </div>
}
```

Razor's `@` encodes the headline and body, so whatever is typed on the dashboard is shown as text.

**The dismiss endpoint**

```csharp
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Security;
using Umbricians.LiveView.Messaging.Members;

[Route("member-messages")]
public class MemberMessagesController(IMemberManager memberManager, IMemberMessageInbox inbox) : Controller
{
    [HttpPost("dismiss/{messageKey:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dismiss(Guid messageKey, string? returnUrl)
    {
        if (await memberManager.GetCurrentMemberAsync() is { } member)
        {
            inbox.MarkRead(member.Key, messageKey);
        }

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
```

**Show it**: add this to your layout (for example `Views/Shared/_Layout.cshtml`) where banners should appear, then
style `.member-message` and its `--information`, `--warning`, `--urgent` and `--goodnews` variants:

```cshtml
@await Component.InvokeAsync("MemberMessages")
```

> If your pages are output-cached or served from a CDN, keep the banner out of the cached HTML: render it on pages
> that are not cached, or load it with a small script from an uncached endpoint.

## Example 2: a live pop-up with SignalR

A live pop-up appears straight away on any page a member has open. It only reaches members who are online at that
moment, so it pairs well with a banner for everyone else.

**The hub** puts each member's connections in a group, so a message can be sent to particular members:

```csharp
using Microsoft.AspNetCore.SignalR;
using Umbraco.Cms.Core.Security;

public class MemberMessagesHub(IMemberManager memberManager) : Hub
{
    public const string Path = "/hubs/member-messages";
    public const string AllMembersGroup = "members";

    public static string GroupFor(Guid memberKey) => $"member:{memberKey}";

    public override async Task OnConnectedAsync()
    {
        // Anonymous visitors can connect but join no group, so they never receive member messages.
        if (await memberManager.GetCurrentMemberAsync() is { } member)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(member.Key));
            await Groups.AddToGroupAsync(Context.ConnectionId, AllMembersGroup);
        }

        await base.OnConnectedAsync();
    }
}
```

**The sender**

```csharp
using Microsoft.AspNetCore.SignalR;
using Umbricians.LiveView.Messaging.Members;

public class SignalRMemberMessageSender(IHubContext<MemberMessagesHub> hub) : IMemberMessageSender
{
    public string Alias => "signalr";
    public string Name => "Live pop-up";
    public string? Description => "Pops up straight away on any page members have open.";

    public async Task<MemberMessageResult> SendAsync(MemberMessage message, CancellationToken cancellationToken)
    {
        var payload = new { message.Key, message.Headline, message.Body, Type = message.Type.ToString(), message.SenderName };

        if (message.Audience == MemberMessageAudience.AllMembers)
        {
            await hub.Clients.Group(MemberMessagesHub.AllMembersGroup).SendAsync("message", payload, cancellationToken);
            return new MemberMessageResult(null, "every member with a page open");
        }

        await hub.Clients
            .Groups(message.MemberKeys.Select(MemberMessagesHub.GroupFor).ToList())
            .SendAsync("message", payload, cancellationToken);
        return new MemberMessageResult(message.MemberKeys.Count, "if they have a page open");
    }
}
```

**Register** the sender and SignalR in a composer:

```csharp
builder.Services.AddSignalR();
builder.AddMemberMessageSender<SignalRMemberMessageSender>();
```

**Map the hub** in `Program.cs`, inside `WithEndpoints`:

```csharp
.WithEndpoints(u =>
{
    u.UseBackOfficeEndpoints();
    u.UseWebsiteEndpoints();
    u.EndpointRouteBuilder.MapHub<MemberMessagesHub>(MemberMessagesHub.Path);
});
```

**Listen on your pages**, for logged-in members, with the
[SignalR JavaScript client](https://learn.microsoft.com/aspnet/core/signalr/javascript-client). Set text with
`textContent`, never as HTML:

```html
<div id="member-toasts" aria-live="polite"></div>
<script src="https://cdn.jsdelivr.net/npm/@microsoft/signalr@8.0.7/dist/browser/signalr.min.js"></script>
<script>
  const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/member-messages")
    .withAutomaticReconnect()
    .build();

  connection.on("message", (message) => {
    const toast = document.createElement("div");
    toast.className = `member-toast member-toast--${message.type.toLowerCase()}`;
    const headline = document.createElement("strong");
    headline.textContent = message.headline;
    const body = document.createElement("p");
    body.textContent = message.body;
    toast.append(headline, body);
    document.getElementById("member-toasts").append(toast);
    setTimeout(() => toast.remove(), 15000);
  });

  connection.start();
</script>
```

> On a load-balanced site, add a SignalR backplane (for example Azure SignalR Service or Redis) so a message sent from
> one server reaches members connected to the others.

## Example 3: email every member

Email reaches everyone, including members who have not visited for months, so it suits **Every member** messages. It
uses Umbraco's own email sending, configured under `Umbraco:CMS:Global:Smtp`.

Sending to thousands of members takes longer than the 30 seconds a sender is given, so this sender queues the work
on Umbraco's background task queue and returns straight away.

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.HostedServices;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Email;
using Umbraco.Cms.Core.Services;
using Umbricians.LiveView.Messaging.Members;

public class EmailMemberMessageSender(
    IEmailSender emailSender,
    IMemberService memberService,
    IOptions<GlobalSettings> globalSettings,
    IBackgroundTaskQueue backgroundTasks,
    ILogger<EmailMemberMessageSender> logger) : IMemberMessageSender
{
    private const int BatchSize = 500;

    public string Alias => "email";
    public string Name => "Email";
    public string? Description => "Emails the message to each member. Sent in the background.";
    public bool ReachesOfflineMembers => true;

    public Task<MemberMessageResult> SendAsync(MemberMessage message, CancellationToken cancellationToken)
    {
        if (!emailSender.CanSendRequiredEmail())
        {
            return Task.FromResult(new MemberMessageResult(0, "email is not configured"));
        }

        backgroundTasks.QueueBackgroundWorkItem(token => SendEmailsAsync(message, token));

        int? queuedFor = message.Audience == MemberMessageAudience.AllMembers ? null : message.MemberKeys.Count;
        return Task.FromResult(new MemberMessageResult(queuedFor, "queued for sending"));
    }

    private async Task SendEmailsAsync(MemberMessage message, CancellationToken cancellationToken)
    {
        string from = globalSettings.Value.Smtp?.From ?? "noreply@example.com";
        int sent = 0;

        await foreach (IMember member in GetRecipientsAsync(message).WithCancellation(cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(member.Email))
            {
                continue;
            }

            try
            {
                await emailSender.SendAsync(
                    new EmailMessage(from, member.Email, message.Headline, message.Body, isBodyHtml: false),
                    emailType: "LiveViewMemberMessage",
                    enableNotification: false,
                    expires: null);
                sent++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not email member message {MessageKey} to member {MemberKey}", message.Key, member.Key);
            }
        }

        logger.LogInformation("Emailed member message {MessageKey} to {Sent} members", message.Key, sent);
    }

    // MemberKeys lists the recipients, except for "every member", where the sender pages through all members itself.
    private async IAsyncEnumerable<IMember> GetRecipientsAsync(MemberMessage message)
    {
        if (message.Audience != MemberMessageAudience.AllMembers)
        {
            foreach (Guid[] batch in message.MemberKeys.Chunk(BatchSize))
            {
                foreach (IMember member in await memberService.GetByKeysAsync(batch))
                {
                    yield return member;
                }
            }

            yield break;
        }

        long pageIndex = 0;
        long total;
        do
        {
            foreach (IMember member in memberService.GetAll(pageIndex++, BatchSize, out total))
            {
                yield return member;
            }
        }
        while (pageIndex * BatchSize < total);
    }
}
```

Before emailing every member, check you have their consent. A common approach is a "receive site notices" property
on the member type, checked in `GetRecipientsAsync` before yielding a member. Queued email is held in memory, so
emails not yet sent when the site restarts are not sent; for large or important mailings, hand the list to your email
platform instead (see below).

## More ideas

Anything that can reach a member can be a sender. Some ideas:

- **Push notifications** to your mobile app or the browser (Web Push, Firebase Cloud Messaging, OneSignal).
- **SMS** through Twilio, Vonage or similar, for urgent messages.
- **Your email or marketing platform** (Mailchimp, Brevo, SendGrid and so on): create a campaign or send to a segment,
  rather than emailing one by one from the site. Better for large lists, unsubscribes and consent.
- **An in-site notification centre** that stores messages in your own database, so they survive restarts and
  members can read them later. Implement your own inbox rather than using the in-memory one.
- **A CRM or customer-service tool** (HubSpot, Salesforce, Zendesk), so the message appears in each member's record.
- **Your mobile app's own channel**, if members mostly use the app.

## Reference

### `IMemberMessageSender`

| Member | Purpose |
|---|---|
| `Alias` | Unique, stable key. Sent from the dashboard to say which senders to use. |
| `Name` | The delivery option shown on the dashboard. |
| `Description` | Optional explanation shown under the option. |
| `ReachesOfflineMembers` | `true` if it can reach members who are not logged in (a banner, email). When it is `false`, the dashboard warns, for "Every member" messages, that it only reaches members online now. Defaults to `false`. |
| `SendAsync` | Delivers the message. Has 30 seconds; an exception or timeout is reported on the dashboard and does not stop other senders. |

Register senders with `builder.AddMemberMessageSender<T>()`. They are singletons, so inject scoped services through
`IServiceScopeFactory`.

### `MemberMessage`

| Property | Meaning |
|---|---|
| `Key` | Unique id for the message; the same for every sender. Use it to avoid sending twice. |
| `Headline` | Up to 200 characters. Always present. |
| `Body` | Plain text, up to 2000 characters. May be empty. Encode it wherever you display it. |
| `Type` | `Information`, `Warning`, `Urgent` or `GoodNews`, chosen on the dashboard. Map it to your own styling. |
| `Audience` | Who the message is for; see below. |
| `MemberKeys` | The recipients' member keys, for `Selected` and `AllOnline`. Empty for `AllMembers`. |
| `SenderName` | The backoffice user who sent it. |
| `SentAt` | When it was sent. |
| `ExpiresAt` | When it stops being relevant (15 minutes to 7 days, chosen on the dashboard). Don't show it after this. |

### `MemberMessageAudience`

| Audience | Dashboard option | `MemberKeys` |
|---|---|---|
| `Selected` | **Selected members**: the members ticked in the table | those members |
| `AllOnline` | **Everyone online**: everyone logged in at the moment of sending, on every page of the table | those members |
| `AllMembers` | **Every member**: all members of the site, including those not logged in | empty: the audience may be very large, so reach them in whatever way suits the sender |

### `MemberMessageResult`

Return `new MemberMessageResult(delivered, note)`. `delivered` is how many members it was sent or queued for, or
`null` if the sender does not know (for example "every member"). `note` is a few words shown next to it on the
dashboard, such as "queued for sending".

### `IMemberMessageInbox`

The package's optional in-memory store for messages waiting to be shown.

| Method | Purpose |
|---|---|
| `Add(message)` | Queue a message for its recipients (or for every member). |
| `GetUnread(memberKey)` | Unexpired messages for that member that they have not marked read, oldest first. |
| `MarkRead(memberKey, messageKey)` | Hide a message for that member, for example when they dismiss it. |

Messages are kept until they expire, and at most the 200 most recent. The inbox is cleared when the site restarts and
is separate on each server of a load-balanced site. If that matters, register your own `IMemberMessageInbox` backed
by a database; the package uses yours instead.

## Good practice

- **Treat the message as plain text.** It is typed in the backoffice but shown to the public. Use Razor's `@`,
  `textContent` in JavaScript, or plain-text email bodies.
- **Stay within 30 seconds.** Anything slower (large email sends, calls to slow services) should be queued in the
  background, as in the email example.
- **Respect `ExpiresAt`.** A "restarting at 18:00" banner is unhelpful the next morning.
- **Respect consent and preferences**, especially for email, SMS and push notifications.
- **Use `Key` to avoid duplicates** if your sender retries or hands work to another system.
- **Think about scale.** For "every member" on a large site, page through members as the email example does, or hand
  off to a platform built for bulk sending.

## Try it in the demo site

The repository's demo site has working banner and SignalR senders. Run it, then:

1. Open `/demo/members` in a browser, log in as a test member and leave the page open.
2. In another browser or a private window, sign in to the backoffice and open **Settings → Live View**.
3. Click **Select all N online** on the members table, then **Message members…**, and send.
4. The live pop-up appears on the member's page straight away; reload it to see the banner.

The demo's code is in
[`demo/Umbricians.LiveView.Demo/DemoMembers`](../demo/Umbricians.LiveView.Demo/DemoMembers). How to run the demo is
in the [README](../README.md#development).
