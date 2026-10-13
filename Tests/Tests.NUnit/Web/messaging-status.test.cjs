const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function setup() {
    const timers = new Map();
    let nextId = 0;
    const requests = [];
    const listeners = {};
    const link = {
        tagName: 'A', dataset: { writeHref: '/Invoices/Create' }, attributes: {},
        setAttribute(k, v) { this.attributes[k] = v; },
        removeAttribute(k) { delete this.attributes[k]; },
        getAttribute(k) { return this.attributes[k] ?? null; }
    };
    const buttons = Array.from({ length: 4 }, () => ({ tagName: 'BUTTON', disabled: false }));
    const input = { value: 'Unsaved invoice and line values' };
    const errors = { textContent: 'Existing validation error' };
    const indicator = { dataset: { statusUrl: '/api/status' }, textContent: 'RabbitMQ Ready', className: '' };
    const document = {
        getElementById: id => id === 'messaging-status' ? indicator : null,
        querySelectorAll: selector => {
            assert.equal(selector, '[data-invoice-write]');
            return [link, ...buttons];
        },
        addEventListener: (name, handler) => { listeners[name] = handler; }
    };
    const context = {
        document, AbortController,
        setTimeout: (fn, ms) => { const id = ++nextId; timers.set(id, { fn, ms }); return id; },
        clearTimeout: id => timers.delete(id),
        fetch: (url, options) => new Promise((resolve, reject) => {
            requests.push({ url, options, resolve, reject });
            options.signal.addEventListener('abort', () => reject(new Error('aborted')));
        })
    };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../../Projects/InvoiceCoreApp/wwwroot/js/messaging-status.js'), 'utf8'), context);
    async function flush() { for (let i = 0; i < 12; i++) await Promise.resolve(); }
    async function tick(ms) {
        const entry = [...timers].find(([, t]) => t.ms === ms);
        assert.ok(entry, `expected ${ms}ms timer`);
        timers.delete(entry[0]);
        entry[1].fn();
        await flush();
    }
    async function reply(body, ok = true) {
        requests.at(-1).resolve({ ok, json: async () => body });
        await flush();
    }
    function enabled(value) {
        for (const button of buttons) assert.equal(button.disabled, !value);
        assert.equal(link.attributes.href, value ? '/Invoices/Create' : undefined);
        assert.equal(link.attributes['aria-disabled'], String(!value));
        assert.equal(link.attributes.tabindex, value ? undefined : '-1');
    }
    return { timers, requests, listeners, link, indicator, input, errors, tick, reply, enabled, flush };
}

test('polls every five seconds without overlap; restores all states and preserves form data', async () => {
    const h = setup();
    assert.equal(h.requests.length, 0);
    for (const [state, canWrite, tone, text] of [
        ['Unavailable', false, 'red', 'RabbitMQ Unavailable'],
        ['Ready', true, 'green', 'RabbitMQ Ready'],
        ['Connecting', false, 'amber', 'RabbitMQ Connecting'],
        ['NotRequired', true, 'neutral', 'RabbitMQ not required']
    ]) {
        await h.tick(5000);
        assert.equal(h.requests.at(-1).url, '/api/status');
        assert.equal(h.requests.at(-1).options.cache, 'no-store');
        assert.equal(h.requests.at(-1).options.credentials, 'same-origin');
        assert.equal([...h.timers.values()].filter(t => t.ms === 5000).length, 1, 'only the request timeout remains');
        await h.reply({ canWriteInvoices: canWrite, rabbitMq: state });
        h.enabled(canWrite);
        assert.equal(h.indicator.textContent, text);
        assert.match(h.indicator.className, new RegExp(`status-${tone}`));
        assert.equal(h.input.value, 'Unsaved invoice and line values');
        assert.equal(h.errors.textContent, 'Existing validation error');
    }
});

for (const failure of ['network', 'timeout', 'http', 'json', 'shape', 'inconsistent', 'unknown']) {
    test(`${failure} fails closed and later success restores controls`, async () => {
        const h = setup();
        await h.tick(5000);
        if (failure === 'network') h.requests.at(-1).reject(new Error('offline'));
        else if (failure === 'timeout') {
            await h.tick(5000);
            assert.equal(h.requests.length, 1, 'timeout must not start an overlapping poll');
            assert.equal(h.requests[0].options.signal.aborted, true);
        }
        else if (failure === 'json') h.requests.at(-1).resolve({ ok: true, json: async () => { throw new Error('bad JSON'); } });
        else await h.reply(failure === 'shape' ? {} : failure === 'inconsistent'
            ? { canWriteInvoices: true, rabbitMq: 'Unavailable' }
            : { canWriteInvoices: true, rabbitMq: 'Unknown' }, failure !== 'http');
        await h.flush();
        h.enabled(false);
        assert.equal(h.indicator.textContent, 'Status unavailable');
        await h.tick(5000);
        await h.reply({ canWriteInvoices: true, rabbitMq: 'Ready' });
        h.enabled(true);
        assert.equal(h.indicator.textContent, 'RabbitMQ Ready');
    });
}

test('disabled link blocks mouse and keyboard activation', async () => {
    const h = setup();
    await h.tick(5000);
    await h.reply({ canWriteInvoices: false, rabbitMq: 'Unavailable' });
    for (const [event, key] of [['click', undefined], ['keydown', 'Enter'], ['keydown', ' ']]) {
        let prevented = false;
        h.listeners[event]({ key, target: { closest: () => h.link }, preventDefault: () => { prevented = true; } });
        assert.equal(prevented, true);
    }
});
