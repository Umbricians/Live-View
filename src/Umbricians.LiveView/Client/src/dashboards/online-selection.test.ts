import { test } from "node:test";
import assert from "node:assert/strict";
import { Selection } from "./online-selection.ts";

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
