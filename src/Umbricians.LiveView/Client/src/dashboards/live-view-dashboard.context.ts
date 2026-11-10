import { UmbContextBase } from "@umbraco-cms/backoffice/class-api";
import type { UmbControllerHost } from "@umbraco-cms/backoffice/controller-api";
import { UMB_CURRENT_USER_CONTEXT } from "@umbraco-cms/backoffice/current-user";
import { UmbContextToken } from "@umbraco-cms/backoffice/context-api";
import { UmbLocalizationController } from "@umbraco-cms/backoffice/localization-api";
import {
  UmbBasicState,
  UmbBooleanState,
  UmbNumberState,
  mergeObservables,
  type Observable,
} from "@umbraco-cms/backoffice/observable-api";
import {
  LIVE_VIEW_PERMISSION_MESSAGE_MEMBERS,
  LIVE_VIEW_PERMISSION_MESSAGE_USERS,
} from "../permissions/constants.js";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import {
  fetchDashboardSettings,
  fetchMemberMessaging,
  fetchSnapshot,
  type DashboardSettings,
  type LiveViewSnapshot,
  type MemberMessaging,
} from "../api/live-view.resource.js";
import {
  UMBRICIANS_LIVE_VIEW_DASHBOARD_CONTEXT,
  type UmbriciansLiveViewDashboardContext,
} from "./live-view-dashboard.context-token.js";
import { memberMessagingAvailable } from "./member-messaging.js";
import type { OnlineTableSelection } from "./online-selection.js";
import { addReading } from "./readings.js";
import { BUILT_IN_REFRESH_SETTINGS, toInterval } from "./refresh-interval.js";
import { MAX_READINGS } from "./readings.js";

/** The package defaults, used only if the site's dashboard settings can't be read. */
const FALLBACK_SETTINGS: DashboardSettings = {
  refreshIntervals: [...BUILT_IN_REFRESH_SETTINGS.refreshIntervals],
  defaultRefreshInterval: BUILT_IN_REFRESH_SETTINGS.defaultRefreshInterval,
  maxReadings: MAX_READINGS,
};

const STORAGE_KEY = "umbricians.liveView.interval";

/** The same context as the public token, typed with everything the built-in panels and the footer use. Matched by alias. */
export const LIVE_VIEW_DASHBOARD_CONTEXT = new UmbContextToken<LiveViewDashboardContext>(
  UMBRICIANS_LIVE_VIEW_DASHBOARD_CONTEXT.contextAlias,
);

/**
 * What the Live View dashboard shares with its panels: the refresh interval and paused state every panel follows,
 * the snapshot polling, and what the footer's message actions need from the online tables. Sites' panels see only
 * the interval and paused state (`UmbriciansLiveViewDashboardContext`).
 */
export class LiveViewDashboardContext extends UmbContextBase implements UmbriciansLiveViewDashboardContext {
  readonly #settings = new UmbBasicState<DashboardSettings | undefined>(undefined);
  /** The site's dashboard settings; undefined until they are read. Nothing polls and no panel is shown before then. */
  readonly settings: Observable<DashboardSettings | undefined> = this.#settings.asObservable();

  readonly #interval = new UmbNumberState<number>(FALLBACK_SETTINGS.defaultRefreshInterval);
  /** The refresh interval in seconds. */
  readonly interval: Observable<number> = this.#interval.asObservable();

  readonly #paused = new UmbBooleanState<boolean>(false);
  /** Whether the dashboard is paused: panels should not refresh while it is. */
  readonly paused: Observable<boolean> = this.#paused.asObservable();

  readonly #snapshot = new UmbBasicState<LiveViewSnapshot | undefined>(undefined);
  readonly snapshot: Observable<LiveViewSnapshot | undefined> = this.#snapshot.asObservable();

  readonly #readings = new UmbBasicState<ReadonlyArray<LiveViewSnapshot>>([]);
  readonly readings: Observable<ReadonlyArray<LiveViewSnapshot>> = this.#readings.asObservable();

  readonly #error = new UmbBasicState<unknown>(undefined);
  /** Why the last poll failed, as thrown; undefined while polling works. The dashboard words it for the user. */
  readonly error: Observable<unknown> = this.#error.asObservable();

  readonly #canMessageUsers = new UmbBooleanState<boolean>(false);
  readonly canMessageUsers: Observable<boolean> = this.#canMessageUsers.asObservable();

  readonly #memberMessaging = new UmbBasicState<MemberMessaging | undefined>(undefined);
  readonly memberMessaging: Observable<MemberMessaging | undefined> = this.#memberMessaging.asObservable();

  readonly #mayMessageMembers = new UmbBooleanState<boolean>(false);
  readonly canMessageMembers: Observable<boolean> = mergeObservables(
    [this.#mayMessageMembers.asObservable(), this.memberMessaging],
    ([permitted, messaging]) => memberMessagingAvailable(permitted, messaging),
  );

  readonly #usersSelection = new UmbBasicState<OnlineTableSelection | undefined>(undefined);
  readonly usersSelection: Observable<OnlineTableSelection | undefined> = this.#usersSelection.asObservable();

  readonly #membersSelection = new UmbBasicState<OnlineTableSelection | undefined>(undefined);
  readonly membersSelection: Observable<OnlineTableSelection | undefined> = this.#membersSelection.asObservable();

  #timer?: number;
  #abort?: AbortController;
  #connected = false;
  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;
  readonly #localize: UmbLocalizationController;

  constructor(host: UmbControllerHost) {
    super(host, LIVE_VIEW_DASHBOARD_CONTEXT);
    this.#localize = new UmbLocalizationController(host);
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));
    this.consumeContext(UMB_CURRENT_USER_CONTEXT, (context) => {
      this.observe(context?.fallbackPermissions, (permissions) => {
        this.#canMessageUsers.setValue(permissions?.includes(LIVE_VIEW_PERMISSION_MESSAGE_USERS) ?? false);
        this.#mayMessageMembers.setValue(permissions?.includes(LIVE_VIEW_PERMISSION_MESSAGE_MEMBERS) ?? false);
      });
    });
  }

  override hostConnected() {
    super.hostConnected();
    this.#connected = true;
    if (this.#settings.getValue()) this.#schedule(0);
    else void this.#start();
    fetchMemberMessaging()
      .then((messaging) => this.#memberMessaging.setValue(messaging))
      .catch(() => this.#memberMessaging.setValue(undefined));
  }

  override hostDisconnected() {
    super.hostDisconnected();
    this.#connected = false;
    this.#stop();
  }

  /**
   * Reads the site's dashboard settings, then starts polling at the remembered interval if it is still offered. If
   * they can't be read, the built-in settings are used and a warning says so.
   */
  async #start() {
    let settings = FALLBACK_SETTINGS;
    let failed = false;
    try {
      settings = await fetchDashboardSettings();
    } catch {
      failed = true;
    }
    if (!this.#connected) return;
    if (failed) {
      this.#notifications?.peek("warning", {
        data: {
          headline: this.#localize.term("umbriciansLiveView_settingsNotLoaded"),
          message: this.#localize.term("umbriciansLiveView_settingsNotLoadedMessage"),
        },
      });
    }
    this.#settings.setValue(settings);
    this.#interval.setValue(readStoredInterval(settings));
    this.#schedule(0);
  }

  setRefreshInterval(seconds: number) {
    const settings = this.#settings.getValue();
    if (!settings) return;
    this.#interval.setValue(toInterval(seconds, settings));
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
      this.#readings.setValue(addReading(this.#readings.getValue(), snapshot, this.#settings.getValue()?.maxReadings));
      this.#error.setValue(undefined);
    } catch (error) {
      if (this.#abort.signal.aborted) return;
      this.#error.setValue(error);
    }
    // Scheduled after the response, so slow responses never stack up.
    this.#schedule(this.#interval.getValue() * 1000);
  }
}

function readStoredInterval(settings: DashboardSettings) {
  try {
    return toInterval(localStorage.getItem(STORAGE_KEY), settings);
  } catch {
    return settings.defaultRefreshInterval;
  }
}
