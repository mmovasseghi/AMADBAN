# فلش آفلاین — دانلود از GitHub، نصب یک‌کلیک (Windows Server 2019)

## خلاصه

| مرحله | کجا اینترنت لازم است؟ |
|--------|------------------------|
| دانلود ZIP از GitHub | فقط PC شما (یک بار) |
| کپی روی فلش | — |
| `نصب-آمادبان.bat` روی سرور | **خیر — هیچ اتصالی به اینترنت** |

## ۱) دانلود از GitHub

**روش الف — Artifact (خودکار با هر push روی `main`):**

1. https://github.com/mmovasseghi/AMADBAN/actions/workflows/build-windows-offline-release.yml
2. آخرین اجرای سبز → **Artifacts** → `Amadban-Offline-Windows.zip`

**روش ب — Run دستی:** همان صفحه → **Run workflow** → main → Run

**روش ج — Release:** اگر برچسب زده شد (مثلاً `offline-v1.0.0`) → تب **Releases** → همان ZIP

## ۲) فلش

1. ZIP را Extract کنید.
2. کل پوشه (مثلاً `Amadban-Offline-snapshot-...`) را روی فلش بگذارید.
3. (اختیاری روی PC) از داخل پوشه:

   ```powershell
   powershell -File install\Verify-FlashBundle.ps1
   ```

## ۳) سرور (یک کلیک)

1. پوشه فلش را کپی کنید.
2. **`INSTALL.bat`** (یا `نصب-آمادبان.bat`) → Run as Administrator — روی Server 2019 حتماً **INSTALL.bat**
3. `http://<IP>:8088/Login` — `admin` / رمز در `C:\Amadban\config\service.env`

## داخل ZIP چه هست؟

- `app/Amadban.exe` — برنامه + .NET (self-contained)
- `postgres/postgresql-17.4-1-windows-x64.exe` — نصب DB آفلاین
- `postgres/vcredist/VC_redist.x64.exe`
- `install/*.ps1`
- `flash-bundle.manifest.txt`

سورس Git حجم ZIP را نگه نمی‌دارد (~۳۵۰MB)؛ فایل کامل **Artifact/Release همین مخزن GitHub** است.