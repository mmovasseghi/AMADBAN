# نصب آمادبان روی Windows Server 2019 (شبکه LAN داخلی)

این راهنما برای میزبان **ویندوز سرور ۲۰۱۹** در بیمارستان است؛ کاربران فقط از **مرورگر داخل LAN** وارد می‌شوند (مثلاً `http://192.168.x.x:8088`).

## پیش‌نیازهای سخت‌افزار و سیستم

| مورد | پیشنهاد |
|------|---------|
| سیستم‌عامل | Windows Server 2019 Standard/Datacenter (Desktop Experience برای نصب آسان‌تر) |
| حافظه | حداقل ۴ GB RAM آزاد (۸ GB راحت‌تر) |
| دیسک | ۲۰ GB فضای خالی برای برنامه + پایگاه + پشتیبان |
| شبکه | کارت شبکه در LAN بیمارستان؛ IP ثابت برای سرور (مثال `192.168.10.50`) |
| نام دامنه | اجباری نیست؛ با IP یا نام NetBIOS کار می‌کند |

## نصب یک‌کلیک آفلاین (پیشنهاد برای سرور بدون اینترنت)

روی **سرور LAN هیچ چیز از اینترنت دانلود نمی‌شود**. یک بار روی PC متصل به نت بسته بسازید، USB ببرید، روی سرور فقط **یک بار کلیک**.

### مرحله الف — دانلود ZIP کامل از GitHub (پیشنهاد)

همهٔ پیش‌نیازها داخل ZIP است؛ **نیازی به دانلود جداگانه PostgreSQL نیست.**

1. [مخزن AMADBAN](https://github.com/mmovasseghi/AMADBAN) → **Actions** → **Build Windows offline flash bundle** → **Run workflow**
2. بعد از اتمام → **Artifacts** → `Amadban-Offline-Windows.zip`
3. ZIP را باز کنید و کل پوشه را روی **فلش** بگذارید.

(جایگزین: ساخت محلی با `.\scripts\windows\offline\Build-OfflineBundle.ps1 -DownloadPrerequisites`)

### مرحله ب — نصب روی Windows Server 2019 (فقط یک کلیک)

1. پوشه را روی سرور باز کنید.
2. **`نصب-آمادبان.bat`** → راست‌کلیک → **Run as administrator**
3. منتظر بمانید تا PostgreSQL + سرویس **Amadban** + فایروال پورت **8088** آماده شود.
4. از PCهای LAN: `http://<IP-ثابت-سرور>:8088/Login` — کاربر `admin`، رمز در `C:\Amadban\config\service.env`.

جزئیات و ساختار بسته: [bundle/README.md](../bundle/README.md).

---

## پیش‌نیازهای نرم‌افزار (نصب دستی — اگر یک‌کلیک نمی‌خواهید)

### ۱) به‌روزرسانی ویتوز

- Windows Update تا آخرین بسته‌های امنیتی.
- ساعت و تاریخ درست (برای کوکی و گزارش‌ها).

### ۲) .NET 10 (ASP.NET Core Runtime)

برنامه روی **.NET 10** است.

1. از [دانلود .NET](https://dotnet.microsoft.com/download/dotnet/10.0) بسته **ASP.NET Core Runtime 10.x (Windows x64)** را بگیرید.
2. برای نصب روی سرور **بدون اینترنت**: همان فایل `.exe` را از PC دیگر کپی کنید.
3. برای **انتشار روی خود سرور** (اختیاری): **.NET SDK 10.x** به‌جای فقط Runtime.

بررسی در PowerShell (مدیر):

```powershell
dotnet --list-runtimes
```

باید خطی شبیه `Microsoft.AspNetCore.App 10.x` ببینید.

### ۳) PostgreSQL

پایگاه داده **PostgreSQL** است (نه SQL Server).

1. نصب [PostgreSQL 17 for Windows](https://www.postgresql.org/download/windows/) (یا 16).
2. پورت پیش‌فرض `5432` روی همان سرور.
3. فقط **localhost** گوش دهد مگر IT خلاف آن خواسته باشد.

ایجاد کاربر و پایگاه (در `psql` یا pgAdmin):

```sql
CREATE USER amadban WITH PASSWORD 'رمز-قوی-جداگانه';
CREATE DATABASE amadban OWNER amadban;
```

اسکیمای اولیه هنگام **اولین اجرای برنامه** از `database/schema.sql` داخل پوشه publish اعمال می‌شود.

### ۴) فایروال ویندوز (برای LAN)

پورت پیش‌فرض سامانه: **8088** (قابل تغییر).

```powershell
New-NetFirewallRule -DisplayName "Amadban HTTP (LAN)" `
  -Direction Inbound -Protocol TCP -LocalPort 8088 -Action Allow
```

اگر فقط یک subnet خاص: در Group Policy یا `-RemoteAddress` محدود کنید (با هماهنگی IT).

### ۵) گواهی HTTPS (اختیاری ولی توصیه IT)

- برای LAN می‌توان موقتاً **HTTP** استفاده کرد.
- برای تولید: گواهی داخلی بیمارستان روی **IIS** یا reverse proxy؛ در این سند مسیر ساده **Kestrel روی 8088** آمده است.

## ساختار پوشه روی سرور

| مسیر | کاربرد |
|------|--------|
| `C:\Amadban\publish` | خروجی `dotnet publish` |
| `C:\Amadban\keys` | کلیدهای Data Protection (نشست ورود؛ **پشتیبان بگیرید**) |
| `C:\Amadban\config\service.env` | رمزها و تنظیمات (فقط مدیر سرور بخواند) |
| `C:\Amadban\backup` | پشتیبان `pg_dump` (دستی یا زمان‌بندی) |

## نصب خودکار (پیشنهادی)

از مخزن، پوشه `scripts\windows` را روی سرور کپی کنید.

1. **تنظیم رمزها**

   ```powershell
   Copy-Item C:\Amadban\config\service.env.example C:\Amadban\config\service.env
   notepad C:\Amadban\config\service.env
   ```

2. **کد برنامه** — یا `git clone` در `C:\Amadban\src` یا فایل zip انتشار از `main`.

3. **اجرای نصب** (PowerShell **Run as Administrator**):

   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass
   cd C:\Amadban\scripts\windows
   .\Install-Amadban.ps1 -SourcePath C:\Amadban\src -LanPort 8088
   ```

   اسکریپت: publish، فایروال، متغیر محیط سرویس، ثبت سرویس Windows به نام **Amadban**.

4. **شروع سرویس**

   ```powershell
   Start-Service Amadban
   Get-Service Amadban
   ```

5. **تست روی خود سرور**

   ```powershell
   Invoke-WebRequest -Uri http://127.0.0.1:8088/Login -UseBasicParsing | Select-Object StatusCode
   ```

6. **تست از PC دیگر LAN**

   در مرورگر: `http://IP-سرور:8088/Login`  
   ورود اولیه: کاربر `admin` و رمزی که در `AMADBAN_ADMIN_PASSWORD` گذاشته‌اید (فقط وقتی پایگاه تازه است).

## به‌روزرسانی نسخه

```powershell
cd C:\Amadban\scripts\windows
.\Install-Amadban.ps1 -SourcePath C:\Amadban\src -LanPort 8088
Restart-Service Amadban
```

## پشتیبان پایگاه

```powershell
$env:PGPASSWORD = 'همان-رمز-amadban'
& "C:\Program Files\PostgreSQL\17\bin\pg_dump.exe" -h localhost -U amadban -d amadban -Fc `
  -f "C:\Amadban\backup\amadban-$(Get-Date -Format yyyyMMdd-HHmm).dump"
```

## متغیرهای محیط (مهم)

| متغیر | معنی |
|--------|------|
| `ASPNETCORE_URLS` | `http://0.0.0.0:8088` — گوش دادن روی همه IP برای LAN |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `AMADBAN_DB_HOST` | معمولاً `127.0.0.1` |
| `AMADBAN_DB_PASSWORD` | رمز کاربر PostgreSQL |
| `AMADBAN_ADMIN_PASSWORD` | رمز اول مدیر (یک‌بار روی DB خالی) |
| `AMADBAN_KEYS` | `C:\Amadban\keys` |

## مسیر جایگزین: IIS

اگر IT فقط IIS می‌پذیرد:

1. نصب **Hosting Bundle .NET 10**.
2. `dotnet publish -c Release -o C:\inetpub\amadban`
3. سایت IIS با binding به پورت 8088 یا 80 روی IP داخلی.
4. Application Pool: **No Managed Code**.
5. همان متغیرهای محیط را در `web.config` یا Configuration Editor برای سایت تنظیم کنید.

## چک‌لیست قبل از تحویل به پرسنل

- [ ] از سه PC مختلف LAN باز شدن `/Login`
- [ ] یک کاربر انباردار، یک مأمور، یک کارتکس ساخته شد
- [ ] چاپ یک سند و یک حواله از مرورگر (پرینتر LAN)
- [ ] فایروال: پورت باز فقط برای subnet بیمارستان
- [ ] `keys` و `backup` در برنامه پشتیبان شبانه

## عیب‌یابی

| علامت | کار |
|--------|-----|
| سرویس فوری خاموش | `Get-EventLog -LogName Application -Newest 20` و Event Viewer |
| خطای اتصال DB | سرویس `postgresql-x64-17` فعال؛ رمز در `service.env` |
| از LAN نمی‌بینند | فایروال Windows + آنتی‌ویروس؛ ping به IP سرور |
| بعد از reboot ورود می‌پرد | پوشه `keys` پاک نشده باشد؛ سرویس Amadban Automatic |

---

مرجع Linux/Docker: [deployment-handoff.md](deployment-handoff.md) · اسکریپت‌ها: `scripts/windows/`