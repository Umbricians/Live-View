import { UmbContextBase } from "@umbraco-cms/backoffice/class-api";
import type { UmbControllerHost } from "@umbraco-cms/backoffice/controller-api";
import { UMB_CURRENT_USER_CONTEXT } from "@umbraco-cms/backoffice/current-user";
import {
  UmbBasicState,
  UmbBooleanState,
  UmbNumberState,
  type Observable,
} from "@umbraco-cms/backoffice/observable-api";
import {
  LIVE_VIEW_PERMISSION_MESSAGE_MEMBERS,
  LIVE_VIEW_PERMISSION_MESSAGE_USERS,
} from "../permissions/constants.js";
import { fetchMemberMessaging, fetchSnapshot, type LiveViewSnapshot, type MemberMessaging } from "../api/live-view.resource.js";
import { UMBRICIANS_LIVE_VIEW_DASHBOARD_CONTEXT } from "./live-view-dashboard.context-token.js";
import type { OnlineTableSelection } from "./online-list.js";
import { addReading } from "./readings.js";

export const INTERVALS = [2, 5, 10, 15, 30];
const DEFAULT_INTERVAL = 5;
const STORAGE_KEY = "umbricians.liveView.interval";

/**
 * What the Live View dashboard shares with its panels: the refresh interval and paused state every panel follows,
 * the snapshot polling, and what the footer's message actions need from the online tables.
 */
export class UmbriciansLiveViewDashboardContext extends UmbContextBase {
  readonly #interval = new UmbNumberState<number>(readStoredInterval());
  /** The refresh interval in seconds. */
  readonly interval: Observable<number> = this.#interval.asObservable();

  readonly #paused = new UmbBooleanState<boolean>(false);
  /** Whether the dashboard is paused: panels should not refresh while it is. */
  readonly paused: Observable<boolean> = this.#paused.asObservable();

  readonly #snapshot = new UmbBasicState<LiveViewSnapshot | undefined>(undefined);
  readonly snapshot: Observable<LiveViewSnapshot | undefined> = this.#snapshot.asObservable();

  readonly #readings = new UmbBasicState<ReadonlyArray<LiveViewSnapshot>>([]);
  readonly readings: Observable<ReadonlyArray<LiveViewSnapshot>> = this.#readings.asObservable();

  readonly #error = new UmbBasicState<string | undefined>(undefined);
  readonly error: Observable<string | undefined> = this.#error.asObservable();

  readonly #canMessageUsers = new UmbBooleanState<boolean>(false);
  readonly canMessageUsers: Observable<boolean> = this.#canMessageUsers.asObservable();

  readonly #canMessageMembers = new UmbBooleanState<boolean>(false);
  readonly canMessageMembers: Observable<boolean> = this.#canMessageMembers.asObservable();

  readonly #memberMessaging = new UmbBasicState<MemberMessaging | undefined>(undefined);
  readonly memberMessaging: Observable<MemberMessaging | undefined> = this.#memberMessaging.asObservable();

  readonly #usersSelection = new UmbBasicState<OnlineTableSelection | undefined>(undefined);
  readonly usersSelection: Observable<OnlineTableSelection | undefined> = this.#usersSelection.asObservable();

  readonly #membersSelection = new UmbBasicState<OnlineTableSelection | undefined>(undefined);
  readonly membersSelection: Observable<OnlineTableSelection | undefined> = this.#membersSelection.asObservable();

  #mayMessageMembers = false;
  #timer?: number;
  #abort?: AbortController;

  constructor(host: UmbControllerHost) {
    super(host, UMBRICIANS_LIVE_VIEW_DASHBOARD_CONTEXT);
    this.consumeContext(UMB_CURRENT_USER_CONTEXT, (context) => {
      this.observe(context?.fallbackPermissions, (permissions) => {
        this.#canMessageUsers.setValue(permissions?.includes(LIVE_VIEW_PERMISSION_MESSAGE_USERS) ?? false);
        this.#mayMessageMembers = permissions?.includes(LIVE_VIEW_PERMISSION_MESSAGE_MEMBERS) ?? false;
        this.#updateCanMessageMembers();
      });
    });
  }

  override hostConnected() {
    super.hostConnected();
    this.#schedule(0);
    fetchMemberMessaging()
      .then((messaging) => this.#memberMessaging.setValue(messaging))
      .catch(() => this.#memberMessaging.setValue(undefined))
      .finally(() => this.#updateCanMessageMembers());
  }

  override hostDisconnected() {
    super.hostDisconnected();
    this.#stop();
  }

  setRefreshInterval(seconds: number) {
    this.#interval.setValue(seconds || DEFAULT_INTERVAL);
    try {
      localStorage.setItem(STORAGE_KEY, String(this.#interval.getValue()));
    } catch {
      // Storage may be unavailable (private mode); the choice then lasts for this visit only.
    }
    this.#stop();
    this.#schedule(0);
  }

  togglePaused() {
    this.#paused.setValue(!this.#paused.getValue());
    if (this.#paused.getValue()) this.#stop();
    else this.#schedule(0);
  }

  /** Empties the chart's readings; the latest snapshot stays, so the tiles stay. */
  clearReadings() {
    this.#readings.setValue([]);
  }

  setUsersSelection(selection: OnlineTableSelection | undefined) {
    this.#usersSelection.setValue(selection);
  }

  setMembersSelection(selection: OnlineTableSelection | undefined) {
    this.#membersSelection.setValue(selection);
  }

  /** Members can be messaged only with the permission and at least one delivery method set up by a developer. */
  #updateCanMessageMembers() {
    this.#canMessageMembers.setValue(
      this.#mayMessageMembers && (this.#memberMessaging.getValue()?.senders.length ?? 0) > 0,
    );
  }

  #schedule(delayMs: number) {
    window.clearTimeout(this.#timer);
    if (this.#paused.getValue()) return;
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
      this.#snapshot.setValue(snapshot);
      this.#readings.setValue(addReading(this.#readings.getValue(), snapshot));
      this.#error.setValue(undefined);
    } catch (error) {
      if (this.#abort.signal.aborted) return;
      this.#error.setValue(error instanceof Error ? error.message : String(error));
    }
    // Scheduled after the response, so slow responses never stack up.
    this.#schedule(this.#interval.getValue() * 1000);
  }
}

function readStoredInterval() {
  try {
    const stored = Number(localStorage.getItem(STORAGE_KEY));
    return INTERVALS.includes(stored) ? stored : DEFAULT_INTERVAL;
  } catch {
    return DEFAULT_INTERVAL;
  }
}
