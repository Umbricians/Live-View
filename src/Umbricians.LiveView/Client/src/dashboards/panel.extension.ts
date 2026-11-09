import type { ManifestElement, ManifestWithDynamicConditions } from "@umbraco-cms/backoffice/extension-api";
import type { LIVE_VIEW_PANEL_TYPE } from "./constants.js";

/**
 * A panel on the Live View dashboard: a box shown below the header, ordered by weight (highest first).
 * Built-in weights: Live activity 300, Backoffice users online 200, Members online 100.
 */
export interface ManifestLiveViewPanel
  extends ManifestElement,
    ManifestWithDynamicConditions<UmbExtensionConditionConfig> {
  type: typeof LIVE_VIEW_PANEL_TYPE;
}

declare global {
  interface UmbExtensionManifestMap {
    umbriciansLiveViewPanel: ManifestLiveViewPanel;
  }
}
