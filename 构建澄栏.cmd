@echo off
set "TEMP=E:\haoyue\cache\claribar\temp"
set "TMP=%TEMP%"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build.ps1" -Check
if errorlevel 1 pause
