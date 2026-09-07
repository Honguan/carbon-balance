// Dependency-free event regression check: node tests/E2E/site-interactions.mjs
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";

class Element {
    constructor(value = "") {
        this.value = value;
        this.dataset = {};
        this.listeners = {};
        this.queries = {};
        this.attributes = {};
    }
    addEventListener(name, callback) { (this.listeners[name] ??= []).push(callback); }
    dispatch(name, properties = {}) {
        const event = { target: this, defaultPrevented: false, preventDefault() { this.defaultPrevented = true; }, ...properties };
        for (const callback of this.listeners[name] ?? []) callback(event);
        return event;
    }
    querySelectorAll(selector) { return this.queries[selector] ?? []; }
    querySelector(selector) { return this.querySelectorAll(selector)[0] ?? null; }
    setAttribute(name, value) { this.attributes[name] = value; }
    removeAttribute(name) { delete this.attributes[name]; }
    after(element) { this.status = element; }
}
class Input extends Element {}
class Form extends Element {
    constructor() { super(); this.fields = {}; this.valid = true; }
    checkValidity() { return this.valid; }
    requestSubmit() { return document.dispatch("submit", { target: this }); }
}
const document = new Element();
const window = new Element();
document.createElement = () => new Element();
let confirmResult = false;
let confirms = 0;
window.confirm = () => { confirms++; return confirmResult; };
const activity = new Form();
activity.fields = { amount: "1" };
const selectorForm = new Form();
const selector = new Element("inventory-a");
selector.form = selectorForm;
const otherSelect = new Element("__other__");
otherSelect.dataset.otherTarget = "#other";
const other = new Input("保留的項目");
const filterInput = new Input();
const list = new Element();
const items = ["電力", "運輸"].map((text) => Object.assign(new Element(), { textContent: text }));
list.queries["[data-factor-list-item]"] = items;
const kind = new Element();
kind.selectedOptions = [{ dataset: { formulaKind: "direct" } }];
const amount = new Input("-1");
const distance = new Input("10");
const weight = new Input("20");
const rawUnit = new Element("kg");
const canonicalUnit = new Element("kg");
const factorSearch = new Input();
const factor = new Element("factor-a");
const factorOption = Object.assign(new Element("factor-a"), {
    selected: true, textContent: "材料", dataset: { factorUnit: "kg", factorValue: "2" }
});
factor.selectedOptions = [factorOption];
factor.queries["option[value]"] = [factorOption];
const controls = {
    activityKind: kind, rawValue: amount, transportDistanceKm: distance, transportWeightKg: weight,
    rawUnitCode: rawUnit, canonicalUnitCode: canonicalUnit, allocationFactor: new Input("1")
};
for (const [name, input] of Object.entries(controls)) activity.queries[`[name='${name}']`] = [input];
activity.queries["[data-factor-filter]"] = [factorSearch];
activity.queries["[data-factor-select]"] = [factor];
activity.queries["[data-emission-preview]"] = [new Element()];
for (const [selector, inputs] of [["[data-direct-input]", [amount]], ["[data-transport-input]", [distance, weight]], ["[data-unit-input]", [rawUnit, canonicalUnit]]]) {
    activity.queries[selector] = inputs.map((input) => {
        const group = new Element();
        group.queries["input, select"] = [input];
        return group;
    });
}
document.queries = {
    "select[data-controlled-other]": [otherSelect], "#other": [other],
    "select[data-auto-submit-select]": [selector],
    "[data-factor-list-filter]": [filterInput], "[data-factor-list]": [list],
    "[data-emission-form]": [activity], "form[method='post']": [activity]
};
vm.runInNewContext(readFileSync(new URL("../../src/CarbonFootprint.Web/wwwroot/js/site.js", import.meta.url), "utf8"), {
    document, window, HTMLInputElement: Input, HTMLTextAreaElement: Input, HTMLFormElement: Form,
    File: class File {}, FormData: class { constructor(form) { return Object.entries(form.fields); } }
});
document.dispatch("DOMContentLoaded");
assert.equal(document.listeners.click, undefined, "Workspace navigation must remain native");
assert.equal(window.dispatch("beforeunload").defaultPrevented, false);
activity.dataset.unsaved = "true";
assert.equal(window.dispatch("beforeunload").defaultPrevented, true, "Restored unsuccessful submissions are still unsaved");
delete activity.dataset.unsaved;
assert.equal(window.dispatch("beforeunload").defaultPrevented, false);
activity.fields.amount = "2";
assert.equal(window.dispatch("beforeunload").defaultPrevented, true, "Unsaved forms must guard navigation");
const download = new Form();
download.dataset.download = "";
assert.equal(download.requestSubmit().defaultPrevented, false);
assert.equal(download.requestSubmit().defaultPrevented, false, "File downloads must remain repeatable without a page navigation");
assert.equal(download.dataset.submitting, undefined);
assert.equal(download.attributes["aria-busy"], undefined);
assert.equal(confirms, 0, "Downloads do not discard other forms");
assert.equal(window.dispatch("beforeunload").defaultPrevented, true, "Downloads must preserve unsaved-change protection");
selector.value = "inventory-b";
selector.dispatch("change");
assert.equal(selector.value, "inventory-a", "Cancelled inventory switches restore the selection");
assert.equal(confirms, 1);
assert.equal(selectorForm.dataset.submitting, undefined);
activity.valid = false;
assert.equal(activity.requestSubmit().defaultPrevented, true);
assert.equal(activity.dataset.submitting, undefined, "Invalid forms remain retryable");
activity.valid = true;
assert.equal(activity.requestSubmit().defaultPrevented, false);
assert.equal(activity.requestSubmit().defaultPrevented, true, "A second submit must be blocked");
assert.equal(window.dispatch("beforeunload").defaultPrevented, false, "Saving the changed form must not warn");
document.queries["form[data-submitting='true']"] = [activity];
window.dispatch("pageshow");
assert.equal(activity.dataset.submitting, undefined, "Back-forward restoration must unlock forms");
confirmResult = true;
selector.value = "inventory-b";
selector.dispatch("change");
assert.equal(selector.value, "inventory-b");
assert.equal(selectorForm.dataset.submitting, "true");
otherSelect.value = "preset";
otherSelect.dispatch("change");
assert.equal(other.disabled, true);
otherSelect.value = "__other__";
otherSelect.dispatch("change");
assert.equal(other.value, "保留的項目");
assert.equal(other.disabled, false);
filterInput.value = "不存在";
filterInput.dispatch("input");
assert(items.every((item) => item.hidden));
assert.match(filterInput.status.textContent, /沒有符合/);
filterInput.value = "";
filterInput.dispatch("input");
assert.match(filterInput.status.textContent, /2 筆/);
factorSearch.value = "不匹配";
activity.dispatch("input");
assert.equal(factor.value, "factor-a", "Search must not silently clear the chosen factor");
assert.equal(factorOption.hidden, false);
kind.selectedOptions[0].dataset.formulaKind = "transport";
activity.dispatch("change");
assert.equal(amount.disabled, true, "Hidden invalid inputs must leave native validation");
assert.equal(amount.value, "-1");
assert.equal(distance.disabled, false);
assert.equal(factor.value, "", "An incompatible factor must still be cleared");
kind.selectedOptions[0].dataset.formulaKind = "direct";
activity.dispatch("change");
assert.equal(amount.disabled, false);
assert.equal(amount.value, "-1");
console.log("Site interaction checks passed: navigation, dirty guard, retries, switching, filters and hidden inputs.");
