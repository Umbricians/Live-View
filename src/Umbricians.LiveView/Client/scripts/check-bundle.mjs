// Checks the built bundle, which the client tests never see because they import the source.
// Fails, naming each problem, unless:
// - the bundle entry exports only `manifests`, because Umbraco registers every export of a bundle as an extension;
// - the API entry the import map points `@umbricians/live-view` at is built and exports exactly src/index.ts's values;
// - every import is relative or from `@umbraco-cms/`, and no source map lists a source from node_modules/@umbraco*, so the
//   backoffice's own modules are left external (once one is bundled, its own imports are relative);
// - no decorated field is also a class field, which would shadow the accessor Lit's decorators put on the prototype,
//   and no decorator is left as raw `@decorator` syntax (what Oxc emits when experimentalDecorators is off).
// Each check fails rather than passing vacuously: on no decorated fields at all, and on no source maps at all.
// Parses with Vite's own `parseAst` (Rolldown's Oxc parser, ESTree output), so it needs no dependency of its own.
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { parseAst } from "vite";

// Checks another folder of built files when given one, e.g. to try it on an older build.
const outDir = process.argv[2] ?? fileURLToPath(new URL("../../wwwroot/App_Plugins/UmbriciansLiveView/", import.meta.url));
const bundleEntry = "umbricians-live-view.js";
const apiSpecifier = "@umbricians/live-view";

const problems = new Set();
let decoratedFieldCount = 0;
let sourceMapCount = 0;
const files = readdirSync(outDir).filter((f) => f.endsWith(".js"));
if (!files.includes(bundleEntry)) problems.add(`${bundleEntry} is missing from ${outDir}`);

for (const file of files) {
  const ast = parseAst(readFileSync(join(outDir, file), "utf8"));
  if (file === bundleEntry) checkEntryExports(file, ast);
  checkImports(file, ast);
  checkSourceMap(file);
  checkDecoratedFields(file, ast);
}

checkApiEntry();

// Some chunks legitimately have no map (Oxc's decorate helper, the re-export-only API entry), but with none at all the
// bundling check above can't see anything, which means `sourcemap: true` was dropped from vite.config.ts.
if (files.length && !sourceMapCount)
  problems.add("no .map files were built, so bundled @umbraco code can't be detected; keep sourcemap: true in vite.config.ts");
// The elements' @state()/@property()/@customElement() decorators always give some; none means none were recognised.
if (files.length && !decoratedFieldCount)
  problems.add("no decorated fields were found, so the class-field check checked nothing; are the decorators compiled (experimentalDecorators)?");

if (problems.size) {
  console.error(`The built bundle in ${relative(process.cwd(), outDir)} has ${problems.size} problem(s):`);
  for (const p of problems) console.error(`  - ${p}`);
  process.exit(1);
}
console.log(
  `Checked ${files.length} built files: the entry exports only manifests, ${apiSpecifier} exports what src/index.ts does, every import is relative or @umbraco-cms/ and none is bundled, ` +
    `and none of the ${decoratedFieldCount} decorated fields is a class field.`,
);

function checkEntryExports(file, ast) {
  const names = exportedNames(ast);
  if (names.length !== 1 || names[0] !== "manifests")
    problems.add(`${file} exports ${names.join(", ") || "nothing"}; the bundle entry must export only manifests`);
}

// The API entry is what `@umbricians/live-view` resolves to, so it must be the file the import map names and export
// exactly the values src/index.ts exports (its type-only exports leave nothing at runtime).
function checkApiEntry() {
  const packageJson = JSON.parse(readFileSync(fileURLToPath(new URL("../public/umbraco-package.json", import.meta.url)), "utf8"));
  const mapped = packageJson.importmap?.imports?.[apiSpecifier];
  if (!mapped) {
    problems.add(`public/umbraco-package.json doesn't map ${apiSpecifier}`);
    return;
  }

  const file = mapped.split("/").at(-1);
  if (!existsSync(join(outDir, file))) {
    problems.add(`${file}, which the import map points ${apiSpecifier} at, is missing from the build`);
    return;
  }

  const source = parseAst(readFileSync(fileURLToPath(new URL("../src/index.ts", import.meta.url)), "utf8"), { lang: "ts" });
  const expected = exportedNames(source).sort();
  const built = exportedNames(parseAst(readFileSync(join(outDir, file), "utf8"))).sort();
  if (!expected.length) problems.add(`src/index.ts exports no values, so ${apiSpecifier} would export nothing`);
  if (built.join() !== expected.join())
    problems.add(`${file} exports ${built.join(", ") || "nothing"}, but ${apiSpecifier} should export ${expected.join(", ")} (src/index.ts)`);
}

// Value exports only: `export type` and type-only specifiers have no runtime binding.
function exportedNames(ast) {
  const names = [];
  for (const node of ast.body) {
    if (node.type === "ExportNamedDeclaration") {
      if (node.exportKind === "type") continue;
      for (const s of node.specifiers) if (s.exportKind !== "type") names.push(nameOf(s.exported));
      const d = node.declaration;
      if (d?.type === "VariableDeclaration") for (const v of d.declarations) names.push(v.id.name ?? "(pattern)");
      else if (d && !d.type.startsWith("TS")) names.push(d.id?.name ?? "(declaration)");
    } else if (node.type === "ExportDefaultDeclaration") names.push("default");
    else if (node.type === "ExportAllDeclaration") names.push(node.exported ? nameOf(node.exported) : `* from ${node.source.value}`);
  }
  return names;
}

function checkSourceMap(file) {
  const map = join(outDir, `${file}.map`);
  if (!existsSync(map)) return;
  sourceMapCount++;
  const bundled = JSON.parse(readFileSync(map, "utf8")).sources.filter((s) => /node_modules\/@umbraco/.test(s));
  if (bundled.length)
    problems.add(`${file} bundles ${bundled.length} module(s) from node_modules/@umbraco*, e.g. ${bundled[0].replace(/^.*node_modules\//, "")}`);
}

function checkImports(file, ast) {
  walk(ast, (node) => {
    let source;
    if (node.type === "ImportDeclaration" || node.type === "ExportAllDeclaration" || (node.type === "ExportNamedDeclaration" && node.source))
      source = node.source;
    else if (node.type === "ImportExpression") source = node.source;
    else return;
    if (source.type !== "Literal" || typeof source.value !== "string") {
      problems.add(`${file} has an import() whose source isn't a string literal`);
      return;
    }
    const s = source.value;
    if (!s.startsWith("./") && !s.startsWith("../") && !s.startsWith("@umbraco-cms/"))
      problems.add(`${file} imports "${s}", which is neither relative nor @umbraco-cms/`);
  });
}

// Legacy (experimentalDecorators) field decorators compile to a call, whatever the helper is called or wherever it
// lives (Oxc imports a shared `decorate` chunk, esbuild and tslib inline `__decorateClass` / `__decorate`):
//   helper([decorators...], Class.prototype, "field", descriptorOrFlag)
// So any call whose first argument is an array, second `<Identifier>.prototype` and third a string names a decorated
// field of the class bound to that identifier. The binding is followed through `class X {}`, `X = class {}`,
// `X = Y`, `X = (Y = class {}, ...)` (how a class with private members or static blocks comes out when lowered) and
// `X = (..., Y)`. A decorated field whose class can't be found is a problem too, so a new output shape fails the
// check rather than slipping past it.
// The field must not also be a class field (`field = 5;`, emitted when useDefineForClassFields is true) or be
// defined on the instance in the constructor by a helper call such as `_defineProperty(this, "field", 5)` or
// esbuild's `__publicField(this, "field", 5)`, which is how class fields come out when they are lowered. The
// helper's name is minified, so any constructor call is matched whose first argument is `this` and second the
// field's name.
function checkDecoratedFields(file, ast) {
  const decorated = [];
  const classes = new Map();
  const aliases = new Map();
  const bind = (name, value) => {
    if (!value) return;
    if (value.type === "ClassExpression") addClass(classes, name, value);
    else if (value.type === "Identifier" && value.name !== name) aliases.set(name, [...(aliases.get(name) ?? []), value.name]);
    else if (value.type === "AssignmentExpression") bind(name, value.right);
    else if (value.type === "SequenceExpression") bind(name, value.expressions.at(-1));
    else if (value.type === "ParenthesizedExpression") bind(name, value.expression);
  };
  walk(ast, (node) => {
    // parseAst puts uncompiled `@decorator` syntax in a `decorators` array on classes, class members and parameters.
    if (Array.isArray(node.decorators) && node.decorators.length)
      problems.add(`${file}: ${describe(node)} still has a raw decorator, so it is never applied (is experimentalDecorators off?)`);
    if (node.type === "CallExpression") {
      const [decorators, target, key] = node.arguments;
      if (decorators?.type === "ArrayExpression" && isPrototypeOf(target) && key?.type === "Literal" && typeof key.value === "string")
        decorated.push({ className: target.object.name, field: key.value });
    }
    if (node.type === "ClassDeclaration" && node.id) addClass(classes, node.id.name, node);
    if (node.type === "VariableDeclarator" && node.id.type === "Identifier") bind(node.id.name, node.init);
    if (node.type === "AssignmentExpression" && node.left.type === "Identifier") bind(node.left.name, node.right);
  });

  const resolve = (name, seen = new Set()) => {
    if (seen.has(name)) return [];
    seen.add(name);
    return [...(classes.get(name) ?? []), ...(aliases.get(name) ?? []).flatMap((alias) => resolve(alias, seen))];
  };

  decoratedFieldCount += decorated.length;
  for (const { className, field } of decorated) {
    const candidates = resolve(className);
    if (!candidates.length)
      problems.add(`${file}: can't find the class ${className} that decorated field "${field}" belongs to; teach scripts/check-bundle.mjs this output shape`);
    for (const cls of candidates) {
      if (declaresInstanceField(cls, field))
        problems.add(`${file}: decorated field "${field}" of class ${className} is also declared as a class field, which shadows its decorator's accessor (is useDefineForClassFields true?)`);
    }
  }
}

function declaresInstanceField(cls, field) {
  let found = false;
  for (const member of cls.body.body) {
    if (member.type === "PropertyDefinition" && !member.static && !member.computed && nameOf(member.key) === field) found = true;
    if (member.type === "MethodDefinition" && member.kind === "constructor")
      walk(member.value.body, (node) => {
        if (node.type !== "CallExpression") return;
        const [self, key] = node.arguments;
        if (self?.type === "ThisExpression" && key?.type === "Literal" && key.value === field)
          found = true;
      });
  }
  return found;
}

function describe(node) {
  if (node.key) return `${node.type === "MethodDefinition" ? "method" : "field"} "${nameOf(node.key)}"`;
  if (node.type === "ClassExpression" || node.type === "ClassDeclaration") return node.id ? `class ${node.id.name}` : "a class";
  return node.type;
}

function isPrototypeOf(node) {
  return node?.type === "MemberExpression" && !node.computed && node.object.type === "Identifier" && node.property.name === "prototype";
}

function addClass(classes, name, node) {
  classes.set(name, [...(classes.get(name) ?? []), node]);
}


function nameOf(node) {
  if (node.type === "Identifier") return node.name;
  if (node.type === "PrivateIdentifier") return `#${node.name}`;
  return String(node.value);
}

function walk(node, visit) {
  if (Array.isArray(node)) {
    for (const child of node) walk(child, visit);
    return;
  }
  if (!node || typeof node.type !== "string") return;
  visit(node);
  for (const key in node) {
    const value = node[key];
    if (value && typeof value === "object") walk(value, visit);
  }
}
