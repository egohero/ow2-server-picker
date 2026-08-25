@echo off
setlocal EnableExtensions EnableDelayedExpansion
title Overwatch 2 Server Picker - install

rem Per-user install: copies the app to %LOCALAPPDATA%\Programs and adds a Start Menu
rem entry. No administrator rights needed for this - only running the app itself needs
rem elevation, because creating firewall rules does.

set "SRC=%~dp0"
set "DEST=%LOCALAPPDATA%\Programs\Ow2ServerPicker"
set "LNKDIR=%APPDATA%\Microsoft\Windows\Start Menu\Programs"
set "LNK=%LNKDIR%\Overwatch 2 Server Picker.lnk"

echo.
echo   Installing Overwatch 2 Server Picker
echo   from : %SRC%
echo   to   : %DEST%
echo.

if not exist "%SRC%Ow2ServerPicker.exe" (
    echo   ERROR: Ow2ServerPicker.exe is not next to this script.
    echo   Extract the whole release archive and run install.cmd from inside it.
    goto :fail
)
if not exist "%SRC%servers.json" (
    echo   ERROR: servers.json is not next to this script.
    echo   The app needs it for the datacenter list.
    goto :fail
)

rem Running the installer from the install folder would copy files onto themselves.
if /i "%SRC%"=="%DEST%\" (
    echo   ERROR: this looks like the installed copy already. Run install.cmd from the
    echo   extracted download instead.
    goto :fail
)

rem --- close any running instance -------------------------------------------------
rem Upgrading over a running copy fails, and the app normally runs elevated, so a plain
rem taskkill from this unelevated script gets "Access is denied".
taskkill /f /im Ow2ServerPicker.exe >nul 2>&1
set "RUNNING=0"
for /f %%c in ('powershell -NoProfile -Command ^
  "@(Get-Process Ow2ServerPicker -ErrorAction SilentlyContinue).Count" 2^>nul') do set "RUNNING=%%c"

if not "%RUNNING%"=="0" (
    echo   Overwatch 2 Server Picker is already running with administrator rights,
    echo   and has to close before it can be replaced.
    echo.
    choice /c YN /n /m "   Close it now? [Y/N] "
    if errorlevel 2 (
        echo.
        echo   Close the app yourself, then run install.cmd again.
        goto :fail
    )
    > "%TEMP%\ow2sp-close.ps1" echo Get-Process Ow2ServerPicker -ErrorAction SilentlyContinue ^| Stop-Process -Force
    powershell -NoProfile -Command ^
      "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','%TEMP%\ow2sp-close.ps1'" >nul 2>&1
    del "%TEMP%\ow2sp-close.ps1" >nul 2>&1

    for /f %%c in ('powershell -NoProfile -Command ^
      "@(Get-Process Ow2ServerPicker -ErrorAction SilentlyContinue).Count" 2^>nul') do set "RUNNING=%%c"
    if not "!RUNNING!"=="0" (
        echo.
        echo   Could not close it. Close the app yourself and run install.cmd again.
        goto :fail
    )
    echo   Closed.
)

if not exist "%DEST%" mkdir "%DEST%" || goto :fail

rem Never silently discard a servers.json the user has edited - keep a copy first.
if exist "%DEST%\servers.json" (
    fc /b "%DEST%\servers.json" "%SRC%servers.json" >nul 2>&1
    if errorlevel 1 (
        copy /y "%DEST%\servers.json" "%DEST%\servers.json.previous" >nul
        echo   Existing servers.json differed - saved as servers.json.previous
    )
)

copy /y "%SRC%Ow2ServerPicker.exe" "%DEST%\" >nul || goto :fail
copy /y "%SRC%servers.json"        "%DEST%\" >nul || goto :fail
if exist "%SRC%tools\capture-server.ps1" (
    if not exist "%DEST%\tools" mkdir "%DEST%\tools"
    copy /y "%SRC%tools\capture-server.ps1" "%DEST%\tools\" >nul
)
if exist "%SRC%uninstall.cmd" copy /y "%SRC%uninstall.cmd" "%DEST%\" >nul

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$w = New-Object -ComObject WScript.Shell;" ^
  "$s = $w.CreateShortcut('%LNK%');" ^
  "$s.TargetPath = '%DEST%\Ow2ServerPicker.exe';" ^
  "$s.WorkingDirectory = '%DEST%';" ^
  "$s.IconLocation = '%DEST%\Ow2ServerPicker.exe,0';" ^
  "$s.Description = 'Choose which Overwatch 2 datacenters the game may connect to';" ^
  "$s.Save()"
if errorlevel 1 goto :fail
if not exist "%LNK%" goto :fail

echo.
echo   Installed.
echo   Press Start and type "Overwatch" to launch it.
echo.
echo   The app asks for administrator rights on launch - that is required to create
echo   Windows Firewall rules, and is the only thing it uses them for.
echo.
echo   To remove it later, run uninstall.cmd from %DEST%
echo.
pause
exit /b 0

:fail
echo.
echo   INSTALL FAILED
echo.
pause
exit /b 1
