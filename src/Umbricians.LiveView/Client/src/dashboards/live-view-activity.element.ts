import { LitElement, css, html, customElement, state, query, repeat } from "@umbraco-cms/backoffice/external/lit";
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
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
import type { LiveMetricReading, LiveViewSnapshot } from "../api/live-view.resource.js";
import { LIVE_VIEW_DASHBOARD_CONTEXT } from "./live-view-dashboard.context.js";
import { MAX_READINGS, readingsAfter } from "./readings.js";

Chart.register(LineController, LineElement, PointElement, LinearScale, CategoryScale, Tooltip, Legend);

const FALLBACK_COLORS = ["#3544b1", "#f79c37", "#2bc37c", "#d42054", "#8b5cf6", "#0ea5e9", "#a16207"];

type LineDataset = ChartDataset<"line", Array<number | null>> & { alias?: string };

/**
 * The Live activity panel: a tile for each metric's latest value and a chart of the readings the dashboard context
 * keeps. A tile or legend entry switches its line off and on.
 */
@customElement("umbricians-live-view-activity")
export class UmbriciansLiveViewActivityElement extends UmbElementMixin(LitElement) {
  @state() private _metrics: Array<LiveMetricReading> = [];
  /** Aliases of the lines switched off from their tile or legend entry, for this visit. */
  @state() private _hidden = new Set<string>();

  @query("canvas") private _canvas!: HTMLCanvasElement;

  #chart?: Chart<"line", Array<number | null>, string>;
  #readings: ReadonlyArray<LiveViewSnapshot> = [];
  /** The newest reading drawn on the chart. */
  #shown?: LiveViewSnapshot;

  constructor() {
    super();
    this.consumeContext(LIVE_VIEW_DASHBOARD_CONTEXT, (context) => {
      this.observe(context?.snapshot, (snapshot) => (this._metrics = snapshot?.metrics ?? []));
      this.observe(context?.readings, (readings) => {
        this.#readings = readings ?? [];
        this.#sync();
      });
    });
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
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

    // Readings that arrived before the chart existed.
    this.#sync();
  }

  /** Draws the readings the chart has not shown yet, or empties the chart when the readings were cleared. */
  #sync() {
    const chart = this.#chart;
    if (!chart) return;

    const readings = this.#readings;
    if (readings.length === 0) {
      chart.data.labels = [];
      chart.data.datasets = [];
      chart.update("none");
      this.#shown = undefined;
      return;
    }

    for (const reading of readingsAfter(readings, this.#shown)) this.#append(new Date(reading.timestamp), reading.metrics);
    this.#shown = readings[readings.length - 1];
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

    if (labels.length > MAX_READINGS) {
      labels.splice(0, labels.length - MAX_READINGS);
      for (const dataset of datasets) dataset.data.splice(0, dataset.data.length - MAX_READINGS);
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

  override render() {
    return html`
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
          A live snapshot only: nothing is stored, and the chart keeps the last ${MAX_READINGS} readings while this
          page is open.
        </p>
      </uui-box>
    `;
  }

  static override styles = css`
    :host {
      display: block;
      min-width: 0;
    }

    /* min-width: 0 lets the chart canvas shrink again after the window narrows. */
    uui-box {
      min-width: 0;
    }

    .muted {
      color: var(--uui-color-text-alt);
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
  `;
}

/** A legend colour at the same 40% opacity as a switched-off tile. */
function faded<T>(value: T): T {
  return typeof value === "string" ? (color(value).alpha(0.4).rgbString() as T) : value;
}

function formatValue(value: number) {
  return Number.isInteger(value) ? value.toLocaleString() : value.toLocaleString(undefined, { maximumFractionDigits: 2 });
}

export default UmbriciansLiveViewActivityElement;

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-live-view-activity": UmbriciansLiveViewActivityElement;
  }
}
