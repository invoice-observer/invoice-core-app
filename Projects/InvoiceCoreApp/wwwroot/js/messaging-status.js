(() => {
    'use strict';
    const indicator = document.getElementById('messaging-status');
    if (!indicator) return;

    const states = {
        Connecting: { canWrite: false, tone: 'amber', text: 'RabbitMQ Connecting' },
        Ready: { canWrite: true, tone: 'green', text: 'RabbitMQ Ready' },
        Unavailable: { canWrite: false, tone: 'red', text: 'RabbitMQ Unavailable' },
        NotRequired: { canWrite: true, tone: 'neutral', text: 'RabbitMQ not required' }
    };

    function update(canWrite, tone, text) {
        indicator.textContent = text;
        indicator.className = `messaging-status status-${tone} mb-3`;
        for (const control of document.querySelectorAll('[data-invoice-write]')) {
            if (control.tagName === 'A') {
                control.setAttribute('aria-disabled', String(!canWrite));
                if (canWrite) {
                    control.setAttribute('href', control.dataset.writeHref);
                    control.removeAttribute('tabindex');
                } else {
                    control.removeAttribute('href');
                    control.setAttribute('tabindex', '-1');
                }
            } else {
                control.disabled = !canWrite;
            }
        }
    }

    function preventDisabledLink(event) {
        const link = event.target.closest('a[data-invoice-write]');
        if (link?.getAttribute('aria-disabled') === 'true') event.preventDefault();
    }
    document.addEventListener('click', preventDisabledLink);
    document.addEventListener('keydown', event => {
        if (event.key === 'Enter' || event.key === ' ') preventDisabledLink(event);
    });

    async function poll() {
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 5000);
        try {
            const response = await fetch(indicator.dataset.statusUrl, {
                credentials: 'same-origin', cache: 'no-store', signal: controller.signal
            });
            if (!response.ok) throw new Error('Status request failed');
            const status = await response.json();
            const state = status && Object.hasOwn(states, status.rabbitMq) ? states[status.rabbitMq] : null;
            if (!state || status.canWriteInvoices !== state.canWrite) throw new Error('Invalid status');
            update(status.canWriteInvoices, state.tone, state.text);
        } catch {
            update(false, 'red', 'Status unavailable');
        } finally {
            clearTimeout(timeout);
            // Schedule after completion so slow requests cannot overlap.
            setTimeout(poll, 5000);
        }
    }

    setTimeout(poll, 5000);
})();
