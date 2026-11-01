import { css, html, nothing } from "@umbraco-cms/backoffice/external/lit";
import type { ReactiveController, ReactiveControllerHost } from "@umbraco-cms/backoffice/external/lit";
import { UmbChangeEvent } from "@umbraco-cms/backoffice/event";
import type { UUIBooleanInputEvent, UUIPaginationElement } from "@umbraco-cms/backoffice/external/uui";
import { PAGE_SIZE, plural } from "../utils/format.js";
import { Selection, type Person, type SelectionSummary } from "./online-selection.js";

/** One page of people online, and how many are online in all. */
export interface OnlinePage<T> {
  items: Array<T>;
  total: number;
  anyOnMcp: boolean;
  /** How many people online can be messaged, which "Select all online" reaches. */
  recipients: number;
}

/** A table that lists people online: refreshed on its own timer, paused with the chart. */
type Host = ReactiveControllerHost & HTMLElement & { interval: number; paused: boolean };

/**
 * One online table's list: the page shown, refreshed every `interval` seconds while not paused, and who is picked in
 * it. Each refresh supersedes the last, so an older response never replaces a newer page. Fires a change event on the
 * host when who the selection reaches changes.
 */
export class OnlineList<T extends Person> implements ReactiveController {
  items: Array<T> = [];
  total = 0;
  anyOnMcp = false;
  recipients = 0;
  page = 1;
  /** When the page was read, so "last active" times are measured from it. */
  now = Date.now();
  selection = Selection.none;

  #host: Host;
  #fetchPage: (skip: number, take: number, signal: AbortSignal) => Promise<OnlinePage<T>>;
  #canSelect: (item: T) => boolean;
  #timer?: number;
  #abort?: AbortController;

  constructor(
    host: Host,
    fetchPage: (skip: number, take: number, signal: AbortSignal) => Promise<OnlinePage<T>>,
    canSelect: (item: T) => boolean = () => true,
  ) {
    this.#host = host;
    this.#fetchPage = fetchPage;
    this.#canSelect = canSelect;
    host.addController(this);
  }

  hostDisconnected() {
    window.clearTimeout(this.#timer);
    this.#abort?.abort();
  }

  /** Who the selection reaches. */
  get summary(): SelectionSummary {
    return this.selection.summary(this.recipients);
  }

  get pages() {
    return Math.ceil(this.total / PAGE_SIZE);
  }

  /** People on this page who can be picked. */
  get pickable() {
    return this.items.filter(this.#canSelect);
  }

  canSelect(item: T) {
    return this.#canSelect(item);
  }

  goTo(page: number) {
    this.page = page;
    this.refresh();
  }

  /** Reads the page now, then keeps reading it every `interval` seconds while the host is shown and not paused. */
  async refresh() {
    window.clearTimeout(this.#timer);
    this.#abort?.abort();
    if (this.#host.paused || !this.#host.isConnected) return;

    const abort = (this.#abort = new AbortController());
    const before = this.summary;
    try {
      const result = await this.#fetchPage((this.page - 1) * PAGE_SIZE, PAGE_SIZE, abort.signal);
      if (abort.signal.aborted) return;
      this.items = result.items;
      this.total = result.total;
      this.anyOnMcp = result.anyOnMcp;
      this.recipients = result.recipients;
      this.now = Date.now();
      if (this.recipients === 0) this.selection = this.selection.allOnline ? Selection.none : this.selection;

      // Step back when the last page has emptied, e.g. after people went offline.
      const last = Math.max(1, this.pages);
      if (this.page > last) {
        this.page = last;
        this.refresh();
      }

      this.#host.requestUpdate();
      if (this.#reachChanged(before)) this.#changed();
    } catch {
      if (abort.signal.aborted) return;
      // The chart shows connection errors; keep the last list.
    }

    if (abort === this.#abort) this.#timer = window.setTimeout(() => this.refresh(), this.#host.interval * 1000);
  }

  toggle(item: T, checked: boolean) {
    this.#select(this.selection.toggle(item, checked, this.pickable));
  }

  togglePage(checked: boolean) {
    this.#select(this.selection.togglePage(this.pickable, checked));
  }

  selectAllOnline = () => this.#select(this.selection.selectAllOnline());

  /** Clears the selection, e.g. after a message has been sent. */
  clearSelection = () => this.#select(Selection.none);

  #select(selection: Selection) {
    this.selection = selection;
    this.#host.requestUpdate();
    this.#changed();
  }

  #reachChanged(before: SelectionSummary) {
    const after = this.summary;
    return after.allOnline !== before.allOnline || after.count !== before.count;
  }

  #changed() {
    this.#host.dispatchEvent(new UmbChangeEvent());
  }
}

/** The header's "Select all N online" or "Clear selection" button. */
export function renderSelectAllButton<T extends Person>(list: OnlineList<T>) {
  if (list.recipients === 0) return nothing;
  return html`<uui-button
    look="secondary"
    compact
    label=${list.selection.allOnline ? "Clear selection" : `Select all ${list.recipients} online`}
    @click=${list.selection.allOnline ? list.clearSelection : list.selectAllOnline}></uui-button>`;
}

/**
 * The bar above the table saying everyone online is picked, or offering to pick them once the whole page is.
 * `nouns` names the people: "user" and "users", say.
 */
export function renderSelectionBar<T extends Person>(list: OnlineList<T>, nouns: { one: string; many: string }) {
  const pickable = list.pickable;
  if (list.selection.allOnline) {
    return html`<div class="selection-bar">
      All ${plural(list.recipients, `online ${nouns.one} is`, `online ${nouns.many} are`)} selected.
      <uui-button look="placeholder" compact label="Clear selection" @click=${list.clearSelection}></uui-button>
    </div>`;
  }
  if (list.selection.pageState(pickable) === "all" && list.recipients > pickable.length) {
    return html`<div class="selection-bar">
      All ${plural(pickable.length, nouns.one, nouns.many)} on this page selected.
      <uui-button
        look="placeholder"
        compact
        label=${`Select all ${list.recipients} online`}
        @click=${list.selectAllOnline}></uui-button>
    </div>`;
  }
  return nothing;
}

/** The header cell's checkbox, picking everyone on the page. */
export function renderPageCheckbox<T extends Person>(list: OnlineList<T>) {
  const pickable = list.pickable;
  const state = list.selection.pageState(pickable);
  return html`<uui-table-head-cell class="select">
    <!-- An empty slotted span replaces the visible label; the label attribute still names it. -->
    <uui-checkbox
      label="Select everyone on this page"
      ?disabled=${pickable.length === 0}
      .checked=${state === "all"}
      .indeterminate=${state === "some"}
      @change=${(e: UUIBooleanInputEvent) => list.togglePage(e.target.checked)}
      ><span></span
    ></uui-checkbox>
  </uui-table-head-cell>`;
}

/** A row's checkbox cell, empty for someone who can't be picked. */
export function renderRowCheckbox<T extends Person>(list: OnlineList<T>, item: T) {
  return html`<uui-table-cell class="select">
    ${list.canSelect(item)
      ? html`<uui-checkbox
          label="Select ${item.name}"
          .checked=${list.selection.isSelected(item.key)}
          @change=${(e: UUIBooleanInputEvent) => list.toggle(item, e.target.checked)}
          ><span></span
        ></uui-checkbox>`
      : nothing}
  </uui-table-cell>`;
}

export function renderPagination<T extends Person>(list: OnlineList<T>, label: string) {
  if (list.pages <= 1) return nothing;
  return html`<uui-pagination
    label=${label}
    .total=${list.pages}
    .current=${list.page}
    @change=${(e: Event) => list.goTo((e.target as UUIPaginationElement).current)}></uui-pagination>`;
}

/** Styles the shared pieces above need in the table's shadow root. */
export const onlineListStyles = css`
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

  .last-seen {
    width: 110px;
  }
`;
