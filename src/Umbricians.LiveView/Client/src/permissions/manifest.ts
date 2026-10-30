import {
  LIVE_VIEW_PERMISSION_CONDITION_ALIAS,
  LIVE_VIEW_PERMISSION_ENTITY_TYPE,
  LIVE_VIEW_PERMISSION_MESSAGE_MEMBERS,
  LIVE_VIEW_PERMISSION_MESSAGE_USERS,
  LIVE_VIEW_PERMISSION_VIEW,
} from "./constants.js";

export const manifests: Array<UmbExtensionManifest> = [
  // Shown in Users → User groups → (group) → Permissions, in a "Live View" box.
  {
    type: "entityUserPermission",
    alias: "Umbricians.LiveView.UserPermission.View",
    name: "Live View: View Live View",
    weight: 300,
    forEntityTypes: [LIVE_VIEW_PERMISSION_ENTITY_TYPE],
    meta: {
      verbs: [LIVE_VIEW_PERMISSION_VIEW],
      label: "View Live View",
      description: "See the Live View dashboard in Settings: live activity and who is online.",
    },
  },
  {
    type: "entityUserPermission",
    alias: "Umbricians.LiveView.UserPermission.MessageUsers",
    name: "Live View: Message backoffice users",
    weight: 200,
    forEntityTypes: [LIVE_VIEW_PERMISSION_ENTITY_TYPE],
    meta: {
      verbs: [LIVE_VIEW_PERMISSION_MESSAGE_USERS],
      label: "Message backoffice users",
      description: "Send notifications to backoffice users who are online. Also needs View Live View.",
    },
  },
  {
    type: "entityUserPermission",
    alias: "Umbricians.LiveView.UserPermission.MessageMembers",
    name: "Live View: Message members",
    weight: 100,
    forEntityTypes: [LIVE_VIEW_PERMISSION_ENTITY_TYPE],
    meta: {
      verbs: [LIVE_VIEW_PERMISSION_MESSAGE_MEMBERS],
      label: "Message members",
      description:
        "Send messages to website members. Also needs View Live View, and a delivery method set up by a developer.",
    },
  },
  {
    type: "condition",
    alias: LIVE_VIEW_PERMISSION_CONDITION_ALIAS,
    name: "Live View Permission Condition",
    api: () => import("./permission.condition.js"),
  },
  {
    type: "localization",
    alias: "Umbricians.LiveView.Localization.En",
    name: "Live View English",
    meta: { culture: "en" },
    js: () => import("./localization.en.js"),
  },
];
