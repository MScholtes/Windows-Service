@echo off
:: Markus Scholtes 2020
:: Uninstall service SERVICENAME from C:\Program Files\<SERVICENAME>

set SERVICENAME=TimerServiceAsync

echo Removing service "%SERVICENAME%" from "%ProgramFiles%\%SERVICENAME%"
sc.exe stop %SERVICENAME% >nul
sc.exe delete %SERVICENAME%
rd /s /q "%ProgramFiles%\%SERVICENAME%"

echo.
echo Service %SERVICENAME% removed and uninstalled
echo.
