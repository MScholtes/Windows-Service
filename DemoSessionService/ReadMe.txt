C# service DemoSessionService

by Markus Scholtes, 2020


Compile.bat compiles the service
Install.bat installs it to %ProgramFiles%\DemoSessionService
Uninstall.bat uninstalls and removes it from %ProgramFiles%\DemoSessionService


The service writes a log entry for every session event like logon/logoff/connect/disconnect/lock/unlock to %WINDIR%\Logs\Service\DemoSessionServiceYYYYMMDD.log if nothing else is specified in ServiceConfig.xml.

The service can also be run interactively, but it does not perform the action since the session change information hook does not exist. It runs until a key is pressed.
But in order to log (to event log or file) it needs administrative rights (with eventlog only the first time to create event source).

The parameter VERBOSE given interactively or to service manager (e.g. by calling "sc start DemoSessionService VERBOSE") forces verbose logging.
