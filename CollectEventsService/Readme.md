# CollectEventsService
C# event collecting service.

The service reads events of all (!) event logs and writes them to *%WINDIR%\Logs\CollectEventsYYYYMMDD.txt* every 5 minutes if nothing else is specified in **ServiceConfig.xml**.  

Aggregating the event logs of one or more remote computers is possible (can be configured in *computers* entry in configuration xml), but the service account has to have the appropriate access rights.

**Markus Scholtes, 2020**

***

## Features
* Easy 'one click' compilaton batch (no Visual Studio required), installation and uninstallation batch.
* Simple service configuration with heavily commented XML config file.
* Only .Net 4.x required (and a supported version of Windows).
* Logging to file and/or event log.
* Interactive execution for testing purposes.

## Drawbacks

* Scripts supply only services that run in context of local system (can be changed manually after service installation).
* Logging to file (logtarget = 1) is not thread safe (might be important for short timer intervals).
* Access errors on service configuration xml file stop the service (this can be changed if ExitCode is not set in ReadBaseConfiguration() and ReadConfiguration()).

## Usage
All commands have to be executed in elevated context:

### Compilation:
```cmd
C:\Windows-Service\CollectEventsService> Compile
```

### Installation:
```cmd
C:\Windows-Service\CollectEventsService> Install
```

### Service configuration:
```cmd
C:\Windows-Service\CollectEventsService> notepad "C:\Program Files\CollectEventsService\ServiceConfig.xml"
```

### Manual service start (can be done in Windows Services console too), service starts automatic on system start per default:
```cmd
C:\Windows-Service\CollectEventsService> sc.exe start CollectEventsService
```

### One-time service start in verbose mode (can be done in Windows Services console too):
```cmd
C:\Windows-Service\CollectEventsService> sc.exe start CollectEventsService VERBOSE
```

### Manual service stop (can be done in Windows Services console too):
```cmd
C:\Windows-Service\CollectEventsService> sc.exe stop CollectEventsService
```

### Deinstallation:
```cmd
C:\Windows-Service\CollectEventsService> Uninstall
```

## Configuration
Edit XML configuration file **C:\Program Files\CollectEventsService\ServiceConfig.xml** to meet your needs.

Most of the configuration items are read on next service action / timer tick, some items like *autolog*, *canpauseandcontinue* or *canstop* at next service start.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<serviceconfig>
	<service>
		<!-- automatic logging of start, pause, continue and stop in application eventlog? -->
		<autolog>false</autolog>
		<!-- are pause and continue of service implemented? -->
		<canpauseandcontinue>true</canpauseandcontinue>
		<!-- stop of service enabled in service manager -->
		<canstop>true</canstop>
		<!-- loglevel: 0 - none, 1 - normal, 2 - verbose -->
		<loglevel>1</loglevel>
		<!-- logtarget: 0 - none. Or sum of: 1 - file, 2 - application log, 4 - console (only for interactive mode) -->
		<logtarget>1</logtarget>
		<!-- logpath: path to directory for logfiles (logtarget = 1), empty: %WINDIR%\Logs\Service -->
		<logpath></logpath>
		<!-- timer: pulse in milliseconds -->
		<timer>300000</timer>
	</service>
	<event>
		<!-- filename: path and name to target file, empty: %WINDIR%\Logs\CollectedEvents.txt or %WINDIR%\Logs\CollectedEvents.csv (depending on format) -->
		<filename></filename>
		<!-- computers: which computers to query, empty for localhost or comma separated list of computer names. Specify localhost for local machine 
		     service account must have access right to event log on remote machine -->
		<computers></computers>
		<!-- eventlogs: which event logs to query, empty for all event logs or comma separated list of log names -->
		<eventlogs></eventlogs>
		<!-- eventlevel: all (default) - 0, up to critical - 1, up to error - 2, up to warning- 3, up to informational - 4, up to verbose - 5 -->
		<eventlevel>0</eventlevel>
		<!-- format: format of file, txt (default) or csv -->
		<format>txt</format>
		<!-- filerotation: when to create a new file: none - never (default), hourly - every hour, daily - every day, monthly - every month, (size in integer) - when (size in integer) in KB is reached -->
		<filerotation>daily</filerotation>
		<!-- filecount: maximal number of log files to keep or 0 for don't check (default) -->
		<filecount>31</filecount>
	</event>
</serviceconfig>
```

## History

### 1.0.0 / 2020-01-02
Initial release
