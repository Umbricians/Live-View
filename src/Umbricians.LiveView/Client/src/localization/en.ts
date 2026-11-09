import type { UmbLocalizationDictionary } from "@umbraco-cms/backoffice/localization-api";

// Every term is used as umbriciansLiveView_<key>. Write each key out in full where it is used, never built from
// parts: the test that checks every key has a term and every term is used finds them by searching for the literal.
// A term with a count is a function of that count, so a language can use its own plural forms.
export default {
  user: {
    // The user group editor heads each permission box with user_permissionsEntityGroup_<entity type>; must match
    // LIVE_VIEW_PERMISSION_ENTITY_TYPE.
    "permissionsEntityGroup_umbricians-live-view": "Live View",
  },
  umbriciansLiveView: {
    // Dashboard
    dashboardName: "Live View",
    refreshInterval: "Refresh interval",
    everySeconds: (seconds: number) => `Every ${seconds}s`,
    pause: "Pause",
    resume: "Resume",
    clearChart: "Clear chart",
    updatedAt: (time: string) => `Updated ${time}`,
    connecting: "Connecting…",
    requestFailed: (status?: number) => `Live View request failed (${status ?? "no response"})`,
    liveActivity: "Live activity",
    showLine: (name: string) => `Show ${name} on the chart`,
    hideLine: (name: string) => `Hide ${name} on the chart`,
    snapshotNote: (points: number) =>
      `A live snapshot only: nothing is stored, and the chart keeps the last ${points} readings while this page is open.`,
    selectUsersOrMembers: "Select users or members to message them",
    selectUsers: "Select users to send them a message",
    selectMembers: "Select members to send them a message",
    usersSelected: (count: number) => `${count} ${count === 1 ? "user" : "users"} selected`,
    membersSelected: (count: number) => `${count} ${count === 1 ? "member" : "members"} selected`,
    messageUsersButton: "Message users…",
    messageMembersButton: "Message members…",
    messageSent: "Message sent",
    sentToUsers: (count: number) => `Sent to ${count} ${count === 1 ? "user" : "users"}.`,
    messageSentToMembers: "Message sent to members",
    messagePartlySent: "Message partly sent",
    senderSent: (sender: string) => `${sender}: sent`,
    senderDelivered: (sender: string, count: number) => `${sender}: ${count} ${count === 1 ? "member" : "members"}`,
    senderFailed: (sender: string, error: string) => `${sender}: ${error.toLowerCase()}`,
    senderNote: (text: string, note: string) => `${text} (${note})`,

    // Online tables
    backofficeUsersOnline: "Backoffice users online",
    membersOnline: "Members online",
    onlineCount: (count: number) => `${count} online`,
    nobodyInBackoffice: "Nobody has the backoffice open right now.",
    noMembersOnline: "No members are online right now.",
    openTabs: "Open tabs",
    lastActive: "Last active",
    delivery: "Delivery",
    memberType: "Member type",
    you: "You",
    youTitle: "This is you, so you are left out of messages",
    viaBackoffice: "Backoffice",
    viaWebsite: "Website",
    viaMcp: "MCP",
    onlyOnMcp: "Only on MCP",
    backofficeTabsOpen: (count: number) => `${count} ${count === 1 ? "backoffice tab" : "backoffice tabs"} open`,
    instant: "Instant",
    instantTitle: "Receives messages instantly",
    withinMinute: "Within 1 min",
    withinMinuteTitle: "Receives messages with its next heartbeat",
    justNow: "Just now",
    selectAllOnline: (count: number) => `Select all ${count} online`,
    allOnlineUsersSelected: (count: number) =>
      `All ${count} ${count === 1 ? "online user is" : "online users are"} selected.`,
    allOnlineMembersSelected: (count: number) =>
      `All ${count} ${count === 1 ? "online member is" : "online members are"} selected.`,
    allUsersOnPageSelected: (count: number) => `All ${count} ${count === 1 ? "user" : "users"} on this page selected.`,
    allMembersOnPageSelected: (count: number) =>
      `All ${count} ${count === 1 ? "member" : "members"} on this page selected.`,
    selectPage: "Select everyone on this page",
    selectPerson: (name: string) => `Select ${name}`,
    backofficeUsersPages: "Backoffice users pages",
    membersPages: "Members pages",

    // Message fields
    headline: "Headline",
    type: "Type",
    message: "Message",
    typeInformation: "Information",
    typeWarning: "Warning",
    typeUrgent: "Urgent",
    typeGoodNews: "Good news",
    messageNotSent: "Message not sent",

    // User dialog
    sendMessage: "Send message",
    send: "Send",
    toEveryoneOnline: "To everyone online",
    toUsers: (count: number) => `To ${count} ${count === 1 ? "user" : "users"}`,
    everyoneOnlineUsersNote: (count: number) =>
      `Everyone else with the backoffice open when you send (${count} ${count === 1 ? "user" : "users"} right now), including anyone who has signed in since you opened this.`,
    backofficeMessageNote:
      "Shown in their backoffice as a notification that stays on screen until dismissed, signed with your name.",
    userHeadlinePlaceholder: "e.g. Upgrade in 10 minutes",
    userMessagePlaceholder: "Please save your work and log out. The site will be unavailable for about 15 minutes.",

    // Member dialog
    messageMembers: "Message members",
    sendTo: "Send to",
    selectedMembers: (count: number) => `Selected members (${count})`,
    selectedMembersNoneHint: "Tick members in the table to send to just them.",
    selectedMembersHint: "The members ticked in the table.",
    everyoneOnline: (count: number) => `Everyone online (${count})`,
    everyoneOnlineHint: "Every member logged in when you send, on every page of the table.",
    everyMember: (count: number) => `Every member (${count})`,
    everyMemberHint: "All members of the site, including those not logged in right now.",
    deliverBy: "Deliver by",
    onlineOnlySender: "Only reaches members who are online now.",
    memberHeadlinePlaceholder: "e.g. Scheduled maintenance at 18:00",
    memberMessagePlaceholder:
      "The site will be unavailable for about 15 minutes. Please save anything you are working on.",
    relevantFor: "Relevant for",
    relevantForDescription: "After this, banners and other queued messages are no longer shown.",
    lifetimeMidnight: "Midnight tonight",
    lifetimeCustom: "Custom…",
    hours: "Hours",
    minutes: "Minutes",
    hoursUnit: "hours",
    minutesUnit: "minutes",
    lifetimeOutOfRange: (days: number) => `Enter between 1 minute and ${days} days.`,
    stopsBeingShown: (at: string, zone: string, duration: string) =>
      `Stops being shown ${at} ${zone} (in ${duration}).`,
    zoneTime: (city: string) => `${city} time`,
    yourTime: "your time",
    sendToEveryMember: "Send to every member",
    sendToMembers: (count: number) => `Send to ${count} ${count === 1 ? "member" : "members"}`,

    // Permissions
    permissionView: "View Live View",
    permissionViewDescription: "See the Live View dashboard in Settings: live activity and who is online.",
    permissionMessageUsers: "Message backoffice users",
    permissionMessageUsersDescription:
      "Send notifications to backoffice users who are online. Also needs View Live View.",
    permissionMessageMembers: "Message members",
    permissionMessageMembersDescription:
      "Send messages to website members. Also needs View Live View, and a delivery method set up by a developer.",
  },
} satisfies UmbLocalizationDictionary;
