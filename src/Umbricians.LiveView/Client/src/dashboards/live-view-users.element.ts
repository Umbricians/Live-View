import { LitElement, css, html, customElement, property, repeat, nothing } from "@umbraco-cms/backoffice/external/lit";
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
// Registers <umb-user-avatar>, so users look as they do in the Users section.
import "@umbraco-cms/backoffice/user";
import { fetchOnlineUsers, type OnlineBackofficeUser } from "../api/live-view.resource.js";
import { formatAgo } from "../utils/format.js";
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
import type { SelectionSummary } from "./online-selection.js";

/**
 * Lists backoffice users who currently have the backoffice open, one page at a time, with a selection the
 * dashboard uses to send them a message. The current user is listed but never selectable, since nobody messages
 * themselves, and nor are users only on MCP, since a backoffice message is shown in a backoffice tab.
 * Fires a change event whenever who the selection reaches changes.
 */
@customElement("umbricians-live-view-users")
export class UmbriciansLiveViewUsersElement extends UmbElementMixin(LitElement) {
  /** Refresh interval in seconds, shared with the chart. */
  @property({ type: Number }) interval = 5;
  @property({ type: Boolean }) paused = false;

  /** Shows the selection checkboxes and delivery column; only for users allowed to message backoffice users. */
  @property({ type: Boolean }) selectable = false;

  #list = new OnlineList<OnlineBackofficeUser>(
    this,
    fetchOnlineUsers,
    (u) => !u.isCurrentUser && u.backofficeLastSeen !== null,
  );

  get selection(): SelectionSummary {
    return this.#list.summary;
  }

  /** Clears the selection, e.g. after a message has been sent. */
  clearSelection() {
    this.#list.clearSelection();
  }

  override updated(changed: Map<string, unknown>) {
    if (changed.has("paused") || changed.has("interval")) this.#list.refresh();
  }

  override render() {
    const list = this.#list;
    return html`
      <uui-box headline=${this.localize.term("umbriciansLiveView_backofficeUsersOnline")}>
        <div slot="header-actions" class="header-actions">
          <span class="muted">${this.localize.term("umbriciansLiveView_onlineCount", list.total)}</span>
          ${this.selectable ? renderSelectAllButton(list) : nothing}
        </div>

        ${list.total === 0
          ? html`<p class="muted">${this.localize.term("umbriciansLiveView_nobodyInBackoffice")}</p>`
          : html`
              ${this.selectable ? renderSelectionBar(list, {
                    allOnline: "umbriciansLiveView_allOnlineUsersSelected",
                    allOnPage: "umbriciansLiveView_allUsersOnPageSelected",
                  }) : nothing}
              <uui-table aria-label=${this.localize.term("umbriciansLiveView_backofficeUsersOnline")}>
                <uui-table-head>
                  ${this.selectable ? renderPageCheckbox(list) : nothing}
                  <uui-table-head-cell>${this.localize.term("general_name")}</uui-table-head-cell>
                  <uui-table-head-cell class="tabs">${this.localize.term("umbriciansLiveView_openTabs")}</uui-table-head-cell>
                  <uui-table-head-cell class="last-seen">${this.localize.term("umbriciansLiveView_lastActive")}</uui-table-head-cell>
                  ${this.selectable
                    ? html`<uui-table-head-cell class="delivery">${this.localize.term("umbriciansLiveView_delivery")}</uui-table-head-cell>`
                    : nothing}
                </uui-table-head>
                ${repeat(
                  list.items,
                  (u) => u.key,
                  (u) => html`
                    <uui-table-row>
                      ${this.selectable ? renderRowCheckbox(list, u) : nothing}
                      <uui-table-cell>
                        <div class="user">
                          <umb-user-avatar .name=${u.name} .imgUrls=${u.avatarUrls}></umb-user-avatar>
                          <div class="user-text">
                            <div class="user-name">
                              ${u.name}
                              ${u.isCurrentUser
                                ? html`<uui-tag look="secondary" title=${this.localize.term("umbriciansLiveView_youTitle")}
                                    >${this.localize.term("umbriciansLiveView_you")}</uui-tag
                                  >`
                                : nothing}
                              ${renderOnlineVia(
                                this.localize,
                                list.anyOnMcp,
                                [
                                  { label: this.localize.term("umbriciansLiveView_viaBackoffice"), lastSeen: u.backofficeLastSeen },
                                  { label: this.localize.term("umbriciansLiveView_viaMcp"), lastSeen: u.mcpLastSeen },
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
                        title=${u.backofficeLastSeen === null
                          ? this.localize.term("umbriciansLiveView_onlyOnMcp")
                          : this.localize.term("umbriciansLiveView_backofficeTabsOpen", u.openTabs)}>
                        ${u.backofficeLastSeen === null
                          ? html`<span class="muted">–</span>`
                          : html`<span class="tab-count">${u.openTabs}</span>`}
                      </uui-table-cell>
                      <uui-table-cell class="last-seen" title=${this.localize.dateTime(u.lastSeen)}>
                        ${formatAgo(this.localize, list.now - Date.parse(u.lastSeen))}
                      </uui-table-cell>
                      ${this.selectable
                        ? html`<uui-table-cell class="delivery">
                        ${!list.canSelect(u)
                          ? nothing
                          : u.isConnected
                          ? html`<uui-tag color="positive" look="secondary" title=${this.localize.term("umbriciansLiveView_instantTitle")}
                              >${this.localize.term("umbriciansLiveView_instant")}</uui-tag
                            >`
                          : html`<uui-tag look="secondary" title=${this.localize.term("umbriciansLiveView_withinMinuteTitle")}
                              >${this.localize.term("umbriciansLiveView_withinMinute")}</uui-tag
                            >`}
                      </uui-table-cell>`
                        : nothing}
                    </uui-table-row>
                  `,
                )}
              </uui-table>
              ${renderPagination(list, this.localize.term("umbriciansLiveView_backofficeUsersPages"))}
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
