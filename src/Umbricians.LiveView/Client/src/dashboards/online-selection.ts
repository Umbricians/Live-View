/** Someone who can be picked in an online table. */
export interface Person {
  key: string;
  name: string;
}

/** Who a message goes to: everyone online, or the people picked in the table. */
export interface SelectionSummary {
  allOnline: boolean;
  count: number;
  people: Array<Person>;
}

/** What the dashboard's footer needs from an online table to message whoever is picked in it. */
export interface OnlineTableSelection {
  readonly summary: SelectionSummary;
  /** How many people are online in the table. */
  readonly onlineCount: number;
  /** How many people a message to everyone online reaches right now: the people online who can be messaged. */
  readonly recipients: number;
  /** Clears the selection, e.g. after a message has been sent. */
  clear(): void;
}

/** Whether the footer needs telling: who the selection reaches, or how many are online, changed. */
export function selectionChanged(
  before: Pick<OnlineTableSelection, "summary" | "onlineCount">,
  after: Pick<OnlineTableSelection, "summary" | "onlineCount">,
): boolean {
  return (
    before.summary.allOnline !== after.summary.allOnline ||
    before.summary.count !== after.summary.count ||
    before.onlineCount !== after.onlineCount
  );
}

/**
 * Who is picked in an online table. Picks survive paging, and "all online" covers people on pages that were never
 * loaded. Unticking someone while "all online" is on turns it into an explicit pick of the page minus them. Never
 * changed in place: each change returns a new selection.
 */
export class Selection {
  static readonly none = new Selection(new Map(), false);

  private readonly picked: ReadonlyMap<string, string>;
  readonly allOnline: boolean;

  private constructor(picked: ReadonlyMap<string, string>, allOnline: boolean) {
    this.picked = picked;
    this.allOnline = allOnline;
  }

  isSelected(key: string) {
    return this.allOnline || this.picked.has(key);
  }

  /** Picks or unpicks one person; `page` is everyone pickable on the page they were on. */
  toggle(person: Person, checked: boolean, page: ReadonlyArray<Person>) {
    const picked = this.allOnline ? new Map(page.map((p) => [p.key, p.name])) : new Map(this.picked);
    if (checked) picked.set(person.key, person.name);
    else picked.delete(person.key);
    return new Selection(picked, false);
  }

  /** Picks or unpicks everyone pickable on a page, keeping picks on other pages. */
  togglePage(page: ReadonlyArray<Person>, checked: boolean) {
    const picked = new Map(this.picked);
    for (const person of page) {
      if (checked) picked.set(person.key, person.name);
      else picked.delete(person.key);
    }
    return new Selection(picked, false);
  }

  selectAllOnline() {
    return new Selection(this.picked, true);
  }

  /** Whether everyone, someone or nobody pickable on a page is picked. */
  pageState(page: ReadonlyArray<Person>): "all" | "some" | "none" {
    const selected = page.filter((p) => this.isSelected(p.key)).length;
    return page.length > 0 && selected === page.length ? "all" : selected > 0 ? "some" : "none";
  }

  /** Who the selection reaches, given how many people online can be messaged. */
  summary(recipients: number): SelectionSummary {
    return this.allOnline
      ? { allOnline: true, count: recipients, people: [] }
      : { allOnline: false, count: this.picked.size, people: [...this.picked].map(([key, name]) => ({ key, name })) };
  }
}
