# DemoSessionService
Demonstration of a Windows Service reacting to session change written in C#, can be used as template for session based services.

The service writes a log entry for every session event like logon/logoff/connect/disconnect/lock/unlock to a file or event log.


**Markus Scholtes, 2020**

***

## Features
* Easy 'one click' compilaton batch (no Visual Studio required), installation and uninstallation batch.
* Simple service configuration with heavily commented XML config file.
* Only .Net 4.x required (and a supported version of Windows).
* Logging to file and/or event log.
* Interactive execution for testing purposes possible but does not notice session changes.

## Drawbacks

* Scripts supply only services that run in context of local system (can be changed manually after service installation).
* Logging to file (logtarget = 1) is not thread safe (might be important for short timer intervals).
* Access errors on service configuration xml file stop the service (this can be changed if ExitCode is not set in ReadBaseConfiguration() and ReadConfiguration()).

## Usage
All commands have to be executed in elevated context:

### Compilation:
```cmd
C:\Windows-Service\DemoSessionService> Compile
```

### Installation:
```cmd
C:\Windows-Service\DemoSessionService> Install
```

### Service configuration:
```cmd
C:\Windows-Service\DemoSessionService> notepad "C:\Program Files\DemoSessionService\ServiceConfig.xml"
```

### Manual service start (can be done in Windows Services console too), service starts automatic on system start per default:
```cmd
C:\Windows-Service\DemoSessionService> sc.exe start DemoSessionService
```

### One-time service start in verbose mode (can be done in Windows Services console too):
```cmd
C:\Windows-Service\DemoSessionService> sc.exe start DemoSessionService VERBOSE
```

### Manual service stop (can be done in Windows Services console too):
```cmd
C:\Windows-Service\DemoSessionService> sc.exe stop DemoSessionService
```

### Deinstallation:
```cmd
C:\Windows-Service\DemoSessionService> Uninstall
```

## Configuration
Edit XML configuration file **C:\Program Files\DemoSessionService\ServiceConfig.xml** to meet your needs.

The configuration items are read on next service start.

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
	</service>
</serviceconfig>
```

## History

### 1.0.0 / 2020-01-06
Initial release
