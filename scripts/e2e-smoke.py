#!/usr/bin/env python3
"""Smoke/E2E checks against live Amadban (HTTP + HTML assertions)."""
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
import http.cookiejar

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://185.93.88.81:8088"
PASS = "amadban12345678"
USERS = {"admin": "chief", "ofis": "agent", "anbardar": "warehouse", "kardex1": "kardex"}

DENY_MARKERS = ("Exception", "NullReference", "500 Internal", "SqlException", "developer exception")
PAGES_ALL = [
    "/Index", "/Stock", "/Dispatch", "/Departments", "/KardexQueue", "/WarehouseReport",
    "/Invoices/New", "/Credit", "/Analytics", "/Suppliers", "/Bank", "/Login",
]
PAGES_FINANCE = ["/Credit", "/Petty", "/TaxBatch", "/Activity", "/Organization", "/Staff"]


def login(user: str) -> tuple[urllib.request.OpenerDirector, http.cookiejar.CookieJar]:
    cj = http.cookiejar.CookieJar()
    op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj))
    html = op.open(BASE + "/Login").read().decode("utf-8", "replace")
    m = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', html)
    if not m:
        raise RuntimeError("login token missing")
    data = urllib.parse.urlencode(
        {"__RequestVerificationToken": m.group(1), "UserName": user, "Password": PASS}
    ).encode()
    op.open(urllib.request.Request(BASE + "/Login", data=data, method="POST"))
    return op, cj


def fetch(op, path: str) -> tuple[int, str]:
    try:
        r = op.open(BASE + path)
        body = r.read(2_000_000).decode("utf-8", "replace")
        return r.getcode(), body
    except urllib.error.HTTPError as e:
        body = e.read(200_000).decode("utf-8", "replace")
        return e.code, body


def antiforgery(op, path: str) -> tuple[str, str]:
    code, body = fetch(op, path)
    m = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', body)
    return (m.group(1) if m else ""), body


class Issue:
    def __init__(self, sev: str, who: str, detail: str):
        self.sev, self.who, self.detail = sev, who, detail


issues: list[Issue] = []


def add(sev, who, detail):
    issues.append(Issue(sev, who, detail))


for user, role in USERS.items():
    op, cj = login(user)
    code, index_html = fetch(op, "/Index")
    if code >= 500:
        add("high", user, f"/Index returned {code}")
    for mk in DENY_MARKERS:
        if mk.lower() in index_html.lower():
            add("high", user, f"/Index contains {mk}")
    if role in ("agent", "chief") and "پیش‌پرداخت" not in index_html:
        add("high", user, "prepayment menu link missing from layout")

    paths = PAGES_ALL + (PAGES_FINANCE if role in ("chief", "agent") else [])
    if role == "chief":
        paths += PAGES_FINANCE
    for path in paths:
        code, html = fetch(op, path)
        if code >= 500:
            add("high", user, f"{path} -> {code}")
        if role in ("warehouse", "kardex") and path in ("/Analytics", "/Invoices/New") and code == 200:
            if "گزارش" in path or "فاکتور" in html[:500]:
                if path == "/Analytics" and "<h1>" in html:
                    add("high", user, f"{path} should be denied but rendered page")
        for mk in DENY_MARKERS:
            if mk.lower() in html.lower():
                add("high", user, f"{path} contains {mk}")

    code, stock = fetch(op, "/Stock")
    has_manual = "handler=Move" in stock or "page-handler=\"Move\"" in stock
    has_product_post = "handler=Product" in stock or "تعریف کالا" in stock and "CanEdit" not in stock
    has_chief_notice = "فقط با حساب" in stock and "رئیس آماد" in stock
    if role == "warehouse":
        if has_manual or has_product_post:
            add("high", user, "Stock shows manual edit forms for warehouse")
        if not has_chief_notice and "/Stock" in stock:
            add("medium", user, "Stock missing read-only notice for warehouse")
    if role == "chief" and not (has_product_post or "handler=Product" in stock):
        add("high", user, "Stock missing chief product form")

    if user == "ofis":
        _, credit = fetch(op, "/Credit")
        if (
            "پیش‌پرداخت روی سند" not in credit
            and "ثبت و چاپ" not in credit
        ):
            add("high", "ofis", "Credit page missing prepayment create form")
        _, detail = fetch(op, "/Index")
        if "باز کردن پرونده" not in detail and "مشاهده" not in detail:
            pass
        for path in ("/Bank", "/Petty", "/Suppliers"):
            code, nav = fetch(op, path)
            if code >= 500:
                add("high", "ofis", f"{path} -> {code}")
    if user == "ofis":
        _, layout_credit = fetch(op, "/Credit")
        if "گزارش و تنظیمات" not in layout_credit and "گزارش فاکتورها" not in layout_credit:
            add("medium", "ofis", "flattened finance nav may be missing on deployed build")
        if "PrintPrepayment" not in credit and "/PrintPrepayment" not in credit and "چاپ" not in credit:
            pass  # print route not on list page until prepayment exists

    if user == "anbardar":
        token, _ = antiforgery(op, "/Stock")
        if token:
            post = urllib.parse.urlencode(
                {
                    "__RequestVerificationToken": token,
                    "productId": "1",
                    "submission": "00000000-0000-0000-0000-000000000001",
                    "invoice": "0",
                    "kind": "in",
                    "quantity": "1",
                    "date": "1404/01/01",
                    "department": "x",
                }
            ).encode()
            req = urllib.request.Request(
                BASE + "/Stock?handler=Move&productId=1", data=post, method="POST"
            )
            class NR(urllib.request.HTTPRedirectHandler):
                def redirect_request(self, req, fp, code, msg, headers, newurl):
                    return None

            op_nr = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj), NR())
            try:
                op_nr.open(req)
                add("high", "anbardar", "Stock Move POST should forbid")
            except urllib.error.HTTPError as e:
                if e.code not in (302, 403, 401):
                    add("high", "anbardar", f"Stock Move POST unexpected {e.code}")

    if user == "kardex1":
        class NR2(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, req, fp, code, msg, headers, newurl):
                return None
        op_k = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj), NR2())
        try:
            op_k.open(BASE + "/Invoices/Detail/1?handler=Experts&department=1").read()
            add("high", "kardex1", "Experts handler should deny")
        except urllib.error.HTTPError as e:
            if e.code not in (302, 403):
                add("high", "kardex1", f"Experts unexpected {e.code}")

# invoice link from index
op, _cj = login("ofis")
_, idx = fetch(op, "/Index")
links = re.findall(r'/Invoices/Detail/(\d+)', idx)
if not links:
    add("medium", "ofis", "No invoice rows on Index to open")
else:
    iid = links[0]
    code, det = fetch(op, f"/Invoices/Detail/{iid}")
    if code >= 500:
        add("high", "ofis", f"Detail/{iid} -> {code}")
    if "پرونده" not in det and code == 200:
        add("medium", "ofis", f"Detail/{iid} unexpected content")

print(f"Checked BASE={BASE}")
print(f"Issues: {len(issues)}")
for i in issues:
    print(f"  [{i.sev}] {i.who}: {i.detail}")
sys.exit(1 if any(x.sev == "high" for x in issues) else 0)