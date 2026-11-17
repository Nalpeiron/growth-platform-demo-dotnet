const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const path = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

const source = readFileSync(path.join(__dirname,
    '../../src/NalpeironGrowthPlatformDemo/wwwroot/js/zenmeter-trial-portal.js'), 'utf8');

function portal({ blocked = false } = {}) {
    const calls = [];
    const child = {
        closed: false,
        opener: {},
        location: { replace: url => calls.push(['navigate', url]) },
        close: () => { child.closed = true; calls.push(['close']); }
    };
    const window = {
        open: (...args) => { calls.push(['open', ...args]); return blocked ? null : child; }
    };
    let click;
    const document = { addEventListener: (_event, listener) => { click = listener; } };
    vm.runInNewContext(source, { window, document, URL });
    const press = (button = { disabled: false }) => click({ target: { closest: () => button } });
    return { api: window.nalpeironTrialPortal, calls, child, press };
}

test('reserves a separate window on click before the portal URL arrives', () => {
    const { api, calls, child, press } = portal();
    press();
    assert.deepEqual(calls, [['open', 'about:blank', '_blank', 'popup,width=1100,height=850']]);
    assert.equal(child.opener, null);
    assert.equal(api.navigate('https://store.test/account/token/auth#/trials'), true);
    assert.deepEqual(calls[1], ['navigate', 'https://store.test/account/token/auth#/trials']);
    api.cancel();
    assert.equal(child.closed, false);
});

test('a popup blocker does not trigger another automatic window or redirect', () => {
    const { api, calls, press } = portal({ blocked: true });
    press();
    assert.equal(api.navigate('https://store.test/account/token/auth#/trials'), false);
    assert.equal(calls.length, 1);
});

test('closing the waiting window does not reopen it after the request finishes', () => {
    const { api, calls, child, press } = portal();
    press();
    child.closed = true;
    assert.equal(api.navigate('https://store.test/account/token/auth#/trials'), false);
    assert.equal(calls.length, 1);
});

test('an unsuccessful request closes only its waiting window', () => {
    const { api, child, press } = portal();
    press();
    api.cancel();
    assert.equal(child.closed, true);
    api.cancel();
});

test('disabled and unrelated buttons never open a portal window', () => {
    const { calls, press } = portal();
    press({ disabled: true });
    press(null);
    assert.deepEqual(calls, []);
});

test('repeated clicks before the request finishes reserve only one window', () => {
    const { calls, press } = portal();
    press();
    press();
    assert.equal(calls.length, 1);
});

test('unsafe URLs close the waiting window without navigating it', () => {
    for (const url of ['http://store.test/account/token', 'javascript:alert(1)', 'https://user:password@store.test/account/token']) {
        const { api, child, calls, press } = portal();
        press();
        assert.equal(api.navigate(url), false);
        assert.equal(child.closed, true);
        assert.equal(calls.some(call => call[0] === 'navigate'), false);
    }
});
