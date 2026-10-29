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
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import { umbOpenModal } from "@umbraco-cms/backoffice/modal";
import { UMBRICIANS_SEND_MESSAGE_MODAL } from "../modals/send-message.modal-token.js";
import { fetchSnapshot, type LiveMetricReading } from "../api/live-view.resource.js";
import type { UmbriciansLiveViewUsersElement } from "./live-view-users.element.js";
import "./live-view-users.element.js";
import "./live-view-members.element.js";
import { plural } from "../utils/format.js";

Chart.register(LineController, LineElement, PointElement, LinearScale, CategoryScale, Tooltip, Legend);

const INTERVALS = [2, 5, 10, 15, 30];
const DEFAULT_INTERVAL = 5;
const MAX_POINTS = 120;
const STORAGE_KEY = "umbricians.liveView.interval";
const FALLBACK_COLORS = ["#3544b1", "#f79c37", "#2bc37c", "#d42054", "#8b5cf6", "#0ea5e9", "#a16207"];

type LineDataset = ChartDataset<"line", Array<number | null>>;

@customElement("umbricians-live-view-dashboard")
export class UmbriciansLiveViewDashboardElement extends UmbElementMixin(LitElement) {
  @state() private _interval = readStoredInterval();
  @state() private _paused = false;
  @state() private _metrics: Array<LiveMetricReading> = [];
  @state() private _lastUpdated?: Date;
  @state() private _error?: string;

  @state() private _selectedCount = 0;

  @query("canvas") private _canvas!: HTMLCanvasElement;
  @query("umbricians-live-view-users") private _users?: UmbriciansLiveViewUsersElement;

  #chart?: Chart<"line", Array<number | null>, string>;
  #timer?: number;
  #abort?: AbortController;
  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));
  }

  override connectedCallback() {
    super.connectedCallback();
    this.#schedule(0);
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
        plugins: { legend: { position: "bottom", labels: { color: text, usePointStyle: true } } },
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
    const datasets = chart.data.datasets as Array<LineDataset & { alias?: string }>;

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

  async #openSendMessage() {
    const selection = this._users?.selection;
    if (!selection || selection.count === 0) return;

    try {
      const { sentTo } = await umbOpenModal(this, UMBRICIANS_SEND_MESSAGE_MODAL, {
        data: { allOnline: selection.allOnline, count: selection.count, recipients: selection.users },
      });
      this.#notifications?.peek("positive", {
        data: { headline: "Message sent", message: `Sent to ${sentTo} ${sentTo === 1 ? "user" : "users"}.` },
      });
    } catch {
      // Closed without sending.
    }
  }

  override render() {
    const selected = this._selectedCount;

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
                (m, i) => html`
                  <div class="tile" style="--tile-color: ${this.#colorFor(m, i)}" title=${m.error ?? ""}>
                    <div class="tile-value">${m.value === null ? "–" : formatValue(m.value)}</div>
                    <div class="tile-label">${m.name}</div>
                  </div>
                `,
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
            @change=${this.#onUsersChange}></umbricians-live-view-users>

          <umbricians-live-view-members
            .interval=${this._interval}
            ?paused=${this._paused}></umbricians-live-view-members>
        </div>

        <span slot="footer-info" class="muted footer-info">
          ${selected === 0
            ? "Select users to send them a message"
            : `${plural(selected, "user", "users")} selected`}
        </span>
        <uui-button
          slot="actions"
          look="primary"
          color="positive"
          label="Send message…"
          ?disabled=${selected === 0}
          @click=${this.#openSendMessage}></uui-button>
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
      border-left: 4px solid var(--tile-color);
      background: var(--uui-color-surface-alt);
      border-radius: var(--uui-border-radius);
      padding: var(--uui-size-space-3) var(--uui-size-space-4);
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

function formatValue(value: number) {
  return Number.isInteger(value) ? value.toLocaleString() : value.toLocaleString(undefined, { maximumFractionDigits: 2 });
}

export default UmbriciansLiveViewDashboardElement;

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-live-view-dashboard": UmbriciansLiveViewDashboardElement;
  }
}
