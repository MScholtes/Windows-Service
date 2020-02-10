C# service ElevatedStartupService

by Markus Scholtes, 2020


Compile.bat compiles the service
Install.bat installs it to %ProgramFiles%\ElevatedStartupService
Uninstall.bat uninstalls and removes it from %ProgramFiles%\ElevatedStartupService


The service starts every program or link elevated in folder C:\Users\<USERNAME>\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\StartupElevated when a user logs on and folder exists.
The service log is written to %WINDIR%\Logs\Service\ElevatedStartupServiceYYYYMMDD.log if nothing else is specified in ServiceConfig.xml.

The service can also be run interactively, but it does not perform the action since the session change information hook does not exist. It runs until a key is pressed.
But in order to log (to event log or file) it needs administrative rights (with eventlog only the first time to create event source).

The parameter VERBOSE given interactively or to service manager (e.g. by calling "sc start ElevatedStartupService VERBOSE") forces verbose logging.
