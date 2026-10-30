import { LitElement, css, html, customElement, property, state, repeat, nothing } from "@umbraco-cms/backoffice/external/lit";
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import { UmbChangeEvent } from "@umbraco-cms/backoffice/event";
import type { UUIBooleanInputEvent, UUIPaginationElement } from "@umbraco-cms/backoffice/external/uui";
import { fetchOnlineMembers, type OnlineMember } from "../api/live-view.resource.js";
import { PAGE_SIZE, formatAgo, plural } from "../utils/format.js";

/** Which members a message goes to: everyone online, or the members picked in the table. */
export interface MemberSelection {
  allOnline: boolean;
  count: number;
  members: Array<{ key: string; name: string }>;
}

/**
 * Lists members logged in on the website, one page at a time, newest arrivals first. When the site has member
 * message senders, members can be selected, across pages or all at once, and the dashboard sends to them.
 * Fires a change event whenever the selection changes.
 */
@customElement("umbricians-live-view-members")
export class UmbriciansLiveViewMembersElement extends UmbElementMixin(LitElement) {
  /** Refresh interval in seconds, shared with the chart. */
  @property({ type: Number }) interval = 5;
  @property({ type: Boolean }) paused = false;

  /** Shows the selection checkboxes; only useful when the site can deliver member messages. */
  @property({ type: Boolean }) selectable = false;

  @state() private _members: Array<OnlineMember> = [];
  @state() private _total = 0;
  @state() private _page = 1;
  @state() private _selected = new Map<string, string>();
  @state() private _allOnline = false;
  @state() private _now = Date.now();

  #timer?: number;

  get selection(): MemberSelection {
    return this._allOnline
      ? { allOnline: true, count: this._total, members: [] }
      : {
          allOnline: false,
          count: this._selected.size,
          members: [...this._selected].map(([key, name]) => ({ key, name })),
        };
  }

  /** How many members are online right now. */
  get onlineCount() {
    return this._total;
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    window.clearTimeout(this.#timer);
  }

  override updated(changed: Map<string, unknown>) {
    if ((changed.has("paused") && !this.paused) || changed.has("interval") || changed.has("_page")) this.#refresh();
    if (changed.has("_selected") || changed.has("_allOnline") || changed.has("_total")) {
      this.dispatchEvent(new UmbChangeEvent());
    }
  }

  /** Clears the selection, e.g. after a message has been sent. */
  clearSelection() {
    this._allOnline = false;
    this._selected = new Map();
  }

  async #refresh() {
    window.clearTimeout(this.#timer);
    if (this.paused || !this.isConnected) return;

    try {
      const result = await fetchOnlineMembers((this._page - 1) * PAGE_SIZE, PAGE_SIZE);
      this._members = result.items;
      this._total = result.total;
      this._now = Date.now();
      if (this._total === 0) this._allOnline = false;

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

  #toggle(member: OnlineMember, checked: boolean) {
    // Unticking someone while "all online" is on turns it into an explicit pick of this page minus them.
    const selected = this._allOnline ? new Map(this._members.map((m) => [m.key, m.name])) : new Map(this._selected);
    this._allOnline = false;
    if (checked) selected.set(member.key, member.name);
    else selected.delete(member.key);
    this._selected = selected;
  }

  #togglePage(checked: boolean) {
    const selected = new Map(this._selected);
    for (const member of this._members) {
      if (checked) selected.set(member.key, member.name);
      else selected.delete(member.key);
    }
    this._allOnline = false;
    this._selected = selected;
  }

  #selectAllOnline = () => {
    this._allOnline = true;
  };

  #clearSelection = () => this.clearSelection();

  #renderSelectionBar() {
    const pageSelected = this._members.length > 0 && this._members.every((m) => this.#isSelected(m.key));

    if (this._allOnline) {
      return html`<div class="selection-bar">
        All ${plural(this._total, "online member is", "online members are")} selected.
        <uui-button look="placeholder" compact label="Clear selection" @click=${this.#clearSelection}></uui-button>
      </div>`;
    }
    if (pageSelected && this._total > this._members.length) {
      return html`<div class="selection-bar">
        All ${plural(this._members.length, "member", "members")} on this page selected.
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
    const pages = Math.ceil(this._total / PAGE_SIZE);
    const pageSelected = this._members.length > 0 && this._members.every((m) => this.#isSelected(m.key));
    const someOnPage = !pageSelected && this._members.some((m) => this.#isSelected(m.key));

    return html`
      <uui-box headline="Members online">
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
          ? html`<p class="muted">No members are logged in on the website right now.</p>`
          : html`
              ${this.selectable ? this.#renderSelectionBar() : nothing}
              <uui-table aria-label="Members online">
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
                  <uui-table-head-cell class="type">Member type</uui-table-head-cell>
                  <uui-table-head-cell class="last-seen">Last active</uui-table-head-cell>
                </uui-table-head>
                ${repeat(
                  this._members,
                  (m) => m.key,
                  (m) => html`
                    <uui-table-row>
                      ${this.selectable
                        ? html`<uui-table-cell class="select">
                            <uui-checkbox
                              label="Select ${m.name}"
                              .checked=${this.#isSelected(m.key)}
                              @change=${(e: UUIBooleanInputEvent) => this.#toggle(m, e.target.checked)}
                              ><span></span
                            ></uui-checkbox>
                          </uui-table-cell>`
                        : nothing}
                      <uui-table-cell>
                        <div class="member">
                          <uui-avatar name=${m.name}></uui-avatar>
                          <div class="member-text">
                            <div class="member-name">${m.name}</div>
                            ${m.email ? html`<div class="muted member-email">${m.email}</div>` : nothing}
                          </div>
                        </div>
                      </uui-table-cell>
                      <uui-table-cell class="type">${m.memberType ?? ""}</uui-table-cell>
                      <uui-table-cell class="last-seen" title=${new Date(m.lastSeen).toLocaleString()}>
                        ${formatAgo(this._now - Date.parse(m.lastSeen))}
                      </uui-table-cell>
                    </uui-table-row>
                  `,
                )}
              </uui-table>
              ${pages > 1
                ? html`<uui-pagination
                    label="Members pages"
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

    .type {
      width: 160px;
    }

    .last-seen {
      width: 110px;
    }

    .member {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-3);
      min-width: 0;
    }

    .member-text {
      min-width: 0;
    }

    .member-name,
    .member-email {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .member-email {
      font-size: var(--uui-type-small-size);
    }

    uui-avatar {
      flex: none;
      font-size: 11px;
      width: 28px;
      height: 28px;
    }

    @container (max-width: 480px) {
      .type {
        display: none;
      }
    }
  `;
}

export default UmbriciansLiveViewMembersElement;

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-live-view-members": UmbriciansLiveViewMembersElement;
  }
}
