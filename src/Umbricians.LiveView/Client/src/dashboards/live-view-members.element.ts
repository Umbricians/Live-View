import { LitElement, css, html, customElement, property, state, repeat, nothing } from "@umbraco-cms/backoffice/external/lit";
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import type { UUIPaginationElement } from "@umbraco-cms/backoffice/external/uui";
import { fetchOnlineMembers, type OnlineMember } from "../api/live-view.resource.js";
import { PAGE_SIZE, formatAgo } from "../utils/format.js";

/**
 * Lists members logged in on the website, one page at a time, most recently active first.
 */
@customElement("umbricians-live-view-members")
export class UmbriciansLiveViewMembersElement extends UmbElementMixin(LitElement) {
  /** Refresh interval in seconds, shared with the chart. */
  @property({ type: Number }) interval = 5;
  @property({ type: Boolean }) paused = false;

  @state() private _members: Array<OnlineMember> = [];
  @state() private _total = 0;
  @state() private _page = 1;
  @state() private _now = Date.now();

  #timer?: number;

  override disconnectedCallback() {
    super.disconnectedCallback();
    window.clearTimeout(this.#timer);
  }

  override updated(changed: Map<string, unknown>) {
    if ((changed.has("paused") && !this.paused) || changed.has("interval") || changed.has("_page")) this.#refresh();
  }

  async #refresh() {
    window.clearTimeout(this.#timer);
    if (this.paused || !this.isConnected) return;

    try {
      const result = await fetchOnlineMembers((this._page - 1) * PAGE_SIZE, PAGE_SIZE);
      this._members = result.items;
      this._total = result.total;
      this._now = Date.now();

      const pages = Math.max(1, Math.ceil(this._total / PAGE_SIZE));
      if (this._page > pages) this._page = pages;
    } catch {
      // The chart shows connection errors; keep the last list.
    }

    this.#timer = window.setTimeout(() => this.#refresh(), this.interval * 1000);
  }

  override render() {
    const pages = Math.ceil(this._total / PAGE_SIZE);

    return html`
      <uui-box headline="Members online">
        <span slot="header-actions" class="muted">${this._total} online</span>

        ${this._total === 0
          ? html`<p class="muted">No members are logged in on the website right now.</p>`
          : html`
              <uui-table aria-label="Members online">
                <uui-table-head>
                  <uui-table-head-cell>Name</uui-table-head-cell>
                  <uui-table-head-cell class="type">Member type</uui-table-head-cell>
                  <uui-table-head-cell class="last-seen">Last active</uui-table-head-cell>
                </uui-table-head>
                ${repeat(
                  this._members,
                  (m) => m.key,
                  (m) => html`
                    <uui-table-row>
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

    uui-pagination {
      display: block;
      margin-top: var(--uui-size-space-4);
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
