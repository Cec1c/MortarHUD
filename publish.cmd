@echo off
rem MortarHUD publishing uses the explicit .NET 10 SDK.
rem Modes: portable (default), folder, runtime.
rem An existing non-empty output directory is never overwritten.
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\publish.ps1" %*
exit /b %errorlevel%
