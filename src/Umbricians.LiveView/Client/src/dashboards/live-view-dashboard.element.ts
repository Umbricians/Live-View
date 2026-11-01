import { LitElement, css, html, customElement, state, query, repeat } from "@umbraco-cms/backoffice/external/lit";
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import type { UUISelectEvent } from "@umbraco-cms/backoffice/external/uui";
import {
  CategoryScale,
  Chart,
  Legend,
  LineController,
  LineElement,
  LinearScale,
  PointElement,
  Tooltip,
  type ChartDataset,
} from "chart.js";
import { color } from "chart.js/helpers";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import { UMB_CURRENT_USER_CONTEXT } from "@umbraco-cms/backoffice/current-user";
import {
  LIVE_VIEW_PERMISSION_MESSAGE_MEMBERS,
  LIVE_VIEW_PERMISSION_MESSAGE_USERS,
} from "../permissions/constants.js";
import { umbOpenModal } from "@umbraco-cms/backoffice/modal";
import { UMBRICIANS_SEND_MESSAGE_MODAL } from "../modals/send-message.modal-token.js";
import { UMBRICIANS_SEND_MEMBER_MESSAGE_MODAL } from "../modals/send-member-message.modal-token.js";
import {
  fetchMemberMessaging,
  fetchSnapshot,
  type LiveMetricReading,
  type MemberMessaging,
} from "../api/live-view.resource.js";
import type { UmbriciansLiveViewUsersElement } from "./live-view-users.element.js";
import type { UmbriciansLiveViewMembersElement } from "./live-view-members.element.js";
import "./live-view-users.element.js";
import "./live-view-members.element.js";
import { plural } from "../utils/format.js";

Chart.register(LineController, LineElement, PointElement, LinearScale, CategoryScale, Tooltip, Legend);

const INTERVALS = [2, 5, 10, 15, 30];
const DEFAULT_INTERVAL = 5;
const MAX_POINTS = 120;
const STORAGE_KEY = "umbricians.liveView.interval";
const FALLBACK_COLORS = ["#3544b1", "#f79c37", "#2bc37c", "#d42054", "#8b5cf6", "#0ea5e9", "#a16207"];

type LineDataset = ChartDataset<"line", Array<number | null>> & { alias?: string };

@customElement("umbricians-live-view-dashboard")
export class UmbriciansLiveViewDashboardElement extends UmbElementMixin(LitElement) {
  @state() private _interval = readStoredInterval();
  @state() private _paused = false;
  @state() private _metrics: Array<LiveMetricReading> = [];
  @state() private _lastUpdated?: Date;
  @state() private _error?: string;
  /** Aliases of the lines switched off from their tile or legend entry, for this visit. */
  @state() private _hidden = new Set<string>();

  @state() private _selectedCount = 0;

  /** The current user's Live View messaging permissions, from their user groups. */
  @state() private _canMessageUsers = false;
  @state() private _canMessageMembers = false;
  @state() private _selectedMembers = 0;

  /** The site's member message senders; member messaging is offered only when there is at least one. */
  @state() private _memberMessaging?: MemberMessaging;

  @query("canvas") private _canvas!: HTMLCanvasElement;
  @query("umbricians-live-view-users") private _users?: UmbriciansLiveViewUsersElement;
  @query("umbricians-live-view-members") private _members?: UmbriciansLiveViewMembersElement;

  #chart?: Chart<"line", Array<number | null>, string>;
  #timer?: number;
  #abort?: AbortController;
  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));
    this.consumeContext(UMB_CURRENT_USER_CONTEXT, (context) => {
      this.observe(context?.fallbackPermissions, (permissions) => {
        this._canMessageUsers = permissions?.includes(LIVE_VIEW_PERMISSION_MESSAGE_USERS) ?? false;
        this._canMessageMembers = permissions?.includes(LIVE_VIEW_PERMISSION_MESSAGE_MEMBERS) ?? false;
      });
    });
  }

  override connectedCallback() {
    super.connectedCallback();
    this.#schedule(0);
    fetchMemberMessaging()
      .then((messaging) => (this._memberMessaging = messaging))
      .catch(() => (this._memberMessaging = undefined));
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#stop();
    this.#chart?.destroy();
    this.#chart = undefined;
  }

  override firstUpdated() {
    const styles = getComputedStyle(this);
    const text = styles.getPropertyValue("--uui-color-text").trim() || "#1b264f";
    const grid = styles.getPropertyValue("--uui-color-divider").trim() || "#e9e9eb";

    this.#chart = new Chart(this._canvas, {
      type: "line",
      data: { labels: [], datasets: [] },
      options: {
        animation: false,
        maintainAspectRatio: false,
        interaction: { mode: "index", intersect: false },
        elements: { point: { radius: 0, hoverRadius: 4 }, line: { cubicInterpolationMode: "monotone", borderWidth: 2 } },
        scales: {
          x: { ticks: { color: text, maxTicksLimit: 8, maxRotation: 0 }, grid: { color: grid } },
          y: { beginAtZero: true, ticks: { color: text, precision: 0 }, grid: { color: grid } },
        },
        plugins: {
          legend: {
            position: "bottom",
            // A legend click switches a line off like its tile does, and a switched-off entry fades like its tile
            // instead of being struck through.
            labels: {
              color: text,
              usePointStyle: true,
              generateLabels: (chart) =>
                Chart.defaults.plugins.legend.labels.generateLabels(chart).map((item) =>
                  item.hidden
                    ? {
                        ...item,
                        hidden: false,
                        fontColor: faded(text),
                        fillStyle: faded(item.fillStyle),
                        strokeStyle: faded(item.strokeStyle),
                      }
                    : item,
                ),
            },
            onClick: (_event, item) => {
              const alias = (this.#chart?.data.datasets[item.datasetIndex!] as LineDataset | undefined)?.alias;
              if (alias) this.#toggleLine(alias);
            },
          },
        },
      },
    });
  }

  #schedule(delayMs: number) {
    window.clearTimeout(this.#timer);
    if (this._paused) return;
    this.#timer = window.setTimeout(() => this.#poll(), delayMs);
  }

  #stop() {
    window.clearTimeout(this.#timer);
    this.#abort?.abort();
  }

  async #poll() {
    this.#abort = new AbortController();
    try {
      const snapshot = await fetchSnapshot(this.#abort.signal);
      this._metrics = snapshot.metrics;
      this._lastUpdated = new Date(snapshot.timestamp);
      this._error = undefined;
      this.#append(this._lastUpdated, snapshot.metrics);
    } catch (error) {
      if (this.#abort.signal.aborted) return;
      this._error = error instanceof Error ? error.message : String(error);
    }
    // Scheduled after the response, so slow responses never stack up.
    this.#schedule(this._interval * 1000);
  }

  #append(timestamp: Date, metrics: Array<LiveMetricReading>) {
    const chart = this.#chart;
    if (!chart) return;

    const labels = chart.data.labels!;
    const datasets = chart.data.datasets as Array<LineDataset>;

    for (const metric of metrics) {
      if (datasets.some((d) => d.alias === metric.alias)) continue;
      const color = this.#colorFor(metric, datasets.length);
      // Back-fill with gaps so a line that appears later still lines up with the timeline.
      datasets.push({
        alias: metric.alias,
        label: metric.name,
        data: labels.map(() => null),
        borderColor: color,
        backgroundColor: color,
        spanGaps: false,
        hidden: this._hidden.has(metric.alias),
      });
    }

    labels.push(timestamp.toLocaleTimeString());
    for (const dataset of datasets) {
      dataset.data.push(metrics.find((m) => m.alias === dataset.alias)?.value ?? null);
    }

    if (labels.length > MAX_POINTS) {
      labels.splice(0, labels.length - MAX_POINTS);
      for (const dataset of datasets) dataset.data.splice(0, dataset.data.length - MAX_POINTS);
    }

    chart.update("none");
  }

  /** Switches a line off or back on: the line leaves the chart, and its tile and legend entry fade. */
  #toggleLine(alias: string) {
    const hidden = new Set(this._hidden);
    if (!hidden.delete(alias)) hidden.add(alias);
    this._hidden = hidden;

    const chart = this.#chart;
    if (!chart) return;
    for (const dataset of chart.data.datasets as Array<LineDataset>) {
      dataset.hidden = dataset.alias !== undefined && hidden.has(dataset.alias);
    }
    chart.update("none");
  }

  #colorFor(metric: LiveMetricReading, index: number) {
    return metric.color || FALLBACK_COLORS[index % FALLBACK_COLORS.length];
  }

  #onIntervalChange(event: UUISelectEvent) {
    this._interval = Number(event.target.value) || DEFAULT_INTERVAL;
    try {
      localStorage.setItem(STORAGE_KEY, String(this._interval));
    } catch {
      // Storage may be unavailable (private mode); the choice then lasts for this visit only.
    }
    this.#stop();
    this.#schedule(0);
  }

  #togglePaused() {
    this._paused = !this._paused;
    if (this._paused) this.#stop();
    else this.#schedule(0);
  }

  #clear() {
    const chart = this.#chart;
    if (!chart) return;
    chart.data.labels = [];
    chart.data.datasets = [];
    chart.update("none");
  }

  #onUsersChange() {
    this._selectedCount = this._users?.selection.count ?? 0;
  }

  #onMembersChange() {
    this._selectedMembers = this._members?.selection.count ?? 0;
  }

  async #openSendMemberMessage() {
    const members = this._members;
    const messaging = this._memberMessaging;
    if (!members || !messaging?.senders.length) return;

    try {
      const result = await umbOpenModal(this, UMBRICIANS_SEND_MEMBER_MESSAGE_MODAL, {
        data: {
          selection: members.selection,
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
      members.clearSelection();
    } catch {
      // Closed without sending.
    }
  }

  async #openSendMessage() {
    const users = this._users;
    const selection = users?.selection;
    if (!users || !selection || selection.count === 0) return;

    try {
      const { sentTo } = await umbOpenModal(this, UMBRICIANS_SEND_MESSAGE_MODAL, {
        data: { allOnline: selection.allOnline, count: selection.count, recipients: selection.people },
      });
      this.#notifications?.peek("positive", {
        data: { headline: "Message sent", message: `Sent to ${sentTo} ${sentTo === 1 ? "user" : "users"}.` },
      });
      users.clearSelection();
    } catch {
      // Closed without sending.
    }
  }

  override render() {
    const selected = this._selectedCount;
    const selectedMembers = this._selectedMembers;
    const canMessageUsers = this._canMessageUsers;
    // Members can be messaged only with the permission and at least one delivery method set up by a developer.
    const canMessageMembers = this._canMessageMembers && (this._memberMessaging?.senders.length ?? 0) > 0;
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
              @click=${this.#togglePaused}>
              <uui-icon name=${this._paused ? "icon-play" : "icon-pause"}></uui-icon>
            </uui-button>
            <uui-button look="secondary" compact label="Clear chart" @click=${this.#clear}>
              <uui-icon name="icon-trash"></uui-icon>
            </uui-button>
          </div>
        </div>

        <div class="content">
          <uui-box headline="Live activity">
            <div class="tiles">
              ${repeat(
                this._metrics,
                (m) => m.alias,
                (m, i) => {
                  const shown = !this._hidden.has(m.alias);
                  return html`
                    <button
                      type="button"
                      class="tile"
                      style="--tile-color: ${this.#colorFor(m, i)}"
                      aria-pressed=${shown ? "true" : "false"}
                      title=${[m.error, `${shown ? "Hide" : "Show"} ${m.name} on the chart`].filter(Boolean).join("\n")}
                      @click=${() => this.#toggleLine(m.alias)}>
                      <div class="tile-value">${m.value === null ? "–" : formatValue(m.value)}</div>
                      <div class="tile-label">${m.name}</div>
                    </button>
                  `;
                },
              )}
            </div>

            <div class="chart"><canvas></canvas></div>
            <p class="muted note">
              A live snapshot only: nothing is stored, and the chart keeps the last ${MAX_POINTS} readings while this
              page is open.
            </p>
          </uui-box>

          <umbricians-live-view-users
            .interval=${this._interval}
            ?paused=${this._paused}
            ?selectable=${canMessageUsers}
            @change=${this.#onUsersChange}></umbricians-live-view-users>

          <umbricians-live-view-members
            .interval=${this._interval}
            ?paused=${this._paused}
            ?selectable=${canMessageMembers}
            @change=${this.#onMembersChange}></umbricians-live-view-members>
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

    /* minmax(0, 1fr) and min-width: 0 let the chart canvas shrink again after the window narrows. */
    .content {
      display: grid;
      grid-template-columns: minmax(0, 1fr);
      gap: var(--uui-size-layout-1);
    }

    uui-box {
      min-width: 0;
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

    .tiles {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
      gap: var(--uui-size-space-4);
      margin-bottom: var(--uui-size-space-5);
    }

    .tile {
      min-width: 0;
      border: none;
      border-left: 4px solid var(--tile-color);
      background: var(--uui-color-surface-alt);
      border-radius: var(--uui-border-radius);
      padding: var(--uui-size-space-3) var(--uui-size-space-4);
      color: inherit;
      font: inherit;
      text-align: left;
      cursor: pointer;
      transition: opacity 120ms ease;
    }

    .tile:hover {
      background: var(--uui-color-surface-emphasis);
    }

    .tile:focus-visible {
      outline: 2px solid var(--uui-color-focus);
      outline-offset: 2px;
    }

    .tile[aria-pressed="false"] {
      opacity: 0.4;
    }

    .tile-value {
      font-size: 1.75rem;
      font-weight: 700;
      line-height: 1.2;
      font-variant-numeric: tabular-nums;
    }

    .tile-label {
      color: var(--uui-color-text-alt);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .chart {
      position: relative;
      width: 100%;
      min-width: 0;
      height: clamp(220px, 40vh, 360px);
    }

    .note {
      font-size: var(--uui-type-small-size);
      margin-bottom: 0;
    }

    .footer-info {
      margin-left: var(--uui-size-layout-1);
    }
  `;
}

function readStoredInterval() {
  try {
    const stored = Number(localStorage.getItem(STORAGE_KEY));
    return INTERVALS.includes(stored) ? stored : DEFAULT_INTERVAL;
  } catch {
    return DEFAULT_INTERVAL;
  }
}

/** A legend colour at the same 40% opacity as a switched-off tile. */
function faded<T>(value: T): T {
  return typeof value === "string" ? (color(value).alpha(0.4).rgbString() as T) : value;
}

function formatValue(value: number) {
  return Number.isInteger(value) ? value.toLocaleString() : value.toLocaleString(undefined, { maximumFractionDigits: 2 });
}

export default UmbriciansLiveViewDashboardElement;

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-live-view-dashboard": UmbriciansLiveViewDashboardElement;
  }
}
