# Windows Server scripts

| مسیر | کاربرد |
|------|--------|
| **`offline/Build-OfflineBundle.ps1`** | ساخت بسته USB (قبل از بردن به سرور بدون نت) |
| **`offline/Install-OneClick.ps1`** | نصب از بسته (داخل پوشه `install/`) |
| **`offline/` + `bundle/README.md`** | راهنمای آفلاین کامل |
| `service.env.example` | فقط نصب دستی |
| `Install-Amadban.ps1` | نصب دستی با `dotnet publish` روی سرور |

**یک کلیک:** `dist/Amadban-Offline-*/نصب-آمادبان.bat` (بعد از Build-OfflineBundle).

Full steps: [docs/deployment-windows-server-2019-lan.md](../../docs/deployment-windows-server-2019-lan.md)