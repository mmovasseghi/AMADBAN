(() => {
    'use strict';
    const printButton = document.querySelector('[data-print]');
    if (printButton) printButton.addEventListener('click', () => {
        const copiesInput = document.querySelector('[data-print-copies]');
        const copies = copiesInput ? Math.max(1, Math.min(12, parseInt(copiesInput.value, 10) || 1)) : 1;
        const clones = [];
        if (copies > 1) {
            const original = document.querySelector('article');
            for (let copy = 1; copy < copies; copy++) {
                const clone = original.cloneNode(true);
                clone.classList.add('print-copy');
                document.body.appendChild(clone);
                clones.push(clone);
            }
        }
        const cleanup = () => clones.forEach(clone => clone.remove());
        window.addEventListener('afterprint', cleanup, { once: true });
        window.print();
        // Some older browsers do not fire afterprint reliably.
        setTimeout(cleanup, 30000);
    });

    const notificationBadge = document.querySelector('[data-notification-badge]');
    if (notificationBadge) setInterval(async () => {
        if (document.hidden) return;
        try {
            const response = await fetch('/Activity?handler=Unread', { cache: 'no-store' });
            if (!response.ok) return;
            const data = await response.json();
            const count = Number(data.count) || 0;
            const label = count > 99 ? '۹۹+' : String(count);
            notificationBadge.textContent = label;
            notificationBadge.hidden = count === 0;
            const sidebarBadge = document.querySelector('.notification-pill');
            if (sidebarBadge) { sidebarBadge.textContent = label; sidebarBadge.hidden = count === 0; }
        } catch (_) {
            // The next normal page visit will refresh the indicator.
        }
    }, 90000);

    const supplier = document.querySelector('[data-supplier]');
    if (supplier) {
        const suggestions = document.getElementById(supplier.getAttribute('list'));
        let timer;
        supplier.addEventListener('input', () => {
            clearTimeout(timer);
            const term = supplier.value.trim();
            suggestions.textContent = '';
            if (term.length < 2) return;
            timer = setTimeout(async () => {
                try {
                    const response = await fetch('/Suppliers?handler=Names&search=' + encodeURIComponent(term));
                    if (!response.ok) return;
                    const names = await response.json();
                    if (supplier.value.trim() !== term) return;
                    names.forEach(name => {
                        const option = document.createElement('option');
                        option.value = name;
                        suggestions.appendChild(option);
                    });
                } catch (_) {
                    // Manual supplier entry remains available.
                }
            }, 220);
        });
    }

    const account = document.querySelector('[data-account]');
    if (account) account.addEventListener('change', () => {
        const selected = account.options[account.selectedIndex];
        if (!selected.value) return;
        ['owner', 'bank', 'sheba'].forEach(field => {
            account.form.elements.namedItem(field).value = selected.dataset[field];
        });
    });

    const paymentKind = document.getElementById('Kind');
    if (paymentKind) {
        const cashCode = document.querySelector('[data-cash-code]');
        const updateCashCode = () => { cashCode.hidden = paymentKind.value !== 'cash'; };
        paymentKind.addEventListener('change', updateCashCode);
        updateCashCode();
    }

    const dispatch = document.querySelector('[data-dispatch-lines]');
    if (dispatch) {
        const template = document.querySelector('[data-dispatch-template]');
        const add = document.querySelector('[data-dispatch-add]');
        const list = document.getElementById('dispatch-products');
        const department = document.querySelector('[name="department"]');
        const recipient = document.querySelector('[name="recipient"]');
        let timer;
        const reindex = () => Array.from(dispatch.rows).forEach((row, index) =>
            row.querySelectorAll('[data-dispatch-field]').forEach(input => {
                input.name = 'lines[' + index + '].' + input.dataset.dispatchField;
            }));
        const addRow = () => {
            if (dispatch.rows.length >= 30) return;
            dispatch.appendChild(template.content.cloneNode(true));
            reindex();
            dispatch.lastElementChild.querySelector('input').focus();
        };
        add.addEventListener('click', addRow);
        dispatch.addEventListener('click', event => {
            if (!event.target.closest('[data-dispatch-remove]')) return;
            const row = event.target.closest('tr');
            if (dispatch.rows.length === 1) row.querySelectorAll('input').forEach(input => input.value = '');
            else { row.remove(); reindex(); }
        });
        dispatch.addEventListener('input', event => {
            if (!event.target.matches('[data-product-code]')) return;
            clearTimeout(timer);
            const term = event.target.value.trim();
            if (term.length < 2 || term.length > 80) return;
            timer = setTimeout(async () => {
                try {
                    const response = await fetch('/Dispatch?handler=Products&search=' + encodeURIComponent(term));
                    if (!response.ok) return;
                    const products = await response.json();
                    list.textContent = '';
                    products.forEach(product => {
                        const option = document.createElement('option');
                        option.value = product.code;
                        option.label = product.name + ' · موجودی ' + product.stock + ' ' + product.unit;
                        list.appendChild(option);
                    });
                } catch (_) {
                    // The printed product code can still be entered directly.
                }
            }, 220);
        });
        dispatch.addEventListener('keydown', event => {
            if (event.key !== 'Enter' || event.isComposing || !event.target.matches('[data-dispatch-field]')) return;
            event.preventDefault();
            const inputs = Array.from(dispatch.querySelectorAll('[data-dispatch-field]'));
            const next = inputs[inputs.indexOf(event.target) + 1];
            if (next) next.focus(); else addRow();
        });
        const filterRecipients = () => {
            recipient.value = '';
            Array.from(recipient.options).forEach(option => {
                if (!option.value) return;
                option.disabled = option.dataset.department !== department.value;
                option.hidden = option.disabled;
            });
        };
        department.addEventListener('change', filterRecipients);
        filterRecipients();
    }

    const receiptInputs = document.querySelectorAll('[data-receipt-product]');
    if (receiptInputs.length) {
        const suggestions = document.getElementById('receipt-products');
        let receiptTimer;
        receiptInputs.forEach(input => input.addEventListener('input', () => {
            clearTimeout(receiptTimer);
            const term = input.value.trim();
            if (term.length < 2 || term.length > 80) return;
            receiptTimer = setTimeout(async () => {
                try {
                    const response = await fetch('/Dispatch?handler=Products&search=' + encodeURIComponent(term));
                    if (!response.ok || input.value.trim() !== term) return;
                    const products = await response.json();
                    suggestions.textContent = '';
                    products.forEach(product => {
                        const option = document.createElement('option');
                        option.value = product.code;
                        option.label = product.name + ' · ' + product.unit + ' · مانده ' + product.stock;
                        suggestions.appendChild(option);
                    });
                } catch (_) { /* Typing a known code remains available. */ }
            }, 220);
        }));
    }

    const lines = document.querySelector('[data-lines]');
    if (!lines) return;
    const template = document.querySelector('[data-line-template]');
    const total = document.querySelector('[data-total]');
    const currency = new Intl.NumberFormat('fa-IR');
    const normalize = text => text.replace(/[۰-۹٠-٩]/g, char =>
        String(char.charCodeAt(0) - (char >= '۰' ? 1776 : 1632)))
        .replace(/[,٬]/g, '').replace('٫', '.').trim();

    const updateTotal = () => {
        let amount = 0;
        for (const row of lines.rows) {
            const price = Number(normalize(row.querySelector('[data-field="Price"]').value));
            const quantity = Number(normalize(row.querySelector('[data-field="Quantity"]').value));
            if (Number.isFinite(price) && Number.isFinite(quantity)) amount += Math.round(price * quantity);
        }
        total.textContent = currency.format(amount);
    };

    const reindex = () => {
        Array.from(lines.rows).forEach((row, index) => {
            row.querySelectorAll('[data-field]').forEach(input => {
                input.name = 'Lines[' + index + '].' + input.dataset.field;
            });
        });
    };
    const addLine = () => {
        if (lines.rows.length >= 100) return;
        lines.appendChild(template.content.cloneNode(true));
        reindex();
        lines.lastElementChild.querySelector('input').focus();
    };

    document.querySelector('[data-add-line]').addEventListener('click', addLine);
    lines.addEventListener('click', event => {
        const remove = event.target.closest('[data-remove]');
        if (!remove) return;
        const row = remove.closest('tr');
        if (lines.rows.length === 1) {
            row.querySelectorAll('input').forEach(input => { input.value = input.dataset.field === 'Quantity' ? '1' : ''; });
            row.querySelector('input').focus();
        } else {
            const next = row.nextElementSibling || row.previousElementSibling;
            row.remove();
            reindex();
            next.querySelector('input').focus();
        }
        updateTotal();
    });
    lines.addEventListener('input', event => {
        if (event.target.matches('[data-field="Price"],[data-field="Quantity"]')) updateTotal();
    });
    lines.addEventListener('keydown', event => {
        if (event.key !== 'Enter' || event.isComposing || !event.target.matches('[data-field]')) return;
        event.preventDefault();
        const fields = Array.from(lines.querySelectorAll('[data-field]'));
        const next = fields[fields.indexOf(event.target) + 1];
        if (next) next.focus();
        else addLine();
    });
    updateTotal();
})();
