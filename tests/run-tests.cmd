@echo off
setlocal enabledelayedexpansion
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" echo ERROR: in-box C# compiler not found & exit /b 1

cd /d "%~dp0.."
if not exist build\tests mkdir build\tests

set "REFS=/reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:Microsoft.CSharp.dll"
set "GUIREFS=%REFS% /reference:System.Drawing.dll /reference:System.Windows.Forms.dll"
set "FAILED=0"

echo === interval arithmetic and catalog ===
"%CSC%" /nologo /target:exe /out:build\tests\SelfTest.exe %REFS% ^
    src\IpMath.cs src\ServerCatalog.cs src\Sorting.cs tests\SelfTest.cs || exit /b 1
copy /y data\servers.json build\tests\servers.json >nul
build\tests\SelfTest.exe || set "FAILED=1"

echo === firewall COM contract (writes nothing) ===
"%CSC%" /nologo /target:exe /out:build\tests\FirewallProbe.exe %REFS% ^
    src\IpMath.cs src\ServerCatalog.cs tests\FirewallProbe.cs || exit /b 1
build\tests\FirewallProbe.exe || set "FAILED=1"

echo === form construction ===
rem Globs the whole of src so adding a UI file cannot silently drop it from this test.
rem /main: picks the harness entry point over Program.Main, which is otherwise ambiguous.
"%CSC%" /nologo /target:exe /out:build\tests\FormSmoke.exe %GUIREFS% ^
    /main:Ow2ServerPicker.FormSmoke ^
    src\*.cs tests\FormSmoke.cs || exit /b 1
build\tests\FormSmoke.exe || set "FAILED=1"

echo.
if "%FAILED%"=="1" (echo TESTS FAILED & exit /b 1)
echo ALL TESTS PASSED
exit /b 0
