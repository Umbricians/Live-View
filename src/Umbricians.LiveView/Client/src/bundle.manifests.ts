import { manifests as entrypoints } from "./entrypoints/manifest.js";
import { manifests as dashboards } from "./dashboards/manifest.js";
import { manifests as modals } from "./modals/manifest.js";

// Loaded from umbraco-package.json; collates every manifest in the package.
export const manifests: Array<UmbExtensionManifest> = [...entrypoints, ...dashboards, ...modals];
