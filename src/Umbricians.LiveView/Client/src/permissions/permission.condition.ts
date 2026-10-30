import { UmbConditionBase } from "@umbraco-cms/backoffice/extension-registry";
import type { UmbConditionConfigBase, UmbConditionControllerArguments } from "@umbraco-cms/backoffice/extension-api";
import type { UmbControllerHost } from "@umbraco-cms/backoffice/controller-api";
import { UMB_CURRENT_USER_CONTEXT } from "@umbraco-cms/backoffice/current-user";
import { LIVE_VIEW_PERMISSION_CONDITION_ALIAS } from "./constants.js";

export interface LiveViewPermissionConditionConfig
  extends UmbConditionConfigBase<typeof LIVE_VIEW_PERMISSION_CONDITION_ALIAS> {
  /** One of the LIVE_VIEW_PERMISSION_* verbs. */
  permission: string;
}

/**
 * Permits an extension when one of the current user's groups grants the given Live View permission.
 */
export class LiveViewPermissionCondition extends UmbConditionBase<LiveViewPermissionConditionConfig> {
  constructor(host: UmbControllerHost, args: UmbConditionControllerArguments<LiveViewPermissionConditionConfig>) {
    super(host, args);
    this.consumeContext(UMB_CURRENT_USER_CONTEXT, (context) => {
      this.observe(context?.fallbackPermissions, (permissions) => {
        this.permitted = permissions?.includes(this.config.permission) ?? false;
      });
    });
  }
}

export { LiveViewPermissionCondition as api };

declare global {
  interface UmbExtensionConditionConfigMap {
    umbriciansLiveViewPermission: LiveViewPermissionConditionConfig;
  }
}
