import { css, html, customElement, state, repeat, nothing } from "@umbraco-cms/backoffice/external/lit";
import { UmbModalBaseElement } from "@umbraco-cms/backoffice/modal";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import type { UUIBooleanInputEvent, UUIRadioGroupEvent, UUISelectEvent } from "@umbraco-cms/backoffice/external/uui";
import { sendMemberMessage, type MemberAudience, type MessageColor } from "../api/live-view.resource.js";
import { plural } from "../utils/format.js";
import type {
  UmbriciansSendMemberMessageModalData,
  UmbriciansSendMemberMessageModalValue,
} from "./send-member-message.modal-token.js";

const COLORS: Array<{ name: string; value: MessageColor }> = [
  { name: "Information", value: "default" },
  { name: "Warning", value: "warning" },
  { name: "Urgent", value: "danger" },
  { name: "Good news", value: "positive" },
];

const LIFETIMES: Array<{ name: string; minutes: number }> = [
  { name: "15 minutes", minutes: 15 },
  { name: "1 hour", minutes: 60 },
  { name: "24 hours", minutes: 60 * 24 },
  { name: "7 days", minutes: 60 * 24 * 7 },
];

/**
 * Sidebar dialog for messaging website members: who it goes to, how it is delivered (the site's registered
 * senders), the message itself and how long it stays relevant.
 */
@customElement("umbricians-send-member-message-modal")
export class UmbriciansSendMemberMessageModalElement extends UmbModalBaseElement<
  UmbriciansSendMemberMessageModalData,
  UmbriciansSendMemberMessageModalValue
> {
  @state() private _audience: MemberAudience = "allOnline";
  @state() private _senders = new Set<string>();
  @state() private _headline = "";
  @state() private _message = "";
  @state() private _color: MessageColor = "warning";
  @state() private _lifetime = 60;
  @state() private _sending = false;

  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));
  }

  override connectedCallback() {
    super.connectedCallback();
    // Start from what was picked in the table; with nothing picked, default to everyone online.
    const selection = this.data?.selection;
    this._audience = selection?.allOnline ? "allOnline" : selection && selection.count > 0 ? "selected" : "allOnline";
    this._senders = new Set(this.data?.senders.map((s) => s.alias));
  }

  #recipientCount(audience: MemberAudience) {
    const data = this.data;
    if (!data) return 0;
    if (audience === "selected") return data.selection.allOnline ? 0 : data.selection.count;
    return audience === "allOnline" ? data.onlineCount : data.totalMembers;
  }

  get #canSend() {
    return (
      !this._sending &&
      this._headline.trim().length > 0 &&
      this._senders.size > 0 &&
      (this._audience === "allMembers" || this.#recipientCount(this._audience) > 0)
    );
  }

  #toggleSender(alias: string, checked: boolean) {
    const senders = new Set(this._senders);
    if (checked) senders.add(alias);
    else senders.delete(alias);
    this._senders = senders;
  }

  async #send(event?: Event) {
    event?.preventDefault();
    if (!this.#canSend || !this.data) return;

    this._sending = true;
    try {
      const result = await sendMemberMessage({
        headline: this._headline.trim(),
        message: this._message.trim(),
        color: this._color,
        audience: this._audience,
        memberKeys: this._audience === "selected" ? this.data.selection.members.map((m) => m.key) : undefined,
        senders: [...this._senders],
        expiresInMinutes: this._lifetime,
      });
      this.updateValue(result);
      this._submitModal();
    } catch (error) {
      this.#notifications?.peek("danger", {
        data: { headline: "Message not sent", message: error instanceof Error ? error.message : String(error) },
      });
    } finally {
      this._sending = false;
    }
  }

  #renderAudience(audience: MemberAudience, label: string, hint: string) {
    const count = this.#recipientCount(audience);
    const disabled = audience === "selected" && count === 0;
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

  override render() {
    const data = this.data;
    if (!data) return nothing;
    const selectedCount = this.#recipientCount("selected");

    return html`
      <umb-body-layout headline="Message members">
        <uui-box headline="Send to">
          <uui-radio-group
            label="Send to"
            .value=${this._audience}
            @change=${(e: UUIRadioGroupEvent) => (this._audience = e.target.value as MemberAudience)}>
            ${this.#renderAudience(
              "selected",
              `Selected members (${selectedCount})`,
              selectedCount === 0 ? "Tick members in the table to send to just them." : "The members ticked in the table.",
            )}
            ${this.#renderAudience(
              "allOnline",
              `Everyone online (${data.onlineCount})`,
              "Every member logged in when you send, on every page of the table.",
            )}
            ${this.#renderAudience(
              "allMembers",
              `Every member (${data.totalMembers})`,
              "All members of the site, including those not logged in right now.",
            )}
          </uui-radio-group>
        </uui-box>

        <uui-box headline="Deliver by">
          ${repeat(
            data.senders,
            (s) => s.alias,
            (s) => html`
              <uui-checkbox
                label=${s.name}
                .checked=${this._senders.has(s.alias)}
                @change=${(e: UUIBooleanInputEvent) => this.#toggleSender(s.alias, e.target.checked)}>
                <div class="option">
                  <strong>${s.name}</strong>
                  ${s.description ? html`<span class="muted">${s.description}</span>` : nothing}
                  ${this._audience === "allMembers" && !s.reachesOfflineMembers
                    ? html`<span class="hint">Only reaches members who are online now.</span>`
                    : nothing}
                </div>
              </uui-checkbox>
            `,
          )}
        </uui-box>

        <uui-box headline="Message">
          <form @submit=${this.#send}>
            <uui-form-layout-item>
              <uui-label slot="label" for="headline" required>Headline</uui-label>
              <uui-input
                id="headline"
                label="Headline"
                placeholder="e.g. Scheduled maintenance at 18:00"
                maxlength="200"
                .value=${this._headline}
                @input=${(e: InputEvent) => (this._headline = (e.target as HTMLInputElement).value)}></uui-input>
            </uui-form-layout-item>

            <uui-form-layout-item>
              <uui-label slot="label" for="type">Type</uui-label>
              <uui-select
                id="type"
                label="Type"
                .options=${COLORS.map((c) => ({ ...c, selected: c.value === this._color }))}
                @change=${(e: UUISelectEvent) => (this._color = e.target.value as MessageColor)}></uui-select>
            </uui-form-layout-item>

            <uui-form-layout-item>
              <uui-label slot="label" for="message">Message</uui-label>
              <uui-textarea
                id="message"
                label="Message"
                placeholder="The site will be unavailable for about 15 minutes. Please save anything you are working on."
                maxlength="2000"
                rows="5"
                .value=${this._message}
                @input=${(e: InputEvent) => (this._message = (e.target as HTMLTextAreaElement).value)}></uui-textarea>
            </uui-form-layout-item>

            <uui-form-layout-item>
              <uui-label slot="label" for="lifetime">Relevant for</uui-label>
              <span slot="description">After this, banners and other queued messages are no longer shown.</span>
              <uui-select
                id="lifetime"
                label="Relevant for"
                .options=${LIFETIMES.map((l) => ({ name: l.name, value: String(l.minutes), selected: l.minutes === this._lifetime }))}
                @change=${(e: UUISelectEvent) => (this._lifetime = Number(e.target.value))}></uui-select>
            </uui-form-layout-item>
          </form>
        </uui-box>

        <umb-footer-layout slot="footer">
          <uui-button slot="actions" look="secondary" label="Close" @click=${this._rejectModal}></uui-button>
          <uui-button
            slot="actions"
            look="primary"
            color=${this._color === "danger" ? "danger" : "positive"}
            label=${this._audience === "allMembers"
              ? "Send to every member"
              : `Send to ${plural(this.#recipientCount(this._audience), "member", "members")}`}
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

    uui-checkbox {
      display: block;
      margin-bottom: var(--uui-size-space-3);
    }

    .option {
      display: grid;
      gap: 2px;
    }

    .muted {
      color: var(--uui-color-text-alt);
      font-size: var(--uui-type-small-size);
    }

    .hint {
      color: var(--uui-color-warning-standalone);
      font-size: var(--uui-type-small-size);
    }

    uui-input,
    uui-select,
    uui-textarea {
      width: 100%;
    }
  `;
}

export default UmbriciansSendMemberMessageModalElement;

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-send-member-message-modal": UmbriciansSendMemberMessageModalElement;
  }
}
