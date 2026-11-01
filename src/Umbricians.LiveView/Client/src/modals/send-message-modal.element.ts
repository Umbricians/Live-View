import { css, html, customElement, state, repeat, nothing } from "@umbraco-cms/backoffice/external/lit";
import { UmbModalBaseElement } from "@umbraco-cms/backoffice/modal";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import { sendMessage } from "../api/live-view.resource.js";
import { plural } from "../utils/format.js";
import { hasHeadline, newMessage, renderMessageFields, sendColor, trimmed } from "./message-fields.js";
import type {
  UmbriciansSendMessageModalData,
  UmbriciansSendMessageModalValue,
} from "./send-message.modal-token.js";

/**
 * Sidebar dialog for writing a notification to the backoffice users picked on the Live View dashboard.
 */
@customElement("umbricians-live-view-send-message-modal")
export class UmbriciansLiveViewSendMessageModalElement extends UmbModalBaseElement<
  UmbriciansSendMessageModalData,
  UmbriciansSendMessageModalValue
> {
  @state() private _text = newMessage();
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
    if (this._sending || !hasHeadline(this._text) || (this.data?.count ?? 0) === 0) return;

    this._sending = true;
    try {
      const result = await sendMessage({
        ...trimmed(this._text),
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
                Everyone else with the backoffice open when you send (${plural(count, "user", "users")} right now),
                including anyone who has signed in since you opened this.
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

            ${renderMessageFields(this._text, (text) => (this._text = text), {
              headline: "e.g. Upgrade in 10 minutes",
              message: "Please save your work and log out. The site will be unavailable for about 15 minutes.",
            }, 6)}
          </form>
        </uui-box>

        <umb-footer-layout slot="footer">
          <uui-button slot="actions" look="secondary" label="Close" @click=${this._rejectModal}></uui-button>
          <uui-button
            slot="actions"
            look="primary"
            color=${sendColor(this._text)}
            label="Send"
            .state=${this._sending ? "waiting" : undefined}
            ?disabled=${!hasHeadline(this._text) || this._sending}
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

export default UmbriciansLiveViewSendMessageModalElement;

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-live-view-send-message-modal": UmbriciansLiveViewSendMessageModalElement;
  }
}
