# بسته آفلاین آمادبان — دانلود از GitHub و نصب با یک کلیک

روی **سرور بیمارستان اینترنت نیست**. شما **یک بار** از GitHub (با اینترنت) بسته را می‌گیرید، روی **فلش** می‌ریزید، روی سرور فقط **`نصب-آمادبان.bat`** را اجرا می‌کنید. هیچ دانلودی روی سرور انجام نمی‌شود.

## روش پیشنهادی: دانلود ZIP آماده از GitHub

همه‌چیز (برنامه + PostgreSQL + VC++ + اسکریپت نصب) داخل **یک فایل ZIP** ساخته می‌شود:

### ۱) گرفتن فایل

- برو به مخزن **[mmovasseghi/AMADBAN](https://github.com/mmovasseghi/AMADBAN)** → تب **Actions**
- workflow با نام **Build Windows offline flash bundle** → **Run workflow** → Run
- بعد از سبز شدن، پایین همان اجرا → **Artifacts** → **`Amadban-Offline-Windows.zip`** را دانلود کن

یا اگر **Release** ساخته شده (برچسب `v...` یا `offline-...`):

- تب **Releases** → `Amadban-Offline-Windows.zip`

### ۲) فلش

1. ZIP را باز کن (Extract).
2. کل پوشهٔ داخلش (مثلاً `Amadban-Offline-...`) را روی فلش کپی کن.
3. باید این‌ها را ببینی:
   - `نصب-آمادبان.bat`
   - `app/` (برنامه self-contained)
   - `postgres/postgresql-17.4-1-windows-x64.exe`
   - `postgres/vcredist/VC_redist.x64.exe`
   - `install/`

### ۳) سرور Windows Server 2019 (LAN)

1. فلش را وصل کن، پوشه را روی دیسک سرور کپی کن (مثلاً `D:\AmadbanSetup`).
2. **`نصب-آمادبان.bat`** → راست‌کلیک → **Run as administrator**
3. مرورگر از PC دیگر: `http://<IP-سرور>:8088/Login`  
   - کاربر: **`admin`**  
   - رمز: `C:\Amadban\config\service.env` → `AMADBAN_ADMIN_PASSWORD`

## ساخت دستی همان بسته (اختیاری)

اگر خودت از سورس می‌سازی:

```powershell
.\scripts\windows\offline\Build-OfflineBundle.ps1 -DownloadPrerequisites
```

یا Linux/macOS:

```bash
./scripts/package-windows-offline.sh
```

## محتوا

| بخش | نقش |
|------|------|
| `app/` | آمادبان + .NET داخل خود exe (روی سرور dotnet نصب نکنید) |
| `postgres/` | نصب‌گر PostgreSQL آفلاین |
| `install/` | PowerShell نصب خودکار |
| `wwwroot` داخل app | فونت و آیکون؛ **بدون CDN** |

## به‌روزرسانی

نسخهٔ جدید ZIP از GitHub بگیرید، دوباره `نصب-آمادبان.bat` (دادهٔ قبلی در `C:\Amadban` و PostgreSQL می‌ماند مگر خودت پاک کنید).

راهنمای کامل: [docs/deployment-windows-server-2019-lan.md](../docs/deployment-windows-server-2019-lan.md)