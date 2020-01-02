C# demo service DemoService

by Markus Scholtes, 2020


Compile.bat compiles the service
Install.bat installs it to %ProgramFiles%\DemoService
Uninstall.bat uninstalls and removes it from %ProgramFiles%\DemoService


The service writes a log entry every 5 seconds to %WINDIR%\Logs\Service\DemoServiceYYYYMMDD.log if nothing else is specified in ServiceConfig.xml.
The config file is read on every time tick, so changes affect instantly.

The service can also be run interactively. Then it performs the action (here to log) until a key is pressed.
But in order to log (to event log or file) it needs administrative rights (with eventlog only the first time to create event source).

The parameter VERBOSE given interactively or to service manager (e.g. by calling "sc start DemoService VERBOSE") forces verbose logging.

For your own service you may replace the line
				WriteToLog("Called service action.");
with your personal service action code.
