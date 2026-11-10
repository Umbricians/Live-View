import { css, html, customElement, state, repeat, nothing } from "@umbraco-cms/backoffice/external/lit";
import { UmbModalBaseElement } from "@umbraco-cms/backoffice/modal";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import { sendMessage } from "../api/live-view.resource.js";
import { describeError } from "../api/request-error.js";
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
        data: { headline: this.localize.term("umbriciansLiveView_messageNotSent"), message: describeError(this.localize, error) },
      });
    } finally {
      this._sending = false;
    }
  }

  override render() {
    const recipients = this.#recipients;
    const count = this.data?.count ?? 0;

    return html`
      <umb-body-layout headline=${this.localize.term("umbriciansLiveView_sendMessage")}>
        <uui-box
          headline=${this.data?.allOnline ? this.localize.term("umbriciansLiveView_toEveryoneOnline") : this.localize.term("umbriciansLiveView_toUsers", count)}>
          ${this.data?.allOnline
            ? html`<p class="muted">${this.localize.term("umbriciansLiveView_everyoneOnlineUsersNote", count)}</p>`
            : nothing}
          <div class="recipients">
            ${repeat(
              recipients,
              (r) => r.key,
              (r) => html`<uui-tag look="secondary">${r.name}</uui-tag>`,
            )}
          </div>
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
            label=${this.localize.term("umbriciansLiveView_send")}
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
