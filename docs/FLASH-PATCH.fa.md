# پچ کوچک نصب (روی offline-v1.0.0)

اگر **بسته کامل v1.0.0** را دارید و فقط اسکریپت نصب خراب بود، این ZIP (~چند ده KB) کافی است.

## دانلود

- **Release:** برچسب `offline-patch-1.0.1` → `Amadban-Offline-Patch-1.0.1.zip`
- یا **Actions** → workflow **Build Windows offline patch** → Artifact

## استفاده

1. پوشهٔ قدیمی روی فلش/سرور را باز کنید (همان که `app` و `postgres` دارد).
2. ZIP پچ را **داخل همان پوشه** Extract کنید (replace).
3. **`INSTALL.bat`** → Run as administrator.

`app/` و `postgres/` را عوض نکنید.