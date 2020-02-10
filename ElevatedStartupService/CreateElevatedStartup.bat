@echo off
:: Markus Scholtes, 2020
:: Create elevated startup folder

echo Creating StartupElevated folder %APPDATA%\Microsoft\Windows\Start Menu\Programs\StartupElevated
echo Place links to your elevated startup programs here

md "%APPDATA%\Microsoft\Windows\Start Menu\Programs\StartupElevated"
explorer "%APPDATA%\Microsoft\Windows\Start Menu\Programs\StartupElevated"
pause