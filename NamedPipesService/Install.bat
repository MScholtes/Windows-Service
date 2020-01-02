@echo off
:: Markus Scholtes 2020
:: Install service to C:\Program Files\<SERVICENAME>

set SERVICENAME=NamedPipesService

echo Creating service "%SERVICENAME%" with binary "%ProgramFiles%\%SERVICENAME%\%SERVICENAME%.exe"

md "%ProgramFiles%\%SERVICENAME%"
copy "%~dp0%SERVICENAME%.exe" "%ProgramFiles%\%SERVICENAME%"
copy "%~dp0ServiceConfig.xml" "%ProgramFiles%\%SERVICENAME%"

sc.exe create %SERVICENAME% binpath= "%ProgramFiles%\%SERVICENAME%\%SERVICENAME%.exe" start= auto Displayname= "%SERVICENAME%"
sc.exe description %SERVICENAME% "C# Named Pipes demo service."

echo.
echo Service %SERVICENAME% installed
echo Start with e.g.  sc.exe start %SERVICENAME%
echo Uninstall with Uninstall.bat
echo.
