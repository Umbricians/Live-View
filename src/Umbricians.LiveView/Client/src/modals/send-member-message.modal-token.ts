import { UmbModalToken } from "@umbraco-cms/backoffice/modal";
import type { MemberMessageSendResult, MemberMessageSender } from "../api/live-view.resource.js";
import type { MemberSelection } from "../dashboards/live-view-members.element.js";

export interface UmbriciansSendMemberMessageModalData {
  /** The members picked in the table, if any. */
  selection: MemberSelection;
  onlineCount: number;
  totalMembers: number;
  senders: Array<MemberMessageSender>;
}

export type UmbriciansSendMemberMessageModalValue = MemberMessageSendResult;

export const UMBRICIANS_SEND_MEMBER_MESSAGE_MODAL = new UmbModalToken<
  UmbriciansSendMemberMessageModalData,
  UmbriciansSendMemberMessageModalValue
>("Umbricians.LiveView.Modal.SendMemberMessage", {
  modal: { type: "sidebar", size: "small" },
});
