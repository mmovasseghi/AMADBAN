@echo off
cd /d "%~dp0"
echo Amadban offline install...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install\Run-Install.ps1" -BundleRoot "%~dp0"
if errorlevel 1 (
  echo.
  echo Install FAILED. See messages above.
  pause
  exit /b 1
)
echo.
echo OK. Open http://127.0.0.1:8088/Login  user: admin  password: C:\Amadban\config\service.env
pause
exit /b 0