@echo off
title Baidu AOI Extractor
cd /d "%~dp0"

cls
echo ======================================================
echo   Baidu AOI Extractor (Python Web)
echo ======================================================
echo.
echo Checking Python environment...

set "PY="
if exist "D:\python\python.exe" set "PY=D:\python\python.exe"
if not defined PY (
  where python >nul 2>nul && set "PY=python"
)

if not defined PY (
  echo [ERROR] Python not found. Please install Python 3.10+.
  pause
  exit /b 1
)

echo Using Python: %PY%
echo Starting Web Server and opening browser...
echo.

"%PY%" app.py

pause
