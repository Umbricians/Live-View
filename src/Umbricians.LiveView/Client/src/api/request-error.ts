import type { UmbLocalizationController } from "@umbraco-cms/backoffice/localization-api";

/** A failed Live View call: the HTTP status (undefined when there was no response) and the server's own explanation. */
export class LiveViewRequestError extends Error {
  status: number | undefined;
  detail: string | undefined;

  constructor(status: number | undefined, detail: string | undefined) {
    // The message is for logs; the dashboard words the failure for the user from the status and detail.
    super(detail || `Live View request failed (${status ?? "no response"})`);
    this.status = status;
    this.detail = detail;
  }
}

/** Why a request failed: the server's own explanation when it gave one, otherwise the status. */
export function describeError(localize: UmbLocalizationController, error: unknown) {
  if (error instanceof LiveViewRequestError && error.detail) return error.detail;
  return localize.term("umbriciansLiveView_requestFailed", error instanceof LiveViewRequestError ? error.status : undefined);
}
