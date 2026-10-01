const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const path = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

const source = readFileSync(path.join(__dirname,
    '../../src/NalpeironGrowthPlatformDemo/wwwroot/js/fastspring-checkout.js'), 'utf8');

function popup() {
    const calls = [];
    const builder = {
        reset: () => calls.push(['reset']),
        tag: tags => calls.push(['tag', tags]),
        add: (sku, callback) => { calls.push(['add', sku]); callback(); },
        checkout: session => calls.push(['checkout', session])
    };
    const window = {};
    const document = {
        getElementById: () => null,
        createElement: () => ({ setAttribute() {} }),
        head: { appendChild: script => { window.fastspring = { builder }; script.onload(); } }
    };
    vm.runInNewContext(source, { window, document, console: { debug() {}, error() {} } });
    return { open: window.nalpeironFastSpring.openPopupCheckout, calls };
}

test('server session opens without replacing its trial settings, products, or tags', () => {
    const { open, calls } = popup();
    open('store.test/popup', 'demo-1', ['base-sku'], { customer_ref: 'customer-1' },
        '/return', false, 'fs-session-1');
    assert.deepEqual(calls, [['checkout', 'fs-session-1']]);
});

test('product checkout remains available for top-ups and Zentitle', () => {
    const { open, calls } = popup();
    open('store.test/popup', 'demo-1', ['base-sku', 'addon-sku'], { customer_ref: 'customer-1' }, '/return');
    assert.deepEqual(calls.map(call => call[0]), ['reset', 'tag', 'add', 'add', 'checkout']);
    assert.equal(calls[2][1], 'base-sku');
    assert.equal(calls[3][1], 'addon-sku');
    assert.equal(calls[4][1], undefined);
});
