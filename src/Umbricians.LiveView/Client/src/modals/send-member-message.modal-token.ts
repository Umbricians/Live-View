import { UmbModalToken } from "@umbraco-cms/backoffice/modal";
import type { MemberMessageSendResult, MemberMessageSender } from "../api/live-view.resource.js";
import type { SelectionSummary } from "../dashboards/online-selection.js";

export interface UmbriciansSendMemberMessageModalData {
  /** The members picked in the table, if any. */
  selection: SelectionSummary;
  onlineCount: number;
  totalMembers: number;
  senders: Array<MemberMessageSender>;
  /** The IANA time zone "midnight tonight" is worked out in, or null for the sender's own. */
  timeZone: string | null;
}

export type UmbriciansSendMemberMessageModalValue = MemberMessageSendResult;

export const UMBRICIANS_SEND_MEMBER_MESSAGE_MODAL = new UmbModalToken<
  UmbriciansSendMemberMessageModalData,
  UmbriciansSendMemberMessageModalValue
>("Umbricians.LiveView.Modal.SendMemberMessage", {
  modal: { type: "sidebar", size: "small" },
});
