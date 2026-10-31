# Glossary

The words this codebase uses for its domain. Code, docs and reviews should use them as defined here.

## Backoffice presence

The module that knows which backoffice users are online. It is told when a user is **seen** (made a backoffice
request) and when each of their backoffice tabs sends a **heartbeat** or closes, and it answers who is online: one
page at a time, or all at once. It is the only source for both the backoffice users line on the chart and the
backoffice users table, so the two always agree. Users are identified by their `Guid` key only.

## Backoffice messenger

The module that sends a one-off message to backoffice users. It owns the send rules: the headline and message
limits, the colours, who **everyone online** means at the moment of sending, and that the **sender** is never one of
the recipients. It also says how many users a message to everyone online would reach, so the users table's count and
the delivery always agree.

## Channel

One line on the Live View chart: a named, coloured count of active sessions (`web`, `mcp`, `api`, `backoffice`,
`members`, or one a site adds). Request-based channels are fed by the tracking middleware; others are fed from code.

## Member

A website visitor with a member account who has signed in on the front end. Not a backoffice user.

## Member key

The member's `Guid` key. The only way Member presence identifies a member. A member's numeric id is converted to
their key once, inside Member presence, and never leaves it.

## Member presence

The module that knows which members are online. It is told when a member is **seen** (made a front-end request)
and when they **sign out**, and it answers who is online: one page at a time, or all at once. It is the only source
for both the members line on the chart and the members table, so the two always agree.

## Online backoffice user

A backoffice user with an open backoffice tab, or seen within the backoffice channel's activity window, whose account
still exists and is not disabled. A tab is open while it keeps sending heartbeats. Online backoffice users are listed
in **arrival** order, like online members.

## Online member

A member seen within the members channel's activity window who has not signed out since, and whose account still
exists. Online members are listed in **arrival** order (when they came online), so rows don't jump between pages.

## Signed-out marker

A short-lived note Member presence keeps when a member signs out, so the sign-out request itself, which still
carries the member's identity, does not mark them online again.
