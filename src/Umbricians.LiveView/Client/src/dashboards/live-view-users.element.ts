import { LitElement, css, html, customElement, property, state, repeat, nothing } from "@umbraco-cms/backoffice/external/lit";
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import { UmbChangeEvent } from "@umbraco-cms/backoffice/event";
import type { UUIBooleanInputEvent, UUIPaginationElement } from "@umbraco-cms/backoffice/external/uui";
import { fetchOnlineUsers, type OnlineBackofficeUser } from "../api/live-view.resource.js";
import { PAGE_SIZE, formatAgo, plural } from "../utils/format.js";

/** Who a message goes to: everyone online, or the users picked in the table. */
export interface UserSelection {
  allOnline: boolean;
  count: number;
  users: Array<{ key: string; name: string }>;
}

/**
 * Lists backoffice users who currently have the backoffice open, one page at a time, with a selection the
 * dashboard uses to send them a message. The selection survives paging, and "Select all online" covers users on
 * pages that were never loaded. Fires a change event whenever the selection changes.
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
  @state() private _page = 1;
  @state() private _selected = new Map<string, string>();
  @state() private _allOnline = false;
  @state() private _now = Date.now();

  #timer?: number;

  get selection(): UserSelection {
    return this._allOnline
      ? { allOnline: true, count: this._total, users: [] }
      : {
          allOnline: false,
          count: this._selected.size,
          users: [...this._selected].map(([key, name]) => ({ key, name })),
        };
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    window.clearTimeout(this.#timer);
  }

  override updated(changed: Map<string, unknown>) {
    if ((changed.has("paused") && !this.paused) || changed.has("interval") || changed.has("_page")) this.#refresh();
    if (changed.has("_selected") || changed.has("_allOnline") || (this._allOnline && changed.has("_total"))) {
      this.dispatchEvent(new UmbChangeEvent());
    }
  }

  async #refresh() {
    window.clearTimeout(this.#timer);
    if (this.paused || !this.isConnected) return;

    try {
      const result = await fetchOnlineUsers((this._page - 1) * PAGE_SIZE, PAGE_SIZE);
      this._users = result.items;
      this._total = result.total;
      this._now = Date.now();
      if (this._total === 0) this._allOnline = false;

      // Step back when the last page has emptied, e.g. after users went offline.
      const pages = Math.max(1, Math.ceil(this._total / PAGE_SIZE));
      if (this._page > pages) this._page = pages;
    } catch {
      // The chart shows connection errors; keep the last list.
    }

    this.#timer = window.setTimeout(() => this.#refresh(), this.interval * 1000);
  }

  #isSelected(key: string) {
    return this._allOnline || this._selected.has(key);
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
    for (const user of this._users) {
      if (checked) selected.set(user.key, user.name);
      else selected.delete(user.key);
    }
    this._allOnline = false;
    this._selected = selected;
  }

  #pageSelection() {
    return new Map(this._users.map((u) => [u.key, u.name]));
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
    const pageSelected = this._users.length > 0 && this._users.every((u) => this.#isSelected(u.key));

    if (this._allOnline) {
      return html`<div class="selection-bar">
        All ${plural(this._total, "online user is", "online users are")} selected.
        <uui-button look="placeholder" compact label="Clear selection" @click=${this.#clearSelection}></uui-button>
      </div>`;
    }
    if (pageSelected && this._total > this._users.length) {
      return html`<div class="selection-bar">
        All ${plural(this._users.length, "user", "users")} on this page selected.
        <uui-button
          look="placeholder"
          compact
          label=${`Select all ${this._total} online`}
          @click=${this.#selectAllOnline}></uui-button>
      </div>`;
    }
    return nothing;
  }

  override render() {
    const pageSelected = this._users.length > 0 && this._users.every((u) => this.#isSelected(u.key));
    const someOnPage = !pageSelected && this._users.some((u) => this.#isSelected(u.key));
    const pages = Math.ceil(this._total / PAGE_SIZE);

    return html`
      <uui-box headline="Backoffice users online">
        <div slot="header-actions" class="header-actions">
          <span class="muted">${this._total} online</span>
          ${this.selectable && this._total > 0
            ? html`<uui-button
                look="secondary"
                compact
                label=${this._allOnline ? "Clear selection" : `Select all ${this._total} online`}
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
                            <uui-checkbox
                              label="Select ${u.name}"
                              .checked=${this.#isSelected(u.key)}
                              @change=${(e: UUIBooleanInputEvent) => this.#toggle(u, e.target.checked)}
                              ><span></span
                            ></uui-checkbox>
                          </uui-table-cell>`
                        : nothing}
                      <uui-table-cell>
                        <div class="user">
                          <uui-avatar name=${u.name}></uui-avatar>
                          <div class="user-text">
                            <div class="user-name">
                              ${u.name}${u.isCurrentUser ? html` <span class="muted">(you)</span>` : nothing}
                            </div>
                            ${u.email ? html`<div class="muted user-email">${u.email}</div>` : nothing}
                          </div>
                        </div>
                      </uui-table-cell>
                      <uui-table-cell class="tabs" title=${`${plural(u.openTabs, "backoffice tab", "backoffice tabs")} open`}>
                        <span class="tab-count">${u.openTabs}</span>
                      </uui-table-cell>
                      <uui-table-cell class="last-seen" title=${new Date(u.lastSeen).toLocaleString()}>
                        ${formatAgo(this._now - Date.parse(u.lastSeen))}
                      </uui-table-cell>
                      ${this.selectable
                        ? html`<uui-table-cell class="delivery">
                        ${u.isConnected
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

    .user-name,
    .user-email {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .user-email {
      font-size: var(--uui-type-small-size);
    }

    uui-avatar {
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
