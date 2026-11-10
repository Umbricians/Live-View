import { UmbModalToken } from "@umbraco-cms/backoffice/modal";
import type { SelectionSummary } from "../dashboards/online-selection.js";

export interface UmbriciansSendMessageModalData {
  /** Who is picked in the users table. */
  selection: SelectionSummary;
  /** How many users a message to everyone online reaches right now. */
  onlineCount: number;
}

export interface UmbriciansSendMessageModalValue {
  sentTo: number;
}

export const UMBRICIANS_SEND_MESSAGE_MODAL = new UmbModalToken<
  UmbriciansSendMessageModalData,
  UmbriciansSendMessageModalValue
>("Umbricians.LiveView.Modal.SendMessage", {
  modal: { type: "sidebar", size: "small" },
});
