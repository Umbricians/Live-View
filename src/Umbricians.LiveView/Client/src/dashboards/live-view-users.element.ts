import { LitElement, css, html, customElement, property, state, repeat, nothing } from "@umbraco-cms/backoffice/external/lit";
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import { UmbChangeEvent } from "@umbraco-cms/backoffice/event";
// Registers <umb-user-avatar>, so users look as they do in the Users section.
import "@umbraco-cms/backoffice/user";
import type { UUIBooleanInputEvent, UUIPaginationElement } from "@umbraco-cms/backoffice/external/uui";
import { fetchOnlineUsers, type OnlineBackofficeUser } from "../api/live-view.resource.js";
import { PAGE_SIZE, formatAgo, plural } from "../utils/format.js";
import { renderOnlineVia } from "../utils/online-via.js";

/** Who a message goes to: everyone online, or the users picked in the table. */
export interface UserSelection {
  allOnline: boolean;
  count: number;
  users: Array<{ key: string; name: string }>;
}

/**
 * Lists backoffice users who currently have the backoffice open, one page at a time, with a selection the
 * dashboard uses to send them a message. The selection survives paging, and "Select all online" covers users on
 * pages that were never loaded. The current user is listed but never selectable, since nobody messages themselves.
 * Fires a change event whenever the selection changes.
 */
@customElement("umbricians-live-view-users")
export class UmbriciansLiveViewUsersElement extends UmbElementMixin(LitElement) {
  /** Refresh interval in seconds, shared with the chart. */
  @property({ type: Number }) interval = 5;
  @property({ type: Boolean }) paused = false;

  /** Shows the selection checkboxes and delivery column; only for users allowed to message backoffice users. */
  @property({ type: Boolean }) selectable = false;

  @state() private _users: Array<OnlineBackofficeUser> = [];
  @state() private _total = 0;
  @state() private _anyOnMcp = false;
  @state() private _recipients = 0;
  @state() private _page = 1;
  @state() private _selected = new Map<string, string>();
  @state() private _allOnline = false;
  @state() private _now = Date.now();

  #timer?: number;
  #abort?: AbortController;

  get selection(): UserSelection {
    return this._allOnline
      ? { allOnline: true, count: this._recipients, users: [] }
      : {
          allOnline: false,
          count: this._selected.size,
          users: [...this._selected].map(([key, name]) => ({ key, name })),
        };
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    window.clearTimeout(this.#timer);
    this.#abort?.abort();
  }

  override updated(changed: Map<string, unknown>) {
    if (changed.has("paused") || changed.has("interval") || changed.has("_page")) this.#refresh();
    if (changed.has("_selected") || changed.has("_allOnline") || (this._allOnline && changed.has("_recipients"))) {
      this.dispatchEvent(new UmbChangeEvent());
    }
  }

  /** Each call supersedes the last: an older response is dropped and only one timer is ever pending. */
  async #refresh() {
    window.clearTimeout(this.#timer);
    this.#abort?.abort();
    if (this.paused || !this.isConnected) return;

    const abort = (this.#abort = new AbortController());
    try {
      const result = await fetchOnlineUsers((this._page - 1) * PAGE_SIZE, PAGE_SIZE, abort.signal);
      if (abort.signal.aborted) return;
      this._users = result.items;
      this._total = result.total;
      this._anyOnMcp = result.anyOnMcp;
      this._recipients = result.recipients;
      this._now = Date.now();
      if (this._recipients === 0) this._allOnline = false;

      // Step back when the last page has emptied, e.g. after users went offline.
      const pages = Math.max(1, Math.ceil(this._total / PAGE_SIZE));
      if (this._page > pages) this._page = pages;
    } catch {
      if (abort.signal.aborted) return;
      // The chart shows connection errors; keep the last list.
    }

    this.#timer = window.setTimeout(() => this.#refresh(), this.interval * 1000);
  }

  /** Users on this page who can be messaged: everyone but the current user. */
  // Users only on MCP can't be messaged: a backoffice message is shown in a backoffice tab.
  get #selectableUsers() {
    return this._users.filter((u) => !u.isCurrentUser && u.backofficeLastSeen !== null);
  }

  #isSelected(user: OnlineBackofficeUser) {
    return !user.isCurrentUser && user.backofficeLastSeen !== null && (this._allOnline || this._selected.has(user.key));
  }

  #toggle(user: OnlineBackofficeUser, checked: boolean) {
    // Unticking someone while "all online" is on turns it into an explicit pick of this page minus them.
    const selected = this._allOnline ? this.#pageSelection() : new Map(this._selected);
    this._allOnline = false;
    if (checked) selected.set(user.key, user.name);
    else selected.delete(user.key);
    this._selected = selected;
  }

  #togglePage(checked: boolean) {
    const selected = new Map(this._selected);
    for (const user of this.#selectableUsers) {
      if (checked) selected.set(user.key, user.name);
      else selected.delete(user.key);
    }
    this._allOnline = false;
    this._selected = selected;
  }

  #pageSelection() {
    return new Map(this.#selectableUsers.map((u) => [u.key, u.name]));
  }

  #selectAllOnline() {
    this._allOnline = true;
  }

  /** Clears the selection, e.g. after a message has been sent. */
  clearSelection() {
    this._allOnline = false;
    this._selected = new Map();
  }

  #clearSelection() {
    this.clearSelection();
  }

  #renderSelectionBar() {
    const selectable = this.#selectableUsers;
    const pageSelected = selectable.length > 0 && selectable.every((u) => this.#isSelected(u));

    if (this._allOnline) {
      return html`<div class="selection-bar">
        All ${plural(this._recipients, "online user is", "online users are")} selected.
        <uui-button look="placeholder" compact label="Clear selection" @click=${this.#clearSelection}></uui-button>
      </div>`;
    }
    if (pageSelected && this._recipients > selectable.length) {
      return html`<div class="selection-bar">
        All ${plural(selectable.length, "user", "users")} on this page selected.
        <uui-button
          look="placeholder"
          compact
          label=${`Select all ${this._recipients} online`}
          @click=${this.#selectAllOnline}></uui-button>
      </div>`;
    }
    return nothing;
  }

  override render() {
    const selectable = this.#selectableUsers;
    const pageSelected = selectable.length > 0 && selectable.every((u) => this.#isSelected(u));
    const someOnPage = !pageSelected && selectable.some((u) => this.#isSelected(u));
    const pages = Math.ceil(this._total / PAGE_SIZE);

    return html`
      <uui-box headline="Backoffice users online">
        <div slot="header-actions" class="header-actions">
          <span class="muted">${this._total} online</span>
          ${this.selectable && this._recipients > 0
            ? html`<uui-button
                look="secondary"
                compact
                label=${this._allOnline ? "Clear selection" : `Select all ${this._recipients} online`}
                @click=${this._allOnline ? this.#clearSelection : this.#selectAllOnline}></uui-button>`
            : nothing}
        </div>

        ${this._total === 0
          ? html`<p class="muted">Nobody has the backoffice open right now.</p>`
          : html`
              ${this.selectable ? this.#renderSelectionBar() : nothing}
              <uui-table aria-label="Backoffice users online">
                <uui-table-head>
                  ${this.selectable
                    ? html`<uui-table-head-cell class="select">
                    <!-- An empty slotted span replaces the visible label; the label attribute still names it. -->
                    <uui-checkbox
                      label="Select everyone on this page"
                      ?disabled=${selectable.length === 0}
                      .checked=${pageSelected}
                      .indeterminate=${someOnPage}
                      @change=${(e: UUIBooleanInputEvent) => this.#togglePage(e.target.checked)}
                      ><span></span
                    ></uui-checkbox>
                  </uui-table-head-cell>`
                    : nothing}
                  <uui-table-head-cell>Name</uui-table-head-cell>
                  <uui-table-head-cell class="tabs">Open tabs</uui-table-head-cell>
                  <uui-table-head-cell class="last-seen">Last active</uui-table-head-cell>
                  ${this.selectable
                    ? html`<uui-table-head-cell class="delivery">Delivery</uui-table-head-cell>`
                    : nothing}
                </uui-table-head>
                ${repeat(
                  this._users,
                  (u) => u.key,
                  (u) => html`
                    <uui-table-row>
                      ${this.selectable
                        ? html`<uui-table-cell class="select">
                            ${u.isCurrentUser || u.backofficeLastSeen === null
                              ? nothing
                              : html`<uui-checkbox
                                  label="Select ${u.name}"
                                  .checked=${this.#isSelected(u)}
                                  @change=${(e: UUIBooleanInputEvent) => this.#toggle(u, e.target.checked)}
                                  ><span></span
                                ></uui-checkbox>`}
                          </uui-table-cell>`
                        : nothing}
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
                                this._anyOnMcp,
                                [
                                  { label: "Backoffice", lastSeen: u.backofficeLastSeen },
                                  { label: "MCP", lastSeen: u.mcpLastSeen },
                                ],
                                this._now,
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
                        ${formatAgo(this._now - Date.parse(u.lastSeen))}
                      </uui-table-cell>
                      ${this.selectable
                        ? html`<uui-table-cell class="delivery">
                        ${u.isCurrentUser || u.backofficeLastSeen === null
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
              ${pages > 1
                ? html`<uui-pagination
                    label="Backoffice users pages"
                    .total=${pages}
                    .current=${this._page}
                    @change=${(e: Event) => (this._page = (e.target as UUIPaginationElement).current)}></uui-pagination>`
                : nothing}
            `}
      </uui-box>
    `;
  }

  static override styles = css`
    :host {
      display: block;
      min-width: 0;
      container-type: inline-size;
    }

    .muted {
      color: var(--uui-color-text-alt);
    }

    p.muted {
      margin: 0;
    }

    .header-actions {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-3);
    }

    .selection-bar {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: center;
      gap: var(--uui-size-space-2);
      padding: var(--uui-size-space-2) var(--uui-size-space-4);
      margin-bottom: var(--uui-size-space-3);
      background: var(--uui-color-surface-alt);
      border-radius: var(--uui-border-radius);
    }

    uui-pagination {
      display: block;
      margin-top: var(--uui-size-space-4);
    }

    .select {
      width: 40px;
    }

    .tabs {
      width: 90px;
      text-align: center;
    }

    .tab-count {
      font-weight: 700;
      font-variant-numeric: tabular-nums;
    }

    .last-seen {
      width: 110px;
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
  `;
}

export default UmbriciansLiveViewUsersElement;

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-live-view-users": UmbriciansLiveViewUsersElement;
  }
}
