(() => {
    'use strict';
    const fetchWithTimeout = async (url, options = {}, ms = 15000) => {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), ms);
        try {
            return await fetch(url, { ...options, signal: controller.signal });
        } finally {
            clearTimeout(timer);
        }
    };
    const simpleKey = 'amadban-simple-ui';
    const simpleOn = () => {
        const v = localStorage.getItem(simpleKey);
        if (v === '0') return false;
        return true;
    };
    const syncSimpleToggle = () => {
        document.documentElement.classList.toggle('simple-ui', simpleOn());
        const on = simpleOn();
        const labelOff = 'حالت خوانایی آسان — برای حروف بزرگ‌تر بزنید';
        const labelOn = 'حالت خوانایی آسان (روشن) — برای حالت معمولی دوباره بزنید';
        document.querySelectorAll('[data-simple-ui-toggle]').forEach(el => {
            el.setAttribute('aria-pressed', on ? 'true' : 'false');
            el.textContent = on ? labelOn : labelOff;
        });
    };
    syncSimpleToggle();
    window.addEventListener('pageshow', () => {
        document.querySelectorAll('button.primary[type="submit"][aria-busy], input.primary[type="submit"][aria-busy]').forEach(el => {
            el.disabled = false;
            el.removeAttribute('aria-busy');
        });
    });
    document.querySelectorAll('[data-simple-ui-toggle]').forEach(btn => btn.addEventListener('click', () => {
        localStorage.setItem(simpleKey, simpleOn() ? '0' : '1');
        syncSimpleToggle();
    }));

    document.querySelectorAll('form').forEach(form => {
        const confirmButtons = form.querySelectorAll('[data-confirm]');
        if (!confirmButtons.length) return;
        form.addEventListener('submit', event => {
            const single = confirmButtons.length === 1;
            const btn = event.submitter;
            const active = single
                ? confirmButtons[0]
                : (btn?.hasAttribute?.('data-confirm') ? btn : null);
            if (!active) return;
            const message = active.getAttribute('data-confirm') || 'مطمئن هستید؟';
            if (!window.confirm(message)) event.preventDefault();
        });
    });

    document.querySelectorAll('form[method="post"]').forEach(form => {
        form.addEventListener('submit', () => {
            form.querySelectorAll('button.primary[type="submit"], input.primary[type="submit"]').forEach(el => {
                el.disabled = true;
                el.setAttribute('aria-busy', 'true');
                window.setTimeout(() => {
                    el.disabled = false;
                    el.removeAttribute('aria-busy');
                }, 25000);
            });
        }, { once: false });
    });

    document.querySelectorAll('table[data-clickable-rows] tbody tr').forEach(row => {
        const open = row.querySelector('td a.button[href], td:last-child a[href]');
        if (!open) return;
        const href = open.getAttribute('href');
        if (!href) return;
        row.classList.add('row-openable');
        row.addEventListener('click', event => {
            if (event.target.closest('a, button, input, select, textarea, label')) return;
            window.location.assign(href);
        });
    });

    const printButton = document.querySelector('[data-print]');
    if (printButton) printButton.addEventListener('click', () => {
        const copiesInput = document.querySelector('[data-print-copies]');
        const copies = copiesInput ? Math.max(1, Math.min(12, parseInt(copiesInput.value, 10) || 1)) : 1;
        const clones = [];
        if (copies > 1) {
            const documentPage = document.querySelector('.document-print-page');
            const original = documentPage || document.querySelector('article');
            for (let copy = 1; copy < copies; copy++) {
                const clone = documentPage ? document.createElement('div') : original.cloneNode(true);
                clone.classList.add(documentPage ? 'document-print-spacer' : 'print-copy');
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

    const fitDocumentPrint = () => {
        const pages = document.querySelectorAll('.document-print-page');
        if (!pages.length) return;
        const available = 165 * 96 / 25.4;
        pages.forEach(page => {
            const sheet = page.querySelector('.document-sheet');
            if (!sheet) return;
            sheet.style.removeProperty('--document-print-scale');
            const height = sheet.scrollHeight;
            sheet.style.setProperty('--document-print-scale', String(Math.min(1, available / Math.max(height, 1))));
        });
    };
    window.addEventListener('beforeprint', fitDocumentPrint);

    const taxSelectAll = document.querySelector('[data-tax-select-all]');
    if (taxSelectAll) taxSelectAll.addEventListener('change', () => {
        document.querySelectorAll('[data-tax-select]').forEach(box => { box.checked = taxSelectAll.checked; });
    });

    const notificationBadge = document.querySelector('[data-notification-badge]');
    if (notificationBadge) setInterval(async () => {
        if (document.hidden) return;
        try {
            const response = await fetchWithTimeout('/Activity?handler=Unread', { cache: 'no-store' }, 12000);
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
        if (!suggestions) { /* manual vendor entry only */ }
        else {
        let timer;
        supplier.addEventListener('input', () => {
            clearTimeout(timer);
            const term = supplier.value.trim();
            suggestions.textContent = '';
            if (term.length < 2) return;
            timer = setTimeout(async () => {
                try {
                    const response = await fetchWithTimeout('/Suppliers?handler=Names&search=' + encodeURIComponent(term));
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
    }

    const warehouseMeeting = document.querySelector('[data-warehouse-flag]');
    const meetingCheckbox = document.getElementById('Meeting');
    if (warehouseMeeting && meetingCheckbox) {
        const syncMeeting = () => {
            if (!warehouseMeeting.checked) meetingCheckbox.checked = true;
        };
        warehouseMeeting.addEventListener('change', syncMeeting);
        syncMeeting();
    }

    const fillAccountFromSelect = (select) => {
        if (!select?.form) return;
        const selected = select.options[select.selectedIndex];
        if (!selected?.value) return;
        ['owner', 'bank', 'sheba'].forEach(field => {
            const input = select.form.querySelector(`[name="${field}"]`);
            if (input && selected.dataset[field] != null) input.value = selected.dataset[field];
        });
    };
    const account = document.querySelector('[data-account]');
    if (account) {
        account.addEventListener('change', () => fillAccountFromSelect(account));
        const real = [...account.options].filter(o => o.value);
        if (real.length === 1) {
            account.value = real[0].value;
            fillAccountFromSelect(account);
        } else if (account.selectedIndex > 0) {
            fillAccountFromSelect(account);
        }
    }

    const paymentKind = document.getElementById('Kind');
    if (paymentKind) {
        const cashCode = document.querySelector('[data-cash-code]');
        const updateCashCode = () => {
            if (!cashCode) return;
            cashCode.hidden = paymentKind.value !== 'cash';
        };
        paymentKind.addEventListener('change', updateCashCode);
        updateCashCode();
    }

    const dispatch = document.querySelector('[data-dispatch-lines]');
    if (dispatch) {
        const template = document.querySelector('[data-dispatch-template]');
        const add = document.querySelector('[data-dispatch-add]');
        const list = document.getElementById('dispatch-products');
        const form = document.getElementById('dispatch-form');
        const department = form?.querySelector('[name="department"]');
        const recipient = form?.querySelector('[data-dispatch-recipient]');
        if (!template || !add || !list || !form || !department || !recipient) { /* incomplete dispatch markup */ }
        else {
        let timer;
        let recipientRequest = 0;
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
                    const response = await fetchWithTimeout('/Dispatch?handler=Products&search=' + encodeURIComponent(term));
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
        const loadRecipients = async () => {
            if (!department || !recipient) return;
            const departmentId = department.value;
            recipient.textContent = '';
            const placeholder = document.createElement('option');
            placeholder.value = '';
            if (!departmentId) {
                placeholder.textContent = 'ابتدا بخش مقصد را انتخاب کنید';
                recipient.appendChild(placeholder);
                recipient.disabled = true;
                return;
            }
            placeholder.textContent = 'در حال بارگذاری تحویل‌گیرندگان…';
            recipient.appendChild(placeholder);
            recipient.disabled = true;
            const requestId = ++recipientRequest;
            try {
                const response = await fetchWithTimeout('/Dispatch?handler=Recipients&department=' + encodeURIComponent(departmentId), { cache: 'no-store' });
                if (!response.ok || requestId !== recipientRequest) return;
                const people = await response.json();
                if (requestId !== recipientRequest) return;
                recipient.textContent = '';
                const pick = document.createElement('option');
                pick.value = '';
                pick.textContent = people.length > 0 ? 'تحویل‌گیرنده همان بخش را انتخاب کنید' : 'برای این بخش هنوز تحویل‌گیرنده ثبت نشده است';
                recipient.appendChild(pick);
                people.forEach(person => {
                    const option = document.createElement('option');
                    option.value = String(person.id);
                    option.textContent = person.label;
                    recipient.appendChild(option);
                });
                recipient.disabled = people.length === 0;
            } catch (_) {
                if (requestId !== recipientRequest) return;
                recipient.textContent = '';
                const failed = document.createElement('option');
                failed.value = '';
                failed.textContent = 'بارگذاری تحویل‌گیرنده ممکن نشد؛ دوباره بخش را انتخاب کنید';
                recipient.appendChild(failed);
                recipient.disabled = true;
            }
        };
        department.addEventListener('change', loadRecipients);
        loadRecipients();
        }
    }

    const signerForm = document.getElementById('request-signer-form');
    if (signerForm) {
        const invoiceId = signerForm.dataset.invoiceId;
        const department = signerForm.querySelector('[data-request-department]');
        const expert = signerForm.querySelector('[data-request-expert]');
        const selectedDepartment = signerForm.dataset.selectedDepartment || '';
        const selectedExpert = signerForm.dataset.selectedExpert || '';
        let expertRequest = 0;
        const loadExperts = async () => {
            if (!department || !expert || !invoiceId) return;
            const departmentId = department.value;
            expert.textContent = '';
            const placeholder = document.createElement('option');
            placeholder.value = '0';
            if (!departmentId || departmentId === '0') {
                placeholder.textContent = 'ابتدا بخش را انتخاب کنید';
                expert.appendChild(placeholder);
                expert.disabled = true;
                return;
            }
            placeholder.textContent = 'در حال بارگذاری…';
            expert.appendChild(placeholder);
            expert.disabled = true;
            const requestId = ++expertRequest;
            try {
                const response = await fetchWithTimeout(`/Invoices/Detail/${invoiceId}?handler=Experts&department=${encodeURIComponent(departmentId)}`, { cache: 'no-store' });
                if (!response.ok || requestId !== expertRequest) return;
                const people = await response.json();
                if (requestId !== expertRequest) return;
                expert.textContent = '';
                const pick = document.createElement('option');
                pick.value = '0';
                pick.textContent = people.length ? 'کارشناس را انتخاب کنید' : 'برای این بخش شخصی ثبت نشده است';
                expert.appendChild(pick);
                people.forEach(person => {
                    const option = document.createElement('option');
                    option.value = String(person.id);
                    option.textContent = person.label;
                    if (selectedExpert && String(person.id) === selectedExpert) option.selected = true;
                    expert.appendChild(option);
                });
                expert.disabled = people.length === 0;
            } catch (_) {
                if (requestId !== expertRequest) return;
                expert.textContent = '';
                const failed = document.createElement('option');
                failed.value = '0';
                failed.textContent = 'بارگذاری کارشناس ممکن نشد';
                expert.appendChild(failed);
                expert.disabled = true;
            }
        };
        department?.addEventListener('change', loadExperts);
        if (selectedDepartment && selectedDepartment !== '0') department.value = selectedDepartment;
        loadExperts();
    }

    const receiptInputs = document.querySelectorAll('[data-receipt-product]');
    if (receiptInputs.length) {
        const suggestions = document.getElementById('receipt-products');
        const loadReceiptSuggestions = async (input, term) => {
            if (term.length < 1 || term.length > 80) return;
            try {
                const response = await fetchWithTimeout('/Dispatch?handler=Products&search=' + encodeURIComponent(term));
                if (!response.ok) return;
                const products = await response.json();
                suggestions.textContent = '';
                products.forEach(product => {
                    const option = document.createElement('option');
                    option.value = product.code;
                    option.label = product.name + ' · ' + product.unit + ' · مانده ' + product.stock;
                    suggestions.appendChild(option);
                });
            } catch (_) { /* Typing a known code remains available. */ }
        };
        let receiptTimer;
        receiptInputs.forEach(input => {
            const row = input.closest('tr');
            const createNew = row?.querySelector('input[type=checkbox][name^="createNew"]');
            const syncCreateNew = () => {
                if (!createNew) return;
                const hasCode = input.value.trim().length > 0;
                createNew.disabled = hasCode;
                if (hasCode) createNew.checked = false;
            };
            input.addEventListener('input', () => {
                syncCreateNew();
                clearTimeout(receiptTimer);
                const term = input.value.trim();
                if (term.length < 2) return;
                receiptTimer = setTimeout(() => loadReceiptSuggestions(input, term), 220);
            });
            input.addEventListener('focus', () => {
                if (input.value.trim().length > 0) return;
                const title = input.getAttribute('data-invoice-title') || '';
                const term = title.trim().slice(0, 40);
                if (term.length >= 1) loadReceiptSuggestions(input, term);
            });
            createNew?.addEventListener('change', () => {
                if (createNew.checked) input.removeAttribute('required');
                else if (!input.value.trim()) input.setAttribute('required', '');
            });
            syncCreateNew();
        });
    }

    const lines = document.querySelector('[data-lines]');
    const template = document.querySelector('[data-line-template]');
    const total = document.querySelector('[data-total]');
    const addLineBtn = document.querySelector('[data-add-line]');
    if (!lines || !template || !total || !addLineBtn) return;
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
        if (total) total.textContent = currency.format(amount);
    };

    const reindex = () => {
        Array.from(lines.rows).forEach((row, index) => {
            row.querySelectorAll('[data-field]').forEach(input => {
                input.name = 'Lines[' + index + '].' + input.dataset.field;
            });
        });
        const addButton = document.querySelector('[data-add-line]');
        const limit = document.querySelector('[data-line-limit]');
        const full = lines.rows.length >= 20;
        addButton.disabled = full;
        if (limit) limit.textContent = full ? '۲۰ قلم ثبت شده؛ امکان افزودن بیشتر وجود ندارد.' : 'حداکثر ۲۰ قلم';
    };
    const addLine = () => {
        if (lines.rows.length >= 20) return;
        lines.appendChild(template.content.cloneNode(true));
        reindex();
        lines.lastElementChild.querySelector('input').focus();
    };

    addLineBtn.addEventListener('click', addLine);
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
    reindex();
    updateTotal();
})();
