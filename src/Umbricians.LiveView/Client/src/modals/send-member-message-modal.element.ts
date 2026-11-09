import { css, html, customElement, state, repeat, nothing } from "@umbraco-cms/backoffice/external/lit";
import { UmbModalBaseElement } from "@umbraco-cms/backoffice/modal";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import type { UUIBooleanInputEvent, UUIRadioGroupEvent, UUISelectEvent } from "@umbraco-cms/backoffice/external/uui";
import { sendMemberMessage, type MemberAudience } from "../api/live-view.resource.js";
import { describeError } from "../utils/format.js";
import { LONGEST_MINUTES, durationParts, nextMidnight, timeZoneCity } from "../utils/expiry.js";
import type {
  UmbriciansSendMemberMessageModalData,
  UmbriciansSendMemberMessageModalValue,
} from "./send-member-message.modal-token.js";
import { hasHeadline, newMessage, renderMessageFields, sendColor, trimmed } from "./message-fields.js";

/** A number of minutes, midnight tonight in the site's time zone, or hours and minutes typed in. */
type Lifetime = number | "midnight" | "custom";

type LifetimeChoice = { amount: number; unit: "minute" | "hour" | "day" } | "midnight" | "custom";

const MINUTES_IN = { minute: 1, hour: 60, day: 60 * 24 };

const LIFETIMES: Array<LifetimeChoice> = [
  { amount: 15, unit: "minute" },
  { amount: 1, unit: "hour" },
  "midnight",
  { amount: 24, unit: "hour" },
  { amount: 7, unit: "day" },
  "custom",
];

/**
 * Sidebar dialog for messaging website members: who it goes to, how it is delivered (the site's registered
 * senders), the message itself and how long it stays relevant.
 */
@customElement("umbricians-live-view-send-member-message-modal")
export class UmbriciansLiveViewSendMemberMessageModalElement extends UmbModalBaseElement<
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
    // Keeps the time left true while the dialog stays open.
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
        data: { headline: this.localize.term("umbriciansLiveView_messageNotSent"), message: describeError(this.localize, error) },
      });
    } finally {
      this._sending = false;
    }
  }

  #lifetimeOptions() {
    return LIFETIMES.map((choice) => {
      if (choice === "midnight" || choice === "custom") {
        return {
          name: choice === "midnight" ? this.localize.term("umbriciansLiveView_lifetimeMidnight") : this.localize.term("umbriciansLiveView_lifetimeCustom"),
          value: choice,
          selected: choice === this._lifetime,
        };
      }
      const minutes = choice.amount * MINUTES_IN[choice.unit];
      return {
        name: this.localize.number(choice.amount, { style: "unit", unit: choice.unit, unitDisplay: "long" }),
        value: String(minutes),
        selected: minutes === this._lifetime,
      };
    });
  }

  #renderExpiry() {
    const expiresAt = this.#expiresAt(this._now);
    if (!expiresAt) {
      return html`<p class="expiry invalid">${this.localize.term("umbriciansLiveView_lifetimeOutOfRange", LONGEST_MINUTES / 60 / 24)}</p>`;
    }
    const timeZone = this.#timeZone;
    const city = timeZoneCity(timeZone);
    const at = this.localize.date(expiresAt, {
      timeZone,
      weekday: "short",
      day: "numeric",
      month: "short",
      hour: "2-digit",
      minute: "2-digit",
    });
    const zone = city ? this.localize.term("umbriciansLiveView_zoneTime", city) : this.localize.term("umbriciansLiveView_yourTime");
    const duration = this.localize.list(
      durationParts(expiresAt.getTime() - this._now).map((part) =>
        this.localize.number(part.value, { style: "unit", unit: part.unit, unitDisplay: "short" }),
      ),
      { type: "unit", style: "narrow" },
    );
    return html`<p class="expiry muted">${this.localize.term("umbriciansLiveView_stopsBeingShown", at, zone, duration)}</p>`;
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
      <umb-body-layout headline=${this.localize.term("umbriciansLiveView_messageMembers")}>
        <uui-box headline=${this.localize.term("umbriciansLiveView_sendTo")}>
          <uui-radio-group
            label=${this.localize.term("umbriciansLiveView_sendTo")}
            .value=${this._audience}
            @change=${(e: UUIRadioGroupEvent) => (this._audience = e.target.value as MemberAudience)}>
            ${this.#renderAudience(
              "selected",
              this.localize.term("umbriciansLiveView_selectedMembers", selectedCount),
              selectedCount === 0 ? this.localize.term("umbriciansLiveView_selectedMembersNoneHint") : this.localize.term("umbriciansLiveView_selectedMembersHint"),
            )}
            ${this.#renderAudience(
              "allOnline",
              this.localize.term("umbriciansLiveView_everyoneOnline", data.onlineCount),
              this.localize.term("umbriciansLiveView_everyoneOnlineHint"),
            )}
            ${this.#renderAudience(
              "allMembers",
              this.localize.term("umbriciansLiveView_everyMember", data.totalMembers),
              this.localize.term("umbriciansLiveView_everyMemberHint"),
            )}
          </uui-radio-group>
        </uui-box>

        <uui-box headline=${this.localize.term("umbriciansLiveView_deliverBy")}>
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
                    ? html`<span class="hint">${this.localize.term("umbriciansLiveView_onlineOnlySender")}</span>`
                    : nothing}
                </div>
              </uui-checkbox>
            `,
          )}
        </uui-box>

        <uui-box headline=${this.localize.term("umbriciansLiveView_message")}>
          <form @submit=${this.#send}>
            ${renderMessageFields(this.localize, this._text, (text) => (this._text = text), {
              headline: this.localize.term("umbriciansLiveView_memberHeadlinePlaceholder"),
              message: this.localize.term("umbriciansLiveView_memberMessagePlaceholder"),
            }, 5)}

            <uui-form-layout-item>
              <uui-label slot="label" for="lifetime">${this.localize.term("umbriciansLiveView_relevantFor")}</uui-label>
              <span slot="description">${this.localize.term("umbriciansLiveView_relevantForDescription")}</span>
              <uui-select
                id="lifetime"
                label=${this.localize.term("umbriciansLiveView_relevantFor")}
                .options=${this.#lifetimeOptions()}
                @change=${(e: UUISelectEvent) => {
                  const value = String(e.target.value);
                  this._lifetime = value === "midnight" || value === "custom" ? value : Number(value);
                }}></uui-select>
              ${this._lifetime === "custom"
                ? html`<div class="custom">
                    <uui-input
                      type="number"
                      label=${this.localize.term("umbriciansLiveView_hours")}
                      min="0"
                      max=${LONGEST_MINUTES / 60}
                      .value=${String(this._customHours)}
                      @input=${(e: InputEvent) => (this._customHours = Number((e.target as HTMLInputElement).value))}>
                      <span slot="append" class="unit">${this.localize.term("umbriciansLiveView_hoursUnit")}</span>
                    </uui-input>
                    <uui-input
                      type="number"
                      label=${this.localize.term("umbriciansLiveView_minutes")}
                      min="0"
                      max="59"
                      .value=${String(this._customMinutes)}
                      @input=${(e: InputEvent) => (this._customMinutes = Number((e.target as HTMLInputElement).value))}>
                      <span slot="append" class="unit">${this.localize.term("umbriciansLiveView_minutesUnit")}</span>
                    </uui-input>
                  </div>`
                : nothing}
              ${this.#renderExpiry()}
            </uui-form-layout-item>
          </form>
        </uui-box>

        <umb-footer-layout slot="footer">
          <uui-button slot="actions" look="secondary" label=${this.localize.term("general_close")} @click=${this._rejectModal}></uui-button>
          <uui-button
            slot="actions"
            look="primary"
            color=${sendColor(this._text)}
            label=${this._audience === "allMembers"
              ? this.localize.term("umbriciansLiveView_sendToEveryMember")
              : this.localize.term("umbriciansLiveView_sendToMembers", this.#recipientCount(this._audience))}
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

export default UmbriciansLiveViewSendMemberMessageModalElement;

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-live-view-send-member-message-modal": UmbriciansLiveViewSendMemberMessageModalElement;
  }
}
