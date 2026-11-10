import { LitElement, css, html, customElement, state, repeat, nothing } from "@umbraco-cms/backoffice/external/lit";
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import { fetchOnlineMembers, type OnlineMember } from "../api/live-view.resource.js";
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
import { LIVE_VIEW_DASHBOARD_CONTEXT } from "./live-view-dashboard.context.js";

/**
 * Lists members online, on the website or MCP, one page at a time, newest arrivals first. When anyone is on MCP,
 * each row says how its member is online. When the site has member message senders, members can be selected, across
 * pages or all at once, and the dashboard sends to them.
 * A dashboard panel, which publishes its selection to the dashboard context.
 */
@customElement("umbricians-live-view-members")
export class UmbriciansLiveViewMembersElement extends UmbElementMixin(LitElement) {
  /** Refresh interval in seconds, from the dashboard context. */
  @state() interval = 5;
  /** Whether the dashboard is paused, from the dashboard context. Starts paused so nothing is fetched before it arrives. */
  @state() paused = true;

  /** Shows the selection checkboxes; only useful when the site can deliver member messages. */
  @state() private _selectable = false;

  #context?: typeof LIVE_VIEW_DASHBOARD_CONTEXT.TYPE;

  // Every member online can be messaged.
  #list = new OnlineList<OnlineMember>(
    this,
    async (skip, take, signal) => {
      const page = await fetchOnlineMembers(skip, take, signal);
      return { ...page, recipients: page.total };
    },
    () => this.#publishSelection(),
  );

  constructor() {
    super();
    this.consumeContext(LIVE_VIEW_DASHBOARD_CONTEXT, (context) => {
      this.#context = context;
      this.observe(context?.interval, (interval) => (this.interval = interval ?? 5));
      this.observe(context?.paused, (paused) => (this.paused = paused ?? true));
      this.observe(context?.canMessageMembers, (can) => (this._selectable = can ?? false));
      this.#publishSelection();
    });
  }

  #publishSelection() {
    this.#context?.setMembersSelection(this.#list.tableSelection);
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    // A panel that is no longer shown takes its footer action with it.
    this.#context?.setMembersSelection(undefined);
  }

  override updated(changed: Map<string, unknown>) {
    if (changed.has("paused") || changed.has("interval")) this.#list.refresh();
  }

  override render() {
    const list = this.#list;
    return html`
      <uui-box headline="Members online">
        <div slot="header-actions" class="header-actions">
          <span class="muted">${list.total} online</span>
          ${this._selectable ? renderSelectAllButton(list) : nothing}
        </div>

        ${list.total === 0
          ? html`<p class="muted">No members are online right now.</p>`
          : html`
              ${this._selectable ? renderSelectionBar(list, { one: "member", many: "members" }) : nothing}
              <uui-table aria-label="Members online">
                <uui-table-head>
                  ${this._selectable ? renderPageCheckbox(list) : nothing}
                  <uui-table-head-cell>Name</uui-table-head-cell>
                  <uui-table-head-cell class="type">Member type</uui-table-head-cell>
                  <uui-table-head-cell class="last-seen">Last active</uui-table-head-cell>
                </uui-table-head>
                ${repeat(
                  list.items,
                  (m) => m.key,
                  (m) => html`
                    <uui-table-row>
                      ${this._selectable ? renderRowCheckbox(list, m) : nothing}
                      <uui-table-cell>
                        <div class="member">
                          <uui-avatar name=${m.name} img-src=${m.avatarUrl ?? nothing}></uui-avatar>
                          <div class="member-text">
                            <div class="member-name">
                              ${m.name}
                              ${renderOnlineVia(
                                list.anyOnMcp,
                                [
                                  { label: "Website", lastSeen: m.websiteLastSeen },
                                  { label: "MCP", lastSeen: m.mcpLastSeen },
                                ],
                                list.now,
                              )}
                            </div>
                            ${m.email ? html`<div class="muted member-email">${m.email}</div>` : nothing}
                          </div>
                        </div>
                      </uui-table-cell>
                      <uui-table-cell class="type">${m.memberType ?? ""}</uui-table-cell>
                      <uui-table-cell class="last-seen" title=${new Date(m.lastSeen).toLocaleString()}>
                        ${formatAgo(list.now - Date.parse(m.lastSeen))}
                      </uui-table-cell>
                    </uui-table-row>
                  `,
                )}
              </uui-table>
              ${renderPagination(list, "Members pages")}
            `}
      </uui-box>
    `;
  }

  static override styles = [
    onlineListStyles,
    css`
      .type {
        width: 160px;
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

      .member-name {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
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
    `,
  ];
}

export default UmbriciansLiveViewMembersElement;

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-live-view-members": UmbriciansLiveViewMembersElement;
  }
}
