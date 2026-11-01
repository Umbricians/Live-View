import { css, html, customElement, state, repeat, nothing } from "@umbraco-cms/backoffice/external/lit";
import { UmbModalBaseElement } from "@umbraco-cms/backoffice/modal";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import type { UUIBooleanInputEvent, UUIRadioGroupEvent, UUISelectEvent } from "@umbraco-cms/backoffice/external/uui";
import { sendMemberMessage, type MemberAudience } from "../api/live-view.resource.js";
import { plural } from "../utils/format.js";
import { LONGEST_MINUTES, formatAt, formatDuration, nextMidnight, timeZoneLabel } from "../utils/expiry.js";
import type {
  UmbriciansSendMemberMessageModalData,
  UmbriciansSendMemberMessageModalValue,
} from "./send-member-message.modal-token.js";
import { hasHeadline, newMessage, renderMessageFields, sendColor, trimmed } from "./message-fields.js";

/** A number of minutes, midnight tonight in the site's time zone, or hours and minutes typed in. */
type Lifetime = number | "midnight" | "custom";

const LIFETIMES: Array<{ name: string; value: Lifetime }> = [
  { name: "15 minutes", value: 15 },
  { name: "1 hour", value: 60 },
  { name: "Midnight tonight", value: "midnight" },
  { name: "24 hours", value: 60 * 24 },
  { name: "7 days", value: 60 * 24 * 7 },
  { name: "Custom…", value: "custom" },
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
  @state() private _text = newMessage();
  @state() private _lifetime: Lifetime = 60;
  @state() private _customHours = 1;
  @state() private _customMinutes = 0;
  @state() private _now = Date.now();
  @state() private _sending = false;

  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;
  #clock?: number;

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
    // Keeps "in 5 h 12 min" true while the dialog stays open.
    this.#clock = window.setInterval(() => (this._now = Date.now()), 30_000);
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    window.clearInterval(this.#clock);
  }

  get #timeZone() {
    return this.data?.timeZone ?? undefined;
  }

  /** When the message stops being shown, counted from `now`; undefined while the custom time is out of range. */
  #expiresAt(now: number): Date | undefined {
    if (this._lifetime === "midnight") return nextMidnight(new Date(now), this.#timeZone);
    const minutes = this._lifetime === "custom" ? this._customHours * 60 + this._customMinutes : this._lifetime;
    return Number.isInteger(minutes) && minutes >= 1 && minutes <= LONGEST_MINUTES
      ? new Date(now + minutes * 60_000)
      : undefined;
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
      hasHeadline(this._text) &&
      this._senders.size > 0 &&
      (this._audience === "allMembers" || this.#recipientCount(this._audience) > 0) &&
      this.#expiresAt(this._now) !== undefined
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
    // Counted from the moment of sending, not when the dialog was last redrawn.
    const expiresAt = this.#expiresAt(Date.now());
    if (!this.#canSend || !this.data || !expiresAt) return;

    this._sending = true;
    try {
      const result = await sendMemberMessage({
        ...trimmed(this._text),
        audience: this._audience,
        memberKeys: this._audience === "selected" ? this.data.selection.people.map((m) => m.key) : undefined,
        senders: [...this._senders],
        expiresAt: expiresAt.toISOString(),
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

  #renderExpiry() {
    const expiresAt = this.#expiresAt(this._now);
    if (!expiresAt) {
      return html`<p class="expiry invalid">Enter between 1 minute and ${LONGEST_MINUTES / 60 / 24} days.</p>`;
    }
    const timeZone = this.#timeZone;
    return html`<p class="expiry muted">
      Stops being shown ${formatAt(expiresAt, timeZone)} ${timeZoneLabel(timeZone)}
      (in ${formatDuration(expiresAt.getTime() - this._now)}).
    </p>`;
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
            ${renderMessageFields(this._text, (text) => (this._text = text), {
              headline: "e.g. Scheduled maintenance at 18:00",
              message: "The site will be unavailable for about 15 minutes. Please save anything you are working on.",
            }, 5)}

            <uui-form-layout-item>
              <uui-label slot="label" for="lifetime">Relevant for</uui-label>
              <span slot="description">After this, banners and other queued messages are no longer shown.</span>
              <uui-select
                id="lifetime"
                label="Relevant for"
                .options=${LIFETIMES.map((l) => ({ name: l.name, value: String(l.value), selected: l.value === this._lifetime }))}
                @change=${(e: UUISelectEvent) => {
                  const value = String(e.target.value);
                  this._lifetime = value === "midnight" || value === "custom" ? value : Number(value);
                }}></uui-select>
              ${this._lifetime === "custom"
                ? html`<div class="custom">
                    <uui-input
                      type="number"
                      label="Hours"
                      min="0"
                      max=${LONGEST_MINUTES / 60}
                      .value=${String(this._customHours)}
                      @input=${(e: InputEvent) => (this._customHours = Number((e.target as HTMLInputElement).value))}>
                      <span slot="append" class="unit">hours</span>
                    </uui-input>
                    <uui-input
                      type="number"
                      label="Minutes"
                      min="0"
                      max="59"
                      .value=${String(this._customMinutes)}
                      @input=${(e: InputEvent) => (this._customMinutes = Number((e.target as HTMLInputElement).value))}>
                      <span slot="append" class="unit">minutes</span>
                    </uui-input>
                  </div>`
                : nothing}
              ${this.#renderExpiry()}
            </uui-form-layout-item>
          </form>
        </uui-box>

        <umb-footer-layout slot="footer">
          <uui-button slot="actions" look="secondary" label="Close" @click=${this._rejectModal}></uui-button>
          <uui-button
            slot="actions"
            look="primary"
            color=${sendColor(this._text)}
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

    .custom {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: var(--uui-size-space-3);
      margin-top: var(--uui-size-space-3);
    }

    .unit {
      display: flex;
      align-items: center;
      padding: 0 var(--uui-size-space-3);
      color: var(--uui-color-text-alt);
    }

    .expiry {
      margin: var(--uui-size-space-2) 0 0;
      font-size: var(--uui-type-small-size);
    }

    .expiry.invalid {
      color: var(--uui-color-danger-standalone);
    }
  `;
}

export default UmbriciansSendMemberMessageModalElement;

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-send-member-message-modal": UmbriciansSendMemberMessageModalElement;
  }
}
