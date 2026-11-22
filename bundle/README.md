# بسته آفلاین نصب آمادبان (LAN / بدون اینترنت)

روی **سرور بدون اینترنت** چیزی از شبکه دانلود نمی‌شود. همهٔ فایل‌های لازم باید روی USB یا پوشهٔ کپی‌شده باشد.

## ساخت بسته (یک بار، روی PC دارای اینترنت + SDK)

```powershell
cd <مسیر مخزن AMADBAN>
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\windows\offline\Build-OfflineBundle.ps1
```

یا از Linux/macOS (خروجی publish ویندوز):

```bash
./scripts/build-offline-bundle.sh
```

خروجی: `dist/Amadban-Offline-*/` — کل این پوشه را به USB ببرید.

## قبل از کپی به USB (الزامی برای نصب خودکار DB)

فایل نصب PostgreSQL را **دستی** در بسته بگذارید (یک بار از PC متصل به نت):

1. از [postgresql.org/download/windows](https://www.postgresql.org/download/windows/) نسخه **Windows x86-64** (EDB installer، مثلاً ۱۷.x) را بگیرید.
2. نام تقریبی: `postgresql-17.x-windows-x64.exe`
3. کپی کنید به:

   `dist/Amadban-Offline-.../postgres/postgresql-17-windows-x64.exe`

   (یا هر فایلی با الگوی `postgresql-*-windows-x64.exe` داخل پوشه `postgres/`)

اختیاری: اگر نصب PostgreSQL خطای Visual C++ داد، `VC_redist.x64.exe` مایکروسافت را در `postgres/vcredist/` بگذارید.

## نصب روی Windows Server 2019 (یک کلیک)

1. پوشه `Amadban-Offline-*` را روی سرور کپی کنید (مثلاً `D:\AmadbanSetup`).
2. **`نصب-آمادبان.bat`** را راست‌کلیک → **Run as administrator**.
3. پایان کار: مرورگر LAN → `http://<IP-سرور>:8088/Login`  
   کاربر اولیه: `admin` — رمز در `C:\Amadban\config\service.env` (خط `AMADBAN_ADMIN_PASSWORD`).

## محتویات بسته

| پوشه | توضیح |
|------|--------|
| `app/` | برنامه self-contained (.NET داخل بسته؛ روی سرور dotnet لازم نیست) |
| `install/` | اسکریپت‌های PowerShell |
| `postgres/` | نصب‌گر PostgreSQL (شما اضافه می‌کنید) |
| `نصب-آمادبان.bat` | همان یک کلیک |

fonts و آیکون‌ها داخل `app/wwwroot` هستند؛ در زمان اجرا اینترنت لازم نیست.