C# demo service TimerServiceAsync

by Markus Scholtes, 2020


Compile.bat compiles the service
Install.bat installs it to %ProgramFiles%\TimerServiceAsync
Uninstall.bat uninstalls and removes it from %ProgramFiles%\TimerServiceAsync


The service start a process entry every 30 seconds and logs to %WINDIR%\Logs\Service\TimerServiceAsyncYYYYMMDD.log if nothing else is specified in ServiceConfig.xml.
The config file is read on every time tick, so changes affect instantly.

The only difference to TimerService is that on service start and continuation the action (process start) is done asynchronous. 
There is a small chance that not all logging can be made because of parallelism conflicts.

The process started is specified in ServiceConfig.xml, many parameters to the processtart can be configured here.

The service can also be run interactively. Then it performs the action (here to log) until a key is pressed.
But in order to log (to event log or file) it needs administrative rights (with eventlog only the first time to create event source).

The parameter VERBOSE given interactively or to service manager (e.g. by calling "sc start TimerServiceAsync VERBOSE") forces verbose logging.
