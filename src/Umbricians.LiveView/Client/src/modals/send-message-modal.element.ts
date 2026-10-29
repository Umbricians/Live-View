import { css, html, customElement, state, repeat, nothing } from "@umbraco-cms/backoffice/external/lit";
import { UmbModalBaseElement } from "@umbraco-cms/backoffice/modal";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import type { UUISelectEvent } from "@umbraco-cms/backoffice/external/uui";
import { sendMessage, type MessageColor } from "../api/live-view.resource.js";
import { plural } from "../utils/format.js";
import type {
  UmbriciansSendMessageModalData,
  UmbriciansSendMessageModalValue,
} from "./send-message.modal-token.js";

const COLORS: Array<{ name: string; value: MessageColor }> = [
  { name: "Information", value: "default" },
  { name: "Warning", value: "warning" },
  { name: "Urgent", value: "danger" },
  { name: "Good news", value: "positive" },
];

/**
 * Sidebar dialog for writing a notification to the backoffice users picked on the Live View dashboard.
 */
@customElement("umbricians-send-message-modal")
export class UmbriciansSendMessageModalElement extends UmbModalBaseElement<
  UmbriciansSendMessageModalData,
  UmbriciansSendMessageModalValue
> {
  @state() private _headline = "";
  @state() private _message = "";
  @state() private _color: MessageColor = "warning";
  @state() private _sending = false;

  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));
  }

  get #recipients() {
    return this.data?.recipients ?? [];
  }

  async #send(event?: Event) {
    event?.preventDefault();
    if (this._sending || !this._headline.trim() || (this.data?.count ?? 0) === 0) return;

    this._sending = true;
    try {
      const result = await sendMessage({
        headline: this._headline.trim(),
        message: this._message.trim(),
        color: this._color,
        ...(this.data?.allOnline ? { allOnline: true } : { userKeys: this.#recipients.map((r) => r.key) }),
      });
      this.updateValue({ sentTo: result.recipients });
      this._submitModal();
    } catch (error) {
      this.#notifications?.peek("danger", {
        data: { headline: "Message not sent", message: error instanceof Error ? error.message : String(error) },
      });
    } finally {
      this._sending = false;
    }
  }

  override render() {
    const recipients = this.#recipients;
    const count = this.data?.count ?? 0;

    return html`
      <umb-body-layout headline="Send message">
        <uui-box headline=${this.data?.allOnline ? "To everyone online" : `To ${plural(count, "user", "users")}`}>
          ${this.data?.allOnline
            ? html`<p class="muted">
                All ${plural(count, "backoffice user", "backoffice users")} online when you send, including anyone who
                has signed in since you opened this.
              </p>`
            : nothing}
          <div class="recipients">
            ${repeat(
              recipients,
              (r) => r.key,
              (r) => html`<uui-tag look="secondary">${r.name}</uui-tag>`,
            )}
          </div>
        </uui-box>

        <uui-box headline="Message">
          <form @submit=${this.#send}>
            <p class="muted">
              Shown in their backoffice as a notification that stays on screen until dismissed, signed with your name.
            </p>

            <uui-form-layout-item>
              <uui-label slot="label" for="headline" required>Headline</uui-label>
              <uui-input
                id="headline"
                label="Headline"
                placeholder="e.g. Upgrade in 10 minutes"
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
                placeholder="Please save your work and log out. The site will be unavailable for about 15 minutes."
                maxlength="2000"
                rows="6"
                .value=${this._message}
                @input=${(e: InputEvent) => (this._message = (e.target as HTMLTextAreaElement).value)}></uui-textarea>
            </uui-form-layout-item>
          </form>
        </uui-box>

        <umb-footer-layout slot="footer">
          <uui-button slot="actions" look="secondary" label="Close" @click=${this._rejectModal}></uui-button>
          <uui-button
            slot="actions"
            look="primary"
            color=${this._color === "danger" ? "danger" : "positive"}
            label="Send"
            .state=${this._sending ? "waiting" : undefined}
            ?disabled=${!this._headline.trim() || this._sending}
            @click=${this.#send}></uui-button>
        </umb-footer-layout>
      </umb-body-layout>
    `;
  }

  static override styles = css`
    uui-box + uui-box {
      margin-top: var(--uui-size-layout-1);
    }

    .recipients {
      display: flex;
      flex-wrap: wrap;
      gap: var(--uui-size-space-2);
    }

    .muted {
      color: var(--uui-color-text-alt);
      margin-top: 0;
    }

    uui-input,
    uui-select,
    uui-textarea {
      width: 100%;
    }
  `;
}

export default UmbriciansSendMessageModalElement;

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-send-message-modal": UmbriciansSendMessageModalElement;
  }
}
