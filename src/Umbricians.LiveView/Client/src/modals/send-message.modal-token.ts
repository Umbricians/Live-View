import { UmbModalToken } from "@umbraco-cms/backoffice/modal";

export interface UmbriciansSendMessageModalData {
  /** Send to everyone online; `count` is how many that is right now. */
  allOnline: boolean;
  count: number;
  /** The picked users, when not sending to everyone online. */
  recipients: Array<{ key: string; name: string }>;
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
