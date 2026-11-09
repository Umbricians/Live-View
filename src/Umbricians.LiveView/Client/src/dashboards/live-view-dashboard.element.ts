import { LitElement, css, html, customElement, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import type { UUISelectEvent } from "@umbraco-cms/backoffice/external/uui";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import { umbOpenModal } from "@umbraco-cms/backoffice/modal";
import { UMBRICIANS_SEND_MESSAGE_MODAL } from "../modals/send-message.modal-token.js";
import { UMBRICIANS_SEND_MEMBER_MESSAGE_MODAL } from "../modals/send-member-message.modal-token.js";
import type { MemberMessaging } from "../api/live-view.resource.js";
import { LIVE_VIEW_PANEL_TYPE } from "./constants.js";
import { INTERVALS, UmbriciansLiveViewDashboardContext } from "./live-view-dashboard.context.js";
import type { OnlineTableSelection } from "./online-list.js";
// Loaded with the dashboard so the built-in panels appear together.
import "./live-view-activity.element.js";
import "./live-view-users.element.js";
import "./live-view-members.element.js";
import { plural } from "../utils/format.js";

/**
 * The dashboard: a header with the refresh controls, the panels, and a footer with the message actions. What the
 * panels share lives in the dashboard context it provides.
 */
@customElement("umbricians-live-view-dashboard")
export class UmbriciansLiveViewDashboardElement extends UmbElementMixin(LitElement) {
  @state() private _interval = 5;
  @state() private _paused = false;
  @state() private _lastUpdated?: Date;
  @state() private _error?: string;

  /** The current user's Live View messaging permissions, from their user groups. */
  @state() private _canMessageUsers = false;
  @state() private _canMessageMembers = false;

  /** The site's member message senders; member messaging is offered only when there is at least one. */
  @state() private _memberMessaging?: MemberMessaging;

  /** Who is picked in each table, while its panel is shown. */
  @state() private _usersSelection?: OnlineTableSelection;
  @state() private _membersSelection?: OnlineTableSelection;

  #context: UmbriciansLiveViewDashboardContext;
  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  constructor() {
    super();
    this.#context = new UmbriciansLiveViewDashboardContext(this);
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));

    const context = this.#context;
    this.observe(context.interval, (interval) => (this._interval = interval));
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
        .map((r) =>
          r.error
            ? `${r.name}: ${r.error.toLowerCase()}`
            : `${r.name}: ${r.delivered === null ? "sent" : plural(r.delivered, "member", "members")}${r.note ? ` (${r.note})` : ""}`,
        )
        .join(" · ");
      this.#notifications?.peek(failed.length ? "warning" : "positive", {
        data: { headline: failed.length ? "Message partly sent" : "Message sent to members", message: summary },
      });
      members.clear();
    } catch {
      // Closed without sending.
    }
  }

  async #openSendMessage() {
    const users = this._usersSelection;
    if (!users || users.summary.count === 0) return;
    const selection = users.summary;

    try {
      const { sentTo } = await umbOpenModal(this, UMBRICIANS_SEND_MESSAGE_MODAL, {
        data: { allOnline: selection.allOnline, count: selection.count, recipients: selection.people },
      });
      this.#notifications?.peek("positive", {
        data: { headline: "Message sent", message: `Sent to ${sentTo} ${sentTo === 1 ? "user" : "users"}.` },
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
      ? "Select users or members to message them"
      : canMessageUsers
        ? "Select users to send them a message"
        : canMessageMembers
          ? "Select members to send them a message"
          : "";

    return html`
      <umb-body-layout header-transparent>
        <div slot="header" class="toolbar">
          <div class="status">
            <span class="live-dot ${this._paused || this._error ? "off" : ""}"></span>
            ${this._error
              ? html`<span class="error">${this._error}</span>`
              : this._lastUpdated
                ? html`<span class="muted">Updated ${this._lastUpdated.toLocaleTimeString()}</span>`
                : html`<span class="muted">Connecting…</span>`}
          </div>
          <div class="controls">
            <uui-select
              label="Refresh interval"
              .options=${INTERVALS.map((s) => ({ name: `Every ${s}s`, value: String(s), selected: s === this._interval }))}
              @change=${this.#onIntervalChange}></uui-select>
            <uui-button
              look="secondary"
              compact
              label=${this._paused ? "Resume" : "Pause"}
              @click=${() => this.#context.togglePaused()}>
              <uui-icon name=${this._paused ? "icon-play" : "icon-pause"}></uui-icon>
            </uui-button>
            <uui-button look="secondary" compact label="Clear chart" @click=${() => this.#context.clearReadings()}>
              <uui-icon name="icon-trash"></uui-icon>
            </uui-button>
          </div>
        </div>

        <div class="content">
          <umb-extension-slot type=${LIVE_VIEW_PANEL_TYPE}></umb-extension-slot>
        </div>

        ${canMessageUsers || canMessageMembers
          ? html`<span slot="footer-info" class="muted footer-info">
              ${selected === 0 && selectedMembers === 0
                ? hint
                : [
                    selected ? `${plural(selected, "user", "users")} selected` : "",
                    selectedMembers ? `${plural(selectedMembers, "member", "members")} selected` : "",
                  ]
                    .filter(Boolean)
                    .join(" · ")}
            </span>`
          : ""}
        ${canMessageMembers
          ? html`<uui-button
              slot="actions"
              look="secondary"
              label="Message members…"
              @click=${this.#openSendMemberMessage}></uui-button>`
          : ""}
        ${canMessageUsers
          ? html`<uui-button
              slot="actions"
              look="primary"
              color="positive"
              label="Message users…"
              ?disabled=${selected === 0}
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
