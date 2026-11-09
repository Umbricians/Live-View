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
      label: "#umbriciansLiveView_permissionView",
      description: "#umbriciansLiveView_permissionViewDescription",
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
      label: "#umbriciansLiveView_permissionMessageUsers",
      description: "#umbriciansLiveView_permissionMessageUsersDescription",
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
      label: "#umbriciansLiveView_permissionMessageMembers",
      description: "#umbriciansLiveView_permissionMessageMembersDescription",
    },
  },
  {
    type: "condition",
    alias: LIVE_VIEW_PERMISSION_CONDITION_ALIAS,
    name: "Live View Permission Condition",
    api: () => import("./permission.condition.js"),
  },
];
