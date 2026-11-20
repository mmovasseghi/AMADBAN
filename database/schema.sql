CREATE TABLE IF NOT EXISTS staff (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    username text NOT NULL UNIQUE,
    display_name text NOT NULL,
    password_hash text NOT NULL,
    role text NOT NULL CHECK (role IN ('chief','agent','warehouse','kardex')),
    active boolean NOT NULL DEFAULT true,
    security_stamp uuid NOT NULL DEFAULT gen_random_uuid()
);
ALTER TABLE staff ADD COLUMN IF NOT EXISTS security_stamp uuid NOT NULL DEFAULT gen_random_uuid();
CREATE TABLE IF NOT EXISTS staff_events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    actor_id bigint NOT NULL REFERENCES staff(id),
    target_id bigint NOT NULL REFERENCES staff(id),
    action text NOT NULL CHECK (action IN ('reset-password','activate','deactivate')),
    occurred_at timestamptz NOT NULL DEFAULT now()
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
    pos_in_invoice boolean NOT NULL DEFAULT false,
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
ALTER TABLE invoices ADD COLUMN IF NOT EXISTS pos_in_invoice boolean NOT NULL DEFAULT false;

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
INSERT INTO counters(name) VALUES ('warehouse-product') ON CONFLICT DO NOTHING;
INSERT INTO counters(name) VALUES ('stock-issue') ON CONFLICT DO NOTHING;

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
ALTER TABLE prepayments ADD COLUMN IF NOT EXISTS submission_id uuid;
CREATE UNIQUE INDEX IF NOT EXISTS prepayments_submission ON prepayments(submission_id) WHERE submission_id IS NOT NULL;

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
CREATE TABLE IF NOT EXISTS departments (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL UNIQUE,
    active boolean NOT NULL DEFAULT true
);
CREATE TABLE IF NOT EXISTS people (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL,
    position text NOT NULL DEFAULT '',
    department_id bigint REFERENCES departments(id),
    active boolean NOT NULL DEFAULT true
);
CREATE INDEX IF NOT EXISTS people_by_department ON people(department_id,name) WHERE active;
CREATE TABLE IF NOT EXISTS stock_issues (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    submission_id uuid NOT NULL UNIQUE,
    number bigint NOT NULL UNIQUE,
    department_id bigint NOT NULL REFERENCES departments(id),
    recipient_id bigint NOT NULL REFERENCES people(id),
    warehouse_person_id bigint NOT NULL REFERENCES staff(id),
    actor_id bigint NOT NULL REFERENCES staff(id),
    issue_date date NOT NULL,
    note text NOT NULL DEFAULT '',
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS stock_issue_lines (
    issue_id bigint NOT NULL REFERENCES stock_issues(id),
    position integer NOT NULL,
    product_id bigint NOT NULL REFERENCES products(id),
    quantity numeric(12,3) NOT NULL CHECK(quantity>0),
    stock_before numeric(12,3) NOT NULL,
    PRIMARY KEY(issue_id,position)
);
CREATE INDEX IF NOT EXISTS stock_issues_recent ON stock_issues(issue_date DESC,id DESC);
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

CREATE TABLE IF NOT EXISTS warehouse_receipts (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    invoice_id bigint NOT NULL REFERENCES invoices(id),
    state text NOT NULL CHECK (state IN ('active','void')),
    receipt_date date NOT NULL,
    actor_id bigint NOT NULL REFERENCES staff(id),
    void_at timestamptz,
    void_by bigint REFERENCES staff(id)
);
CREATE UNIQUE INDEX IF NOT EXISTS warehouse_receipts_active ON warehouse_receipts(invoice_id) WHERE state='active';
ALTER TABLE stock_events ADD COLUMN IF NOT EXISTS receipt_id bigint REFERENCES warehouse_receipts(id);
ALTER TABLE stock_events ADD COLUMN IF NOT EXISTS issue_id bigint REFERENCES stock_issues(id);
CREATE INDEX IF NOT EXISTS stock_events_receipt ON stock_events(receipt_id) WHERE receipt_id IS NOT NULL;

-- This migration runs once on an installation created before stocktaking was added.
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name='stock_events' AND column_name='before_quantity') THEN
        ALTER TABLE stock_events DROP CONSTRAINT IF EXISTS stock_events_kind_check;
        ALTER TABLE stock_events ADD CONSTRAINT stock_events_kind_check CHECK (kind IN ('opening','in','out','count'));
        ALTER TABLE stock_events DROP CONSTRAINT IF EXISTS stock_events_delta_check;
        ALTER TABLE stock_events ADD CONSTRAINT stock_events_delta_check CHECK (kind IN ('opening','count') OR delta <> 0);
        ALTER TABLE stock_events ADD COLUMN before_quantity numeric(12,3);
        ALTER TABLE stock_events ADD COLUMN counted_quantity numeric(12,3);
    END IF;
END $$;

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
ALTER TABLE signers ADD COLUMN IF NOT EXISTS person_id bigint REFERENCES people(id);

CREATE TABLE IF NOT EXISTS activity_feed (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    actor_id bigint NOT NULL REFERENCES staff(id),
    title text NOT NULL,
    summary text NOT NULL,
    target_kind text NOT NULL CHECK (target_kind IN ('invoice','stock','dispatch','credit','petty','bank','staff','supplier','organization')),
    target_id bigint,
    occurred_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS activity_feed_recent ON activity_feed(id DESC);
CREATE TABLE IF NOT EXISTS activity_reads (
    staff_id bigint PRIMARY KEY REFERENCES staff(id),
    last_seen_id bigint NOT NULL DEFAULT 0
);

CREATE OR REPLACE FUNCTION feed_invoice_event() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE item invoices%ROWTYPE;
BEGIN
    SELECT * INTO item FROM invoices WHERE id=NEW.invoice_id;
    INSERT INTO activity_feed(actor_id,title,summary,target_kind,target_id)
    VALUES(NEW.actor_id,NEW.action,
           left('سند ' || coalesce(item.document_number::text,'بدون شماره') || ' · ' || item.subject || ' · ' || item.vendor,300),
           'invoice',NEW.invoice_id);
    RETURN NEW;
END $$;

CREATE OR REPLACE FUNCTION feed_stock_event() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE item products%ROWTYPE;
BEGIN
    IF NEW.invoice_id IS NOT NULL OR NEW.issue_id IS NOT NULL THEN RETURN NEW; END IF;
    SELECT * INTO item FROM products WHERE id=NEW.product_id;
    INSERT INTO activity_feed(actor_id,title,summary,target_kind,target_id)
    VALUES(NEW.actor_id,CASE NEW.kind WHEN 'opening' THEN 'شمارش افتتاحیه انبار'
                 WHEN 'count' THEN 'انبارگردانی و ثبت مغایرت'
                 WHEN 'in' THEN 'ورود کالا به انبار' ELSE 'خروج دستی کالا از انبار' END,
           left(item.name || ' · ' || abs(NEW.delta)::text || ' ' || item.unit ||
                coalesce(' · ' || NEW.department,''),300), 'stock',NEW.product_id);
    RETURN NEW;
END $$;

CREATE OR REPLACE FUNCTION feed_dispatch_event() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO activity_feed(actor_id,title,summary,target_kind,target_id)
    SELECT NEW.actor_id,'حواله خروج انبار شماره ' || NEW.number,
           left('به ' || d.name || ' · تحویل‌گیرنده ' || p.name,300),'dispatch',NEW.number
    FROM departments d JOIN people p ON p.id=NEW.recipient_id WHERE d.id=NEW.department_id;
    RETURN NEW;
END $$;

CREATE OR REPLACE FUNCTION feed_credit_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO activity_feed(actor_id,title,summary,target_kind)
    SELECT NEW.actor_id,CASE WHEN NEW.source_id IS NULL THEN 'افزایش اعتبار' ELSE 'انتقال اعتبار' END,
           left(coalesce(src.code || ' ← ','') || dst.code || ' · ' || NEW.amount || ' ریال',300),'credit'
    FROM credit_codes dst LEFT JOIN credit_codes src ON src.id=NEW.source_id WHERE dst.id=NEW.destination_id;
    RETURN NEW;
END $$;

CREATE OR REPLACE FUNCTION feed_prepayment_event() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP='INSERT' AND NEW.invoice_id IS NULL THEN
        INSERT INTO activity_feed(actor_id,title,summary,target_kind)
        VALUES(NEW.created_by,'پیش‌پرداخت پیش‌فاکتور شماره ' || NEW.number,
               left(NEW.vendor || ' · ' || NEW.amount || ' ریال',300),'credit');
    ELSIF TG_OP='UPDATE' AND NEW.invoice_id IS NULL AND OLD.state IS DISTINCT FROM NEW.state THEN
        INSERT INTO activity_feed(actor_id,title,summary,target_kind)
        VALUES(NEW.created_by,CASE NEW.state WHEN 'paid' THEN 'پرداخت پیش‌فاکتور' ELSE 'ابطال پیش‌پرداخت' END,
               left('پیش‌پرداخت ' || NEW.number || ' · ' || NEW.vendor,300),'credit');
    END IF;
    RETURN NEW;
END $$;

CREATE OR REPLACE FUNCTION feed_simple_event() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_TABLE_NAME='petty_receipts' THEN
        INSERT INTO activity_feed(actor_id,title,summary,target_kind)
        VALUES(NEW.actor_id,'دریافت تنخواه ثبت شد',
               left('شماره ' || NEW.receipt_number || ' · ' || NEW.amount || ' ریال',300),'petty');
    ELSIF TG_TABLE_NAME='bank_checks' THEN
        INSERT INTO activity_feed(actor_id,title,summary,target_kind)
        VALUES(NEW.created_by,'چک بانکی ثبت شد',left(NEW.bank || ' · ' || NEW.recipient || ' · ' || NEW.amount || ' ریال',300),'bank');
    ELSIF TG_TABLE_NAME='staff_events' THEN
        INSERT INTO activity_feed(actor_id,title,summary,target_kind,target_id)
        VALUES(NEW.actor_id,CASE NEW.action WHEN 'activate' THEN 'فعال‌سازی حساب' WHEN 'deactivate' THEN 'غیرفعال‌سازی حساب' ELSE 'تغییر رمز حساب' END,
               (SELECT display_name FROM staff WHERE id=NEW.target_id),'staff',NEW.target_id);
    ELSIF TG_TABLE_NAME='supplier_documents' THEN
        INSERT INTO activity_feed(actor_id,title,summary,target_kind,target_id)
        VALUES(NEW.actor_id,'مدرک فروشنده ثبت شد',
               (SELECT name FROM suppliers WHERE id=NEW.supplier_id) || ' · ' || NEW.kind,'supplier',NEW.supplier_id);
    END IF;
    RETURN NEW;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname='activity_invoice_event') THEN
        CREATE TRIGGER activity_invoice_event AFTER INSERT ON invoice_events FOR EACH ROW EXECUTE FUNCTION feed_invoice_event();
        CREATE TRIGGER activity_stock_event AFTER INSERT ON stock_events FOR EACH ROW EXECUTE FUNCTION feed_stock_event();
        CREATE TRIGGER activity_dispatch_event AFTER INSERT ON stock_issues FOR EACH ROW EXECUTE FUNCTION feed_dispatch_event();
        CREATE TRIGGER activity_credit_change AFTER INSERT ON credit_changes FOR EACH ROW EXECUTE FUNCTION feed_credit_change();
        CREATE TRIGGER activity_prepayment_event AFTER INSERT OR UPDATE ON prepayments FOR EACH ROW EXECUTE FUNCTION feed_prepayment_event();
        CREATE TRIGGER activity_petty_event AFTER INSERT ON petty_receipts FOR EACH ROW EXECUTE FUNCTION feed_simple_event();
        CREATE TRIGGER activity_bank_event AFTER INSERT ON bank_checks FOR EACH ROW EXECUTE FUNCTION feed_simple_event();
        CREATE TRIGGER activity_staff_event AFTER INSERT ON staff_events FOR EACH ROW EXECUTE FUNCTION feed_simple_event();
        CREATE TRIGGER activity_supplier_document AFTER INSERT OR UPDATE ON supplier_documents FOR EACH ROW EXECUTE FUNCTION feed_simple_event();
    END IF;
END $$;
