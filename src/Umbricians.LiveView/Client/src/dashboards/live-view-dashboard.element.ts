import { LitElement, css, html, customElement, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import type { UUISelectEvent } from "@umbraco-cms/backoffice/external/uui";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import { umbOpenModal } from "@umbraco-cms/backoffice/modal";
import { UMBRICIANS_SEND_MESSAGE_MODAL } from "../modals/send-message.modal-token.js";
import { UMBRICIANS_SEND_MEMBER_MESSAGE_MODAL } from "../modals/send-member-message.modal-token.js";
import type { DashboardSettings, MemberMessaging } from "../api/live-view.resource.js";
import { LIVE_VIEW_PANEL_TYPE } from "./constants.js";
import { LiveViewDashboardContext } from "./live-view-dashboard.context.js";
import type { OnlineTableSelection } from "./online-selection.js";
// Loaded with the dashboard so the built-in panels appear together.
import "./live-view-activity.element.js";
import "./live-view-users.element.js";
import "./live-view-members.element.js";
import { describeError } from "../api/request-error.js";

/**
 * The dashboard: a header with the refresh controls, the panels, and a footer with the message actions. What the
 * panels share lives in the dashboard context it provides.
 */
@customElement("umbricians-live-view-dashboard")
export class UmbriciansLiveViewDashboardElement extends UmbElementMixin(LitElement) {
  /** The site's dashboard settings; the panels aren't shown until they are read. */
  @state() private _settings?: DashboardSettings;
  @state() private _interval = 5;
  @state() private _paused = false;
  @state() private _lastUpdated?: Date;
  @state() private _error?: unknown;

  /** The current user's Live View messaging permissions, from their user groups. */
  @state() private _canMessageUsers = false;
  @state() private _canMessageMembers = false;

  /** The site's member message senders; member messaging is offered only when there is at least one. */
  @state() private _memberMessaging?: MemberMessaging;

  /** Who is picked in each table, while its panel is shown. */
  @state() private _usersSelection?: OnlineTableSelection;
  @state() private _membersSelection?: OnlineTableSelection;

  #context: LiveViewDashboardContext;
  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  constructor() {
    super();
    this.#context = new LiveViewDashboardContext(this);
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));

    const context = this.#context;
    this.observe(context.settings, (settings) => (this._settings = settings));
    this.observe(context.interval,(interval) => (this._interval = interval));
    this.observe(context.paused, (paused) => (this._paused = paused));
    this.observe(context.error, (error) => (this._error = error));
    this.observe(context.snapshot, (snapshot) => (this._lastUpdated = snapshot ? new Date(snapshot.timestamp) : undefined));
    this.observe(context.canMessageUsers, (can) => (this._canMessageUsers = can));
    this.observe(context.canMessageMembers, (can) => (this._canMessageMembers = can));
    this.observe(context.memberMessaging, (messaging) => (this._memberMessaging = messaging));
    this.observe(context.usersSelection, (selection) => (this._usersSelection = selection));
    this.observe(context.membersSelection, (selection) => (this._membersSelection = selection));
  }

  #onIntervalChange(event: UUISelectEvent) {
    this.#context.setRefreshInterval(Number(event.target.value));
  }

  async #openSendMemberMessage() {
    const members = this._membersSelection;
    const messaging = this._memberMessaging;
    if (!members || !messaging?.senders.length) return;

    try {
      const result = await umbOpenModal(this, UMBRICIANS_SEND_MEMBER_MESSAGE_MODAL, {
        data: {
          selection: members.summary,
          onlineCount: members.onlineCount,
          totalMembers: messaging.totalMembers,
          senders: messaging.senders,
          timeZone: messaging.timeZone,
        },
      });
      const failed = result.results.filter((r) => r.error);
      const summary = result.results
        .map((r) => {
          if (r.error) return this.localize.term("umbriciansLiveView_senderFailed", r.name, r.error);
          const text =
            r.delivered === null
              ? this.localize.term("umbriciansLiveView_senderSent", r.name)
              : this.localize.term("umbriciansLiveView_senderDelivered", r.name, r.delivered);
          return r.note ? this.localize.term("umbriciansLiveView_senderNote", text, r.note) : text;
        })
        .join(" · ");
      this.#notifications?.peek(failed.length ? "warning" : "positive", {
        data: { headline: failed.length ? this.localize.term("umbriciansLiveView_messagePartlySent") : this.localize.term("umbriciansLiveView_messageSentToMembers"),
          message: summary, },
      });
      members.clear();
    } catch {
      // Closed without sending.
    }
  }

  async #openSendMessage() {
    const users = this._usersSelection;
    if (!users) return;
    const selection = users.summary;

    try {
      const { sentTo } = await umbOpenModal(this, UMBRICIANS_SEND_MESSAGE_MODAL, {
        data: { selection, onlineCount: users.recipients },
      });
      this.#notifications?.peek("positive", {
        data: { headline: this.localize.term("umbriciansLiveView_messageSent"),
          message: this.localize.term("umbriciansLiveView_sentToUsers", sentTo), },
      });
      users.clear();
    } catch {
      // Closed without sending.
    }
  }

  override render() {
    const users = this._usersSelection;
    const members = this._membersSelection;
    // A message action is offered only while its panel is shown. Members can be messaged only with the permission
    // and at least one delivery method set up by a developer.
    const canMessageUsers = this._canMessageUsers && users !== undefined;
    const canMessageMembers = this._canMessageMembers && members !== undefined;
    const selected = users?.summary.count ?? 0;
    const selectedMembers = members?.summary.count ?? 0;
    const hint = canMessageUsers && canMessageMembers
      ? this.localize.term("umbriciansLiveView_selectUsersOrMembers")
      : canMessageUsers
        ? this.localize.term("umbriciansLiveView_selectUsers")
        : canMessageMembers
          ? this.localize.term("umbriciansLiveView_selectMembers")
          : "";

    return html`
      <umb-body-layout header-transparent>
        <div slot="header" class="toolbar">
          <div class="status">
            <span class="live-dot ${this._paused || this._error !== undefined ? "off" : ""}"></span>
            ${this._error !== undefined
              ? html`<span class="error">${describeError(this.localize, this._error)}</span>`
              : this._lastUpdated
                ? html`<span class="muted"
                    >${this.localize.term("umbriciansLiveView_updatedAt", this.localize.date(this._lastUpdated, { timeStyle: "medium" }))}</span
                  >`
                : html`<span class="muted">${this.localize.term("umbriciansLiveView_connecting")}</span>`}
          </div>
          <div class="controls">
            <uui-select
              label=${this.localize.term("umbriciansLiveView_refreshInterval")}
              .options=${(this._settings?.refreshIntervals ?? []).map((s) => ({ name: this.localize.term("umbriciansLiveView_everySeconds", s), value: String(s), selected: s === this._interval }))}
              @change=${this.#onIntervalChange}></uui-select>
            <uui-button
              look="secondary"
              compact
              label=${this._paused ? this.localize.term("umbriciansLiveView_resume") : this.localize.term("umbriciansLiveView_pause")}
              @click=${() => this.#context.togglePaused()}>
              <uui-icon name=${this._paused ? "icon-play" : "icon-pause"}></uui-icon>
            </uui-button>
            <uui-button look="secondary" compact label=${this.localize.term("umbriciansLiveView_clearChart")} @click=${() => this.#context.clearReadings()}>
              <uui-icon name="icon-trash"></uui-icon>
            </uui-button>
          </div>
        </div>

        <div class="content">
          ${this._settings
            ? html`<umb-extension-slot type=${LIVE_VIEW_PANEL_TYPE}></umb-extension-slot>`
            : html`<uui-loader-bar></uui-loader-bar>`}
        </div>

        ${canMessageUsers || canMessageMembers
          ? html`<span slot="footer-info" class="muted footer-info">
              ${selected === 0 && selectedMembers === 0
                ? hint
                : [
                    selected ? this.localize.term("umbriciansLiveView_usersSelected", selected) : "",
                    selectedMembers ? this.localize.term("umbriciansLiveView_membersSelected", selectedMembers) : "",
                  ]
                    .filter(Boolean)
                    .join(" · ")}
            </span>`
          : ""}
        ${canMessageMembers
          ? html`<uui-button
              slot="actions"
              look="secondary"
              label=${this.localize.term("umbriciansLiveView_messageMembersButton")}
              @click=${this.#openSendMemberMessage}></uui-button>`
          : ""}
        ${canMessageUsers
          ? html`<uui-button
              slot="actions"
              look="primary"
              color="positive"
              label=${this.localize.term("umbriciansLiveView_messageUsersButton")}
              @click=${this.#openSendMessage}></uui-button>`
          : ""}
      </umb-body-layout>
    `;
  }

  static override styles = css`
    :host {
      display: block;
      height: 100%;
    }

    /* minmax(0, 1fr) and each panel's min-width: 0 let the chart canvas shrink again after the window narrows. */
    .content {
      display: grid;
      grid-template-columns: minmax(0, 1fr);
      gap: var(--uui-size-layout-1);
    }

    .toolbar {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--uui-size-space-3);
      width: 100%;
    }

    .status,
    .controls {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-3);
    }

    .live-dot {
      flex: none;
      width: 10px;
      height: 10px;
      border-radius: 50%;
      background: var(--uui-color-positive, #2bc37c);
      animation: pulse 2s infinite;
    }

    .live-dot.off {
      background: var(--uui-color-disabled-contrast, #a1a1a1);
      animation: none;
    }

    @keyframes pulse {
      50% {
        opacity: 0.35;
      }
    }

    .muted {
      color: var(--uui-color-text-alt);
    }

    .error {
      color: var(--uui-color-danger);
    }

    .footer-info {
      margin-left: var(--uui-size-layout-1);
    }
  `;
}

export default UmbriciansLiveViewDashboardElement;

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-live-view-dashboard": UmbriciansLiveViewDashboardElement;
  }
}
