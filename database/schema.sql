CREATE TABLE IF NOT EXISTS staff (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    username text NOT NULL UNIQUE,
    display_name text NOT NULL,
    password_hash text NOT NULL,
    role text NOT NULL CHECK (role IN ('chief','agent','warehouse','kardex')),
    active boolean NOT NULL DEFAULT true
);

CREATE TABLE IF NOT EXISTS invoices (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    submission_id uuid NOT NULL UNIQUE,
    document_number bigint UNIQUE,
    number text NOT NULL,
    vendor text NOT NULL,
    subject text NOT NULL,
    invoice_date date NOT NULL,
    kind text NOT NULL CHECK (kind IN ('cash','credit')),
    warehouse_required boolean NOT NULL,
    meeting_required boolean NOT NULL,
    account_required boolean NOT NULL,
    tax_required boolean NOT NULL,
    total bigint NOT NULL CHECK (total > 0),
    kardex_number text,
    kardex_date date,
    paid_at timestamptz,
    delivery_state text NOT NULL DEFAULT 'open' CHECK (delivery_state IN ('open','delivered','returned','closed')),
    created_by bigint NOT NULL REFERENCES staff(id),
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS invoices_recent ON invoices(invoice_date DESC, id DESC);
CREATE INDEX IF NOT EXISTS invoices_due ON invoices(invoice_date, id) WHERE paid_at IS NULL AND kind='credit';
CREATE INDEX IF NOT EXISTS invoices_document_queue ON invoices(id) WHERE document_number IS NULL;

CREATE TABLE IF NOT EXISTS invoice_lines (
    invoice_id bigint NOT NULL REFERENCES invoices(id),
    position integer NOT NULL,
    title text NOT NULL,
    unit text NOT NULL,
    quantity numeric(12,3) NOT NULL CHECK (quantity > 0),
    unit_price bigint NOT NULL CHECK (unit_price >= 0),
    amount bigint NOT NULL CHECK (amount >= 0),
    PRIMARY KEY (invoice_id, position)
);

CREATE TABLE IF NOT EXISTS counters (
    name text PRIMARY KEY,
    last_value bigint NOT NULL DEFAULT 0
);
INSERT INTO counters(name) VALUES ('document'), ('prepayment') ON CONFLICT DO NOTHING;

CREATE TABLE IF NOT EXISTS invoice_events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    invoice_id bigint NOT NULL REFERENCES invoices(id),
    actor_id bigint NOT NULL REFERENCES staff(id),
    action text NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS invoice_events_recent ON invoice_events(invoice_id, id DESC);

CREATE TABLE IF NOT EXISTS invoice_attachments (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    invoice_id bigint NOT NULL REFERENCES invoices(id),
    kind text NOT NULL CHECK (kind IN ('invoice','pos','meeting','account','taxpayer','tax','bank-slip','proxy','check')),
    filename text NOT NULL,
    content_type text NOT NULL CHECK (content_type IN ('application/pdf','image/jpeg','image/png')),
    content bytea NOT NULL,
    uploaded_by bigint NOT NULL REFERENCES staff(id),
    uploaded_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS invoice_attachments_lookup ON invoice_attachments(invoice_id,kind);

ALTER TABLE invoices ADD COLUMN IF NOT EXISTS proxy_required boolean NOT NULL DEFAULT false;

CREATE TABLE IF NOT EXISTS credit_codes (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code text NOT NULL UNIQUE,
    title text NOT NULL
);
CREATE TABLE IF NOT EXISTS credit_changes (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    source_id bigint REFERENCES credit_codes(id),
    destination_id bigint NOT NULL REFERENCES credit_codes(id),
    amount bigint NOT NULL CHECK (amount > 0),
    actor_id bigint NOT NULL REFERENCES staff(id),
    occurred_at timestamptz NOT NULL DEFAULT now(),
    CHECK (source_id IS DISTINCT FROM destination_id)
);
CREATE INDEX IF NOT EXISTS credit_changes_source ON credit_changes(source_id);
CREATE INDEX IF NOT EXISTS credit_changes_destination ON credit_changes(destination_id);
CREATE TABLE IF NOT EXISTS prepayments (
    number bigint PRIMARY KEY,
    invoice_id bigint REFERENCES invoices(id),
    proforma_number text,
    vendor text NOT NULL,
    code_id bigint NOT NULL REFERENCES credit_codes(id),
    owner text NOT NULL,
    bank text NOT NULL,
    sheba text NOT NULL,
    amount bigint NOT NULL CHECK (amount > 0),
    state text NOT NULL CHECK (state IN ('reserved','paid','void')),
    created_by bigint NOT NULL REFERENCES staff(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    CHECK (invoice_id IS NOT NULL OR proforma_number IS NOT NULL)
);
CREATE UNIQUE INDEX IF NOT EXISTS prepayment_active_invoice ON prepayments(invoice_id) WHERE state<>'void';
CREATE INDEX IF NOT EXISTS prepayments_code ON prepayments(code_id,state);

CREATE TABLE IF NOT EXISTS notices (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    actor_id bigint NOT NULL REFERENCES staff(id),
    message text NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT now()
);

CREATE OR REPLACE VIEW credit_balance AS
SELECT c.id,c.code,c.title,
    (COALESCE(incoming.total,0) - COALESCE(outgoing.total,0))::bigint AS allocated,
    COALESCE(payments.reserved,0)::bigint AS reserved, COALESCE(payments.paid,0)::bigint AS paid
FROM credit_codes c
LEFT JOIN (SELECT destination_id, sum(amount) total FROM credit_changes GROUP BY destination_id) incoming ON incoming.destination_id=c.id
LEFT JOIN (SELECT source_id, sum(amount) total FROM credit_changes WHERE source_id IS NOT NULL GROUP BY source_id) outgoing ON outgoing.source_id=c.id
LEFT JOIN (
    SELECT code_id,sum(amount) FILTER (WHERE state='reserved') reserved,sum(amount) FILTER (WHERE state='paid') paid
    FROM prepayments WHERE state<>'void' GROUP BY code_id
) payments ON payments.code_id=c.id;

ALTER TABLE invoices ADD COLUMN IF NOT EXISTS paid_by bigint REFERENCES staff(id);

CREATE TABLE IF NOT EXISTS products (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code text NOT NULL UNIQUE,
    name text NOT NULL,
    unit text NOT NULL,
    stock numeric(12,3) NOT NULL DEFAULT 0 CHECK (stock >= 0),
    minimum numeric(12,3) NOT NULL DEFAULT 0 CHECK (minimum >= 0)
);
CREATE TABLE IF NOT EXISTS stock_events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    submission_id uuid NOT NULL UNIQUE,
    product_id bigint NOT NULL REFERENCES products(id),
    invoice_id bigint REFERENCES invoices(id),
    kind text NOT NULL CHECK (kind IN ('opening','in','out')),
    delta numeric(12,3) NOT NULL CHECK (delta <> 0),
    department text,
    actor_id bigint NOT NULL REFERENCES staff(id),
    event_date date NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS stock_opening_once ON stock_events(product_id) WHERE kind='opening';
CREATE INDEX IF NOT EXISTS stock_events_by_product ON stock_events(product_id,id DESC);
CREATE INDEX IF NOT EXISTS stock_events_invoice ON stock_events(invoice_id) WHERE invoice_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS petty_receipts (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    submission_id uuid NOT NULL UNIQUE,
    recipient_id bigint NOT NULL REFERENCES staff(id),
    receipt_number text NOT NULL,
    receipt_date date NOT NULL,
    amount bigint NOT NULL CHECK (amount > 0),
    actor_id bigint NOT NULL REFERENCES staff(id)
);
CREATE INDEX IF NOT EXISTS petty_receipts_recipient ON petty_receipts(recipient_id,receipt_date);

CREATE TABLE IF NOT EXISTS bank_checks (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    sayad text NOT NULL UNIQUE,
    account text NOT NULL,
    bank text NOT NULL,
    branch text NOT NULL,
    check_date date NOT NULL,
    amount bigint NOT NULL CHECK (amount > 0),
    recipient text NOT NULL,
    created_by bigint NOT NULL REFERENCES staff(id)
);
CREATE INDEX IF NOT EXISTS bank_checks_recent ON bank_checks(check_date DESC,id DESC);

CREATE TABLE IF NOT EXISTS suppliers (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL,
    kind text NOT NULL CHECK (kind IN ('person','company')),
    national_code text NOT NULL DEFAULT '',
    economic_code text NOT NULL DEFAULT '',
    phone text NOT NULL DEFAULT '',
    address text NOT NULL DEFAULT '',
    UNIQUE(name)
);
CREATE INDEX IF NOT EXISTS suppliers_search_name ON suppliers(lower(name));
CREATE TABLE IF NOT EXISTS supplier_accounts (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    supplier_id bigint NOT NULL REFERENCES suppliers(id),
    owner text NOT NULL,
    bank text NOT NULL,
    account text NOT NULL DEFAULT '',
    sheba text NOT NULL,
    UNIQUE(supplier_id,sheba)
);
CREATE TABLE IF NOT EXISTS supplier_documents (
    supplier_id bigint NOT NULL REFERENCES suppliers(id),
    kind text NOT NULL CHECK (kind IN ('account','taxpayer')),
    filename text NOT NULL,
    content_type text NOT NULL CHECK (content_type IN ('application/pdf','image/jpeg','image/png')),
    content bytea NOT NULL,
    actor_id bigint NOT NULL REFERENCES staff(id),
    uploaded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY(supplier_id,kind)
);

CREATE TABLE IF NOT EXISTS print_settings (
    staff_id bigint NOT NULL REFERENCES staff(id),
    form text NOT NULL CHECK(form IN ('document','prepayment')),
    template text NOT NULL,
    PRIMARY KEY(staff_id,form)
);

ALTER TABLE invoices ADD COLUMN IF NOT EXISTS cash_credit_code bigint REFERENCES credit_codes(id);
ALTER TABLE prepayments ADD COLUMN IF NOT EXISTS permit_number text NOT NULL DEFAULT '';
ALTER TABLE prepayments ADD COLUMN IF NOT EXISTS permit_date text NOT NULL DEFAULT '';
ALTER TABLE prepayments ADD COLUMN IF NOT EXISTS tax_amount bigint NOT NULL DEFAULT 0 CHECK(tax_amount>=0);

CREATE TABLE IF NOT EXISTS credit_reclassifications (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    prepayment_number bigint NOT NULL REFERENCES prepayments(number),
    source_id bigint NOT NULL REFERENCES credit_codes(id),
    destination_id bigint NOT NULL REFERENCES credit_codes(id),
    amount bigint NOT NULL CHECK (amount>0),
    actor_id bigint NOT NULL REFERENCES staff(id),
    occurred_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE credit_changes ADD COLUMN IF NOT EXISTS source_before bigint;
ALTER TABLE credit_changes ADD COLUMN IF NOT EXISTS destination_before bigint;

CREATE TABLE IF NOT EXISTS branding (
    code text PRIMARY KEY CHECK(code IN ('faraja','health')),
    content_type text NOT NULL CHECK(content_type IN ('image/png','image/jpeg')),
    content bytea NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS signers (
    role text PRIMARY KEY,
    name text NOT NULL
);
