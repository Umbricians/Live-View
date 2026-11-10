import { css, html, customElement, state, repeat, nothing } from "@umbraco-cms/backoffice/external/lit";
import { UmbModalBaseElement } from "@umbraco-cms/backoffice/modal";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import { UMB_CURRENT_USER_CONTEXT } from "@umbraco-cms/backoffice/current-user";
import "@umbraco-cms/backoffice/user-group";
import type { UmbUserGroupInputElement } from "@umbraco-cms/backoffice/user-group";
import type { UUIRadioGroupEvent } from "@umbraco-cms/backoffice/external/uui";
import { fetchUserGroupRecipients, sendMessage, type BackofficeAudience } from "../api/live-view.resource.js";
import { describeError } from "../api/request-error.js";
import { hasHeadline, newMessage, renderMessageFields, sendColor, trimmed } from "./message-fields.js";
import type {
  UmbriciansSendMessageModalData,
  UmbriciansSendMessageModalValue,
} from "./send-message.modal-token.js";

/** Umbraco's user group picker reads the groups through the Users section's API, so it needs access to that section. The server enforces the same rule. */
const USERS_SECTION = "Umb.Section.Users";

/**
 * Sidebar dialog for writing a notification to backoffice users: who it goes to (the users picked on the Live View
 * dashboard, everyone online, or everyone online in chosen user groups) and the message itself.
 */
@customElement("umbricians-live-view-send-message-modal")
export class UmbriciansLiveViewSendMessageModalElement extends UmbModalBaseElement<
  UmbriciansSendMessageModalData,
  UmbriciansSendMessageModalValue
> {
  @state() private _audience: BackofficeAudience = "allOnline";
  @state() private _userGroupKeys: Array<string> = [];
  /** How many users the chosen user groups reach; undefined while counting. */
  @state() private _userGroupCount?: number = 0;
  @state() private _userGroupError?: string;
  @state() private _canPickUserGroups = false;
  @state() private _text = newMessage();
  @state() private _sending = false;

  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;
  #countAbort?: AbortController;

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));
    this.consumeContext(UMB_CURRENT_USER_CONTEXT, (context) =>
      this.observe(context?.allowedSections, (sections) => (this._canPickUserGroups = sections?.includes(USERS_SECTION) ?? false)),
    );
  }

  override connectedCallback() {
    super.connectedCallback();
    // Start from what was picked in the table; with nothing picked, default to everyone online.
    const selection = this.data?.selection;
    this._audience = selection?.allOnline ? "allOnline" : selection && selection.count > 0 ? "selected" : "allOnline";
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#countAbort?.abort();
  }

  #recipientCount(audience: BackofficeAudience) {
    const data = this.data;
    if (!data) return 0;
    if (audience === "selected") return data.selection.allOnline ? 0 : data.selection.count;
    return audience === "allOnline" ? data.onlineCount : (this._userGroupCount ?? 0);
  }

  async #onUserGroupsChange(event: Event) {
    this._userGroupKeys = (event.target as UmbUserGroupInputElement).selection;
    this.#countAbort?.abort();
    this._userGroupError = undefined;

    if (this._userGroupKeys.length === 0) {
      this._userGroupCount = 0;
      return;
    }

    const abort = new AbortController();
    this.#countAbort = abort;
    this._userGroupCount = undefined;
    try {
      const { recipients } = await fetchUserGroupRecipients(this._userGroupKeys, abort.signal);
      this._userGroupCount = recipients;
    } catch (error) {
      if (abort.signal.aborted) return;
      this._userGroupCount = 0;
      this._userGroupError = describeError(this.localize, error);
    }
  }

  get #canSend() {
    return (
      !this._sending &&
      hasHeadline(this._text) &&
      this.#recipientCount(this._audience) > 0 &&
      (this._audience !== "userGroups" || (this._canPickUserGroups && !this._userGroupError))
    );
  }

  async #send(event?: Event) {
    event?.preventDefault();
    const selection = this.data?.selection;
    if (!this.#canSend || !selection) return;

    this._sending = true;
    try {
      const audience =
        this._audience === "selected"
          ? { userKeys: selection.people.map((p) => p.key) }
          : this._audience === "allOnline"
            ? { allOnline: true }
            : { userGroupKeys: this._userGroupKeys };
      const result = await sendMessage({ ...trimmed(this._text), ...audience });
      this.updateValue({ sentTo: result.recipients });
      this._submitModal();
    } catch (error) {
      this.#notifications?.peek("danger", {
        data: { headline: this.localize.term("umbriciansLiveView_messageNotSent"), message: describeError(this.localize, error) },
      });
    } finally {
      this._sending = false;
    }
  }

  #renderAudience(audience: BackofficeAudience, label: string, hint: string, disabled: boolean) {
    return html`<uui-radio
      value=${audience}
      label=${label}
      ?disabled=${disabled}
      .checked=${this._audience === audience}>
      <div class="option">
        <strong>${label}</strong>
        <span class="muted">${hint}</span>
      </div>
    </uui-radio>`;
  }

  #renderUserGroups() {
    const count = this._userGroupCount;
    return html`
      <umb-user-group-input
        .selection=${this._userGroupKeys}
        @change=${this.#onUserGroupsChange}></umb-user-group-input>
      ${this._userGroupKeys.length === 0
        ? nothing
        : this._userGroupError
          ? html`<p class="error">${this._userGroupError}</p>`
          : html`<p class="muted">
              ${count === undefined ? this.localize.term("umbriciansLiveView_countingUsers") : this.localize.term("umbriciansLiveView_reachesUsersOnline", count)}
            </p>`}
    `;
  }

  override render() {
    const data = this.data;
    if (!data) return nothing;
    const selectedCount = this.#recipientCount("selected");
    const onlineCount = this.#recipientCount("allOnline");
    const groupCount = this._userGroupCount;

    return html`
      <umb-body-layout headline=${this.localize.term("umbriciansLiveView_sendMessage")}>
        <uui-box headline=${this.localize.term("umbriciansLiveView_sendTo")}>
          <uui-radio-group
            label=${this.localize.term("umbriciansLiveView_sendTo")}
            .value=${this._audience}
            @change=${(e: UUIRadioGroupEvent) => (this._audience = e.target.value as BackofficeAudience)}>
            ${this.#renderAudience(
              "selected",
              this.localize.term("umbriciansLiveView_selectedUsers", selectedCount),
              selectedCount === 0
                ? this.localize.term("umbriciansLiveView_selectedUsersNoneHint")
                : this.localize.term("umbriciansLiveView_selectedUsersHint"),
              selectedCount === 0,
            )}
            ${this.#renderAudience(
              "allOnline",
              this.localize.term("umbriciansLiveView_everyoneOnline", onlineCount),
              this.localize.term("umbriciansLiveView_everyoneOnlineUsersHint"),
              onlineCount === 0,
            )}
            ${this.#renderAudience(
              "userGroups",
              this._userGroupKeys.length > 0 && groupCount !== undefined
                ? this.localize.term("umbriciansLiveView_userGroupsCount", groupCount)
                : this.localize.term("umbriciansLiveView_userGroups"),
              this._canPickUserGroups
                ? this.localize.term("umbriciansLiveView_userGroupsHint")
                : this.localize.term("umbriciansLiveView_userGroupsNeedUsersSection"),
              !this._canPickUserGroups,
            )}
          </uui-radio-group>

          ${this._audience === "selected"
            ? html`<div class="recipients">
                ${repeat(
                  data.selection.people,
                  (r) => r.key,
                  (r) => html`<uui-tag look="secondary">${r.name}</uui-tag>`,
                )}
              </div>`
            : nothing}
          ${this._audience === "userGroups" ? this.#renderUserGroups() : nothing}
        </uui-box>

        <uui-box headline=${this.localize.term("umbriciansLiveView_message")}>
          <form @submit=${this.#send}>
            <p class="muted">${this.localize.term("umbriciansLiveView_backofficeMessageNote")}</p>

            ${renderMessageFields(this.localize, this._text, (text) => (this._text = text), {
              headline: this.localize.term("umbriciansLiveView_userHeadlinePlaceholder"),
              message: this.localize.term("umbriciansLiveView_userMessagePlaceholder"),
            }, 6)}
          </form>
        </uui-box>

        <umb-footer-layout slot="footer">
          <uui-button slot="actions" look="secondary" label=${this.localize.term("general_close")} @click=${this._rejectModal}></uui-button>
          <uui-button
            slot="actions"
            look="primary"
            color=${sendColor(this._text)}
            label=${this.localize.term("umbriciansLiveView_sendToUsers", this.#recipientCount(this._audience))}
            .state=${this._sending ? "waiting" : undefined}
            ?disabled=${!this.#canSend}
            @click=${this.#send}></uui-button>
        </umb-footer-layout>
      </umb-body-layout>
    `;
  }

  static override styles = css`
    uui-box + uui-box {
      margin-top: var(--uui-size-layout-1);
    }

    uui-radio-group {
      display: grid;
      gap: var(--uui-size-space-3);
    }

    .option {
      display: grid;
      gap: 2px;
    }

    .recipients {
      display: flex;
      flex-wrap: wrap;
      gap: var(--uui-size-space-2);
      margin-top: var(--uui-size-space-4);
    }

    umb-user-group-input {
      display: block;
      margin-top: var(--uui-size-space-4);
    }

    .muted {
      color: var(--uui-color-text-alt);
      font-size: var(--uui-type-small-size);
    }

    form .muted {
      margin-top: 0;
    }

    uui-box > p.muted,
    uui-box > p.error {
      margin: var(--uui-size-space-3) 0 0;
    }

    .error {
      color: var(--uui-color-danger-standalone);
      font-size: var(--uui-type-small-size);
    }

    uui-input,
    uui-select,
    uui-textarea {
      width: 100%;
    }
  `;
}

export default UmbriciansLiveViewSendMessageModalElement;

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-live-view-send-message-modal": UmbriciansLiveViewSendMessageModalElement;
  }
}
