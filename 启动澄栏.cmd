@echo off
set "TEMP=E:\haoyue\cache\claribar\temp"
set "TMP=%TEMP%"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run.ps1"
if errorlevel 1 pause
