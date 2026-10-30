import { LIVE_VIEW_PERMISSION_ENTITY_TYPE } from "./constants.js";

// The user group editor heads each permission box with the term user_permissionsEntityGroup_<entity type>.
export default {
  user: {
    [`permissionsEntityGroup_${LIVE_VIEW_PERMISSION_ENTITY_TYPE}`]: "Live View",
  },
};
