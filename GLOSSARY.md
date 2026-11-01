# Glossary

The words this codebase uses for its domain. Code, docs and reviews should use them as defined here.

## Backoffice messenger

The module that sends a one-off message to backoffice users. It owns the send rules: the **message content**, who
**everyone online** means at the moment of sending (users in the backoffice; an MCP caller only on MCP can't see a
backoffice message), and that the **sender** is never one of the recipients. It also says how many users a message to
everyone online would reach, so the users table's count and the delivery always agree.

## Backoffice presence

The module that knows which backoffice users are online. It is told when a user is **seen** (made a backoffice
request), when each of their backoffice tabs sends a **heartbeat** or closes, and when they are an **MCP caller**, and
it answers who is online: one page at a time, or all at once. It is the only source for both the backoffice
users line on the chart and the backoffice users table, so the two always agree. Users are identified by their
`Guid` key only.

## Channel

One line on the Live View chart: a named, coloured count of active sessions (`web`, `mcp`, `api`, `backoffice`,
`members`, or one a site adds). Request-based channels are fed by the tracking middleware, as the **request
classifier** decides; others are fed from code.

## Channel settings

A channel's settings as they apply, with every value filled in: its name, sort order, paths, session key sources,
activity window and so on. Appsettings beat channels declared in code, which beat the built-in defaults; whatever is
still empty then gets the package defaults (counted by client, a window of `ActivityWindowSeconds`). Everything that
reads a channel's settings reads them here.

## MCP caller

A backoffice user or member using an MCP endpoint. Requests to an MCP endpoint the site hosts are matched to the
signed-in person, who is looked up by key as a backoffice user first, then as a member; an MCP server elsewhere
reports its callers through `ILiveViewPresence`. An MCP caller is online while any of their MCP requests is open, such
as a stream, and for the `mcp` channel's activity window after.

## Member

A website visitor with a member account who has signed in on the front end. Not a backoffice user.

## Member key

The member's `Guid` key. The only way Member presence identifies a member. A member's numeric id is converted to
their key once, inside Member presence, and never leaves it.

## Member messaging

The module that sends a message to members through the site's member message senders. It owns the send rules: the
**message content**, the audience (selected members, **everyone online** at the moment of sending, or every member),
which senders deliver it, and when it stops being shown.

## Member presence

The module that knows which members are online. It is told when a member is **seen** (made a front-end request),
when they **sign out**, and when they are an **MCP caller**, and it answers who is online: one page at a time, or
all at once. It is the only source for both the members line on the chart and the members table, so the two
always agree.

## Message content

What every message says, to backoffice users or members alike: a headline (required, up to 200 characters), a
message (up to 2000) and its type (Information, Warning, Urgent or Good news). Text is trimmed before it is checked.
Both messaging modules check it the same way, and both dialogs share the same fields for it.

## Online backoffice user

A backoffice user with an open backoffice tab, seen within the backoffice channel's activity window, or online as an
**MCP caller**, whose account still exists and is not disabled. A tab is open while it keeps sending heartbeats.
Online backoffice users are listed like online members: one row each, saying how they are online when anyone is on
MCP.

## Online list

What the backoffice users table and the members table both are: one page of people online, refreshed with the chart
and paused with it, and a **selection** of who to message. The selection survives paging; **all online** covers people
on pages never loaded, and unticking someone while it is on picks the rest of their page instead. Each table only says
which of its people can be picked and how a row looks.

## Online member

A member seen on the website within the members channel's activity window who has not signed out since, or online
as an **MCP caller**, and whose account still exists. Each has one row, however they are online; while anyone is on
MCP, each row is tagged with how its member is online (Website, MCP) and when they were last seen that way. Rows are
listed in **arrival** order (when they came online), so they don't jump between pages. Signing out of the website
leaves a member online on MCP.

## Request classifier

The module that decides which **channels** a request counts in, under what session key, and who it shows was seen:
a member (for **Member presence**), a backoffice user (for **Backoffice presence**) or an **MCP caller**. It owns
every rule about which requests count: path prefixes and exclusions, sort order and exclusive channels, static
files, bots, and the session key sources. The tracking middleware only acts on what it decides.

## Signed-out marker

A short-lived note Member presence keeps when a member signs out, so the sign-out request itself, which still
carries the member's identity, does not mark them online again.
