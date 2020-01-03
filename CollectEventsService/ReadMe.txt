C# event collecting service

by Markus Scholtes, 2020


Compile.bat compiles the service
Install.bat installs it to %ProgramFiles%\CollectEventsService
Uninstall.bat uninstalls and removes it from %ProgramFiles%\CollectEventsService


The service reads events of all (!) event logs and writes them to %WINDIR%\Logs\CollectEventsYYYYMMDD.txt every 5 minutes if nothing else is specified in ServiceConfig.xml.
The config file is read on every time tick, so changes affect instantly.

The service can also be run interactively. Then it performs the action until a key is pressed.
But in order to read and write it needs administrative rights.

The parameter VERBOSE given interactively or to service manager (e.g. by calling "sc start CollectEventsService VERBOSE") forces verbose logging.
