import { LitElement, css, html, customElement, state, repeat, nothing } from "@umbraco-cms/backoffice/external/lit";
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
// Registers <umb-user-avatar>, so users look as they do in the Users section.
import "@umbraco-cms/backoffice/user";
import { fetchOnlineUsers, type OnlineBackofficeUser } from "../api/live-view.resource.js";
import { formatAgo, plural } from "../utils/format.js";
import { renderOnlineVia } from "../utils/online-via.js";
import {
  OnlineList,
  onlineListStyles,
  renderPageCheckbox,
  renderPagination,
  renderRowCheckbox,
  renderSelectAllButton,
  renderSelectionBar,
} from "./online-list.js";
import { UMBRICIANS_LIVE_VIEW_DASHBOARD_CONTEXT } from "./live-view-dashboard.context-token.js";

/**
 * Lists backoffice users who currently have the backoffice open, one page at a time, with a selection the
 * dashboard uses to send them a message. The current user is listed but never selectable, since nobody messages
 * themselves, and nor are users only on MCP, since a backoffice message is shown in a backoffice tab.
 * A dashboard panel, which publishes its selection to the dashboard context.
 */
@customElement("umbricians-live-view-users")
export class UmbriciansLiveViewUsersElement extends UmbElementMixin(LitElement) {
  /** Refresh interval in seconds, from the dashboard context. */
  @state() interval = 5;
  /** Whether the dashboard is paused, from the dashboard context. Starts paused so nothing is fetched before it arrives. */
  @state() paused = true;

  /** Shows the selection checkboxes and delivery column; only for users allowed to message backoffice users. */
  @state() private _selectable = false;

  #context?: typeof UMBRICIANS_LIVE_VIEW_DASHBOARD_CONTEXT.TYPE;

  #list = new OnlineList<OnlineBackofficeUser>(
    this,
    fetchOnlineUsers,
    () => this.#publishSelection(),
    (u) => !u.isCurrentUser && u.backofficeLastSeen !== null,
  );

  constructor() {
    super();
    this.consumeContext(UMBRICIANS_LIVE_VIEW_DASHBOARD_CONTEXT, (context) => {
      this.#context = context;
      this.observe(context?.interval, (interval) => (this.interval = interval ?? 5));
      this.observe(context?.paused, (paused) => (this.paused = paused ?? true));
      this.observe(context?.canMessageUsers, (can) => (this._selectable = can ?? false));
      this.#publishSelection();
    });
  }

  #publishSelection() {
    this.#context?.setUsersSelection(this.#list.tableSelection);
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    // A panel that is no longer shown takes its footer action with it.
    this.#context?.setUsersSelection(undefined);
  }

  override updated(changed: Map<string, unknown>) {
    if (changed.has("paused") || changed.has("interval")) this.#list.refresh();
  }

  override render() {
    const list = this.#list;
    return html`
      <uui-box headline="Backoffice users online">
        <div slot="header-actions" class="header-actions">
          <span class="muted">${list.total} online</span>
          ${this._selectable ? renderSelectAllButton(list) : nothing}
        </div>

        ${list.total === 0
          ? html`<p class="muted">Nobody has the backoffice open right now.</p>`
          : html`
              ${this._selectable ? renderSelectionBar(list, { one: "user", many: "users" }) : nothing}
              <uui-table aria-label="Backoffice users online">
                <uui-table-head>
                  ${this._selectable ? renderPageCheckbox(list) : nothing}
                  <uui-table-head-cell>Name</uui-table-head-cell>
                  <uui-table-head-cell class="tabs">Open tabs</uui-table-head-cell>
                  <uui-table-head-cell class="last-seen">Last active</uui-table-head-cell>
                  ${this._selectable
                    ? html`<uui-table-head-cell class="delivery">Delivery</uui-table-head-cell>`
                    : nothing}
                </uui-table-head>
                ${repeat(
                  list.items,
                  (u) => u.key,
                  (u) => html`
                    <uui-table-row>
                      ${this._selectable ? renderRowCheckbox(list, u) : nothing}
                      <uui-table-cell>
                        <div class="user">
                          <umb-user-avatar .name=${u.name} .imgUrls=${u.avatarUrls}></umb-user-avatar>
                          <div class="user-text">
                            <div class="user-name">
                              ${u.name}
                              ${u.isCurrentUser
                                ? html`<uui-tag look="secondary" title="This is you, so you are left out of messages"
                                    >You</uui-tag
                                  >`
                                : nothing}
                              ${renderOnlineVia(
                                list.anyOnMcp,
                                [
                                  { label: "Backoffice", lastSeen: u.backofficeLastSeen },
                                  { label: "MCP", lastSeen: u.mcpLastSeen },
                                ],
                                list.now,
                              )}
                            </div>
                            ${u.email ? html`<div class="muted user-email">${u.email}</div>` : nothing}
                          </div>
                        </div>
                      </uui-table-cell>
                      <uui-table-cell
                        class="tabs"
                        title=${u.backofficeLastSeen === null ? "Only on MCP" : `${plural(u.openTabs, "backoffice tab", "backoffice tabs")} open`}>
                        ${u.backofficeLastSeen === null
                          ? html`<span class="muted">–</span>`
                          : html`<span class="tab-count">${u.openTabs}</span>`}
                      </uui-table-cell>
                      <uui-table-cell class="last-seen" title=${new Date(u.lastSeen).toLocaleString()}>
                        ${formatAgo(list.now - Date.parse(u.lastSeen))}
                      </uui-table-cell>
                      ${this._selectable
                        ? html`<uui-table-cell class="delivery">
                        ${!list.canSelect(u)
                          ? nothing
                          : u.isConnected
                          ? html`<uui-tag color="positive" look="secondary" title="Receives messages instantly"
                              >Instant</uui-tag
                            >`
                          : html`<uui-tag look="secondary" title="Receives messages with its next heartbeat"
                              >Within 1 min</uui-tag
                            >`}
                      </uui-table-cell>`
                        : nothing}
                    </uui-table-row>
                  `,
                )}
              </uui-table>
              ${renderPagination(list, "Backoffice users pages")}
            `}
      </uui-box>
    `;
  }

  static override styles = [
    onlineListStyles,
    css`
      .tabs {
        width: 90px;
        text-align: center;
      }

      .tab-count {
        font-weight: 700;
        font-variant-numeric: tabular-nums;
      }

      .delivery {
        width: 120px;
      }

      .user {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        min-width: 0;
      }

      .user-text {
        min-width: 0;
      }

      .user-name {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .user-name,
      .user-email {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }

      .user-email {
        font-size: var(--uui-type-small-size);
      }

      umb-user-avatar {
        flex: none;
        font-size: 11px;
        width: 28px;
        height: 28px;
      }

      @container (max-width: 560px) {
        .delivery {
          display: none;
        }
      }

      @container (max-width: 400px) {
        .last-seen {
          display: none;
        }
      }
    `,
  ];
}

export default UmbriciansLiveViewUsersElement;

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-live-view-users": UmbriciansLiveViewUsersElement;
  }
}
