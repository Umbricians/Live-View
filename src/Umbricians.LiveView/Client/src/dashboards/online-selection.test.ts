import { test } from "node:test";
import assert from "node:assert/strict";
import { Selection, selectionChanged } from "./online-selection.ts";

const ann = { key: "a", name: "Ann" };
const bob = { key: "b", name: "Bob" };
const cat = { key: "c", name: "Cat" };
const page = [ann, bob];

test("picks survive moving to another page", () => {
  const selection = Selection.none.toggle(ann, true, page).toggle(cat, true, [cat]);

  assert.deepEqual(selection.summary(10), { allOnline: false, count: 2, people: [ann, cat] });
  assert.equal(selection.pageState(page), "some");
});

test("picking a page keeps picks on other pages, and unpicking it only drops that page", () => {
  const selection = Selection.none.toggle(cat, true, [cat]).togglePage(page, true);
  assert.equal(selection.pageState(page), "all");
  assert.equal(selection.summary(10).count, 3);

  assert.deepEqual(selection.togglePage(page, false).summary(10).people, [cat]);
});

test("all online reaches everyone who can be messaged, on pages never loaded too", () => {
  const selection = Selection.none.selectAllOnline();

  assert.deepEqual(selection.summary(42), { allOnline: true, count: 42, people: [] });
  assert.ok(selection.isSelected("someone-on-page-9"));
});

test("unticking someone while all online is on picks the rest of their page", () => {
  const selection = Selection.none.selectAllOnline().toggle(ann, false, page);

  assert.deepEqual(selection.summary(42), { allOnline: false, count: 1, people: [bob] });
});

test("a page with nobody pickable is never all picked", () => {
  assert.equal(Selection.none.selectAllOnline().pageState([]), "none");
});

const reach = (allOnline: boolean, count: number, people: typeof page = [], onlineCount = 10, recipients = 10) => ({
  summary: { allOnline, count, people },
  onlineCount,
  recipients,
});

test("the footer is told when the count, all online, the online total or the number who can be messaged changes", () => {
  assert.equal(selectionChanged(reach(false, 1), reach(false, 2)), true);
  assert.equal(selectionChanged(reach(false, 1), reach(true, 1)), true);
  assert.equal(selectionChanged(reach(false, 1, [], 10), reach(false, 1, [], 11)), true);
  assert.equal(selectionChanged(reach(false, 1, [], 10, 9), reach(false, 1, [], 10, 10)), true);
});

test("the footer is not told when only who is listed differs", () => {
  assert.equal(selectionChanged(reach(false, 1), reach(false, 1)), false);
  assert.equal(selectionChanged(reach(false, 1, [ann]), reach(false, 1, [bob])), false);
});
