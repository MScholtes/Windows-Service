# Windows-Service
Sample implementations of a Windows Service in C#

**Markus Scholtes, 2020**

***

## Features
* Easy 'one click' compilaton batches (no Visual Studio required), installation and uninstallation batches.
* Simple service configuration with heavily commented XML config files.
* Only .Net 4.x required (and a supported version of Windows).
* Logging to file and/or event log.
* Interactive execution for testing purposes.

## List of services

* **DemoService** - Service template with a timer that only writes to a log.
* **TimerService** - Service that starts programs defined in configuration xml timer based including runas and elevation capabilities.
* **TimerServiceAsync** - Same as TimerService, but first program start is asynchronous to avoid service start timeout.
* **NamedPipesService** - Named Pipes demo service including client program. Just echoes messages sent by client.
* **NPCommandService** - Named Pipes service including client program that executes command lines sent by client in service context.
* **NPPowershellService** - Named Pipes service including client program that executes powershell commands sent by client in service context.
* **CollectEventsService** - Service that collects ALL events of ALL event logs (there are over 1000 event logs in Windows 10) and summarizes them in one text or csv file. Can access remote computers if the service account has the adequate access rights.
* **DemoSessionService** - Demo service that writes a log entry for every session event like logon / logoff / connect / disconnect / lock / unlock to a file or event log.

to be continued...

## Drawbacks

* Scripts supply only services that run in context of local system (can be changed manually after service installation).
* Logging to file (logtarget = 1) is not thread safe (might be important for short timer intervals).
* Access errors on service configuration xml file stop the service (this can be changed if ExitCode is not set in ReadBaseConfiguration() and ReadConfiguration()).

to be restricted...

## Usage
For instance explained with compiling, installing and removing the *DemoService* (all commands have to be executed in elevated context), the other services work accordingly:

### Compilation:
```cmd
C:\Windows-Service\DemoService> Compile
```

### Installation:
```cmd
C:\Windows-Service\DemoService> Install
```

### Service configuration:
```cmd
C:\Windows-Service\DemoService> notepad "C:\Program Files\DemoService\ServiceConfig.xml"
```

### Manual service start (can be done in Windows Services console too), services start automatic on system start per default:
```cmd
C:\Windows-Service\DemoService> sc.exe start DemoService
```

### One-time service start in verbose mode (can be done in Windows Services console too):
```cmd
C:\Windows-Service\DemoService> sc.exe start DemoService VERBOSE
```

### Manual service stop (can be done in Windows Services console too):
```cmd
C:\Windows-Service\DemoService> sc.exe stop DemoService
```

### Deinstallation:
```cmd
C:\Windows-Service\DemoService> Uninstall
```

## Configuration
Edit XML configuration file **C:\Program Files\\<SERVICENAME\>\ServiceConfig.xml** to meet your needs.

Most of the configuration items are read on next service action / timer tick, some items like *autolog*, *canpauseandcontinue* or *canstop* at next service start.

### Minimal configuration file (here for *DemoService*, other services have more configuration items):

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
		<timer>5000</timer>
		<!-- pulsetype: action to perform in every pulse
				0 - do nothing
				1 - start action every pulse
				8 - start action only one time now and on service start
				9 - start action only one time now, on service start and on service continuation
    -->
		<pulsetype>1</pulsetype>
	</service>
</serviceconfig>
```

## History

### 1.0.2 / 2020-01-22
Fix for CollectEventsService (error setting time interval to 0 for second computer)

### 1.0.1 / 2020-01-06
DemoSessionService added

### 1.0.0 / 2020-01-02
Initial release
