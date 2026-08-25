@echo off
setlocal
rem Finds a live ICMP responder inside each datacenter and refreshes servers.json.
rem Dry run by default; pass --write to actually update the catalog.
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

cd /d "%~dp0.."
if not exist build\tools mkdir build\tools

"%CSC%" /nologo /target:exe /out:build\tools\FindProbes.exe ^
    /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll ^
    src\IpMath.cs src\ServerCatalog.cs tools\FindProbes.cs || exit /b 1

build\tools\FindProbes.exe "%~dp0..\data\servers.json" %*
