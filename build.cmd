@echo off
setlocal
rem Builds Ow2ServerPicker.exe with the C# compiler that ships inside Windows.
rem No Visual Studio, no .NET SDK, no NuGet - nothing to install.

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo ERROR: Could not find the in-box C# compiler ^(.NET Framework 4.x^).
    exit /b 1
)

cd /d "%~dp0"
if not exist build mkdir build

rem Version is derived from git so it cannot go stale; see tools\gen-version.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File tools\gen-version.ps1 || exit /b 1

"%CSC%" ^
    /nologo ^
    /target:winexe ^
    /platform:anycpu ^
    /optimize+ ^
    /warnaserror- ^
    /out:build\Ow2ServerPicker.exe ^
    /win32manifest:src\app.manifest ^
    /win32icon:assets\app.ico ^
    /resource:data\servers.json,servers.json ^
    /resource:assets\app.ico,app.ico ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll ^
    /reference:System.Web.Extensions.dll ^
    /reference:Microsoft.CSharp.dll ^
    src\*.cs

if errorlevel 1 (
    echo.
    echo BUILD FAILED
    exit /b 1
)

copy /y data\servers.json build\servers.json >nul

echo.
echo Built build\Ow2ServerPicker.exe
echo A copy of servers.json sits beside it and overrides the embedded catalog.
endlocal
