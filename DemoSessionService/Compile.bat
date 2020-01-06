@echo off
:: Markus Scholtes, 2020
:: Compile DemoSessionService in .Net 4.x environment
setlocal

set COMPILER=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if NOT EXIST "%COMPILER%" echo C# compiler not found&goto :READY

"%COMPILER%" /target:exe "%~dp0DemoSessionService.cs" /win32icon:"%~dp0MScholtes.ico"

:READY
:: was batch started in Windows Explorer? Yes, then pause
echo "%CMDCMDLINE%" | find /i "/c" > nul
if %ERRORLEVEL%==0 pause
