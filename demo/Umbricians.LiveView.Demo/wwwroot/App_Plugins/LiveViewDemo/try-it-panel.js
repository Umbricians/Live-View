import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { html, css } from "@umbraco-cms/backoffice/external/lit";
import { UMBRICIANS_LIVE_VIEW_DASHBOARD_CONTEXT } from "@umbricians/live-view";

// A dashboard panel written without a build step: it follows the dashboard's refresh interval and paused state.
export default class LiveViewDemoTryItPanel extends UmbLitElement {
  // Reactive properties are declared here, and their values set in the constructor: a class field would shadow
  // Lit's accessor.
  static properties = { _now: { state: true } };

  #interval = 5;
  #paused = true;
  #timer;

  constructor() {
    super();
    this._now = undefined;
    this.consumeContext(UMBRICIANS_LIVE_VIEW_DASHBOARD_CONTEXT, (context) => {
      this.observe(context?.interval, (interval) => {
        this.#interval = interval ?? 5;
        this.#tick();
      });
      this.observe(context?.paused, (paused) => {
        this.#paused = paused ?? true;
        this.#tick();
      });
    });
  }

  disconnectedCallback() {
    super.disconnectedCallback();
    clearTimeout(this.#timer);
  }

  // The only place a timer is started, so the two observers above can never leave two running.
  #tick = () => {
    clearTimeout(this.#timer);
    if (this.#paused || !this.isConnected) {
      this._now = undefined;
      return;
    }
    this.#timer = setTimeout(this.#tick, this.#interval * 1000);
    this._now = new Date();
  };

  render() {
    return html`
      <uui-box headline="Try the demo">
        <p>
          <a href="/demo/members" target="_blank">Sign in as a test member</a> to appear in Members online. Use a
          private window for each extra member.
        </p>
        <p class="muted">
          ${this._now
            ? `Refreshes every ${this.#interval} s, last at ${this._now.toLocaleTimeString()}`
            : "Paused"}
        </p>
      </uui-box>
    `;
  }

  static styles = css`
    :host {
      display: block;
      min-width: 0;
    }

    p {
      margin: 0 0 var(--uui-size-space-3);
    }

    .muted {
      color: var(--uui-color-text-alt);
      margin-bottom: 0;
    }
  `;
}

customElements.define("live-view-demo-try-it-panel", LiveViewDemoTryItPanel);
