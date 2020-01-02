# TimerServiceAsync
Service that starts a program defined in configuration xml timer based including runas and elevation capabilities.  

Same as TimerService, but first program start is asynchronous to avoid service start timeout. The asynchronous start with delegates is only used out of laziness.

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

## Security considerations
Since **TimerServiceAsync** can execute programs in the context and/or on the desktop of other users without authentication and even hidden **do not install or use this code sample in production environment**!

## Usage
All commands have to be executed in elevated context:

### Compilation:
```cmd
C:\Windows-Service\TimerServiceAsync> Compile
```

### Installation:
```cmd
C:\Windows-Service\TimerServiceAsync> Install
```

### Service configuration:
```cmd
C:\Windows-Service\TimerServiceAsync> notepad "C:\Program Files\TimerServiceAsync\ServiceConfig.xml"
```

### Manual service start (can be done in Windows Services console too), service starts automatic on system start per default:
```cmd
C:\Windows-Service\TimerServiceAsync> sc.exe start TimerServiceAsync
```

### One-time service start in verbose mode (can be done in Windows Services console too):
```cmd
C:\Windows-Service\TimerServiceAsync> sc.exe start TimerServiceAsync VERBOSE
```

### Manual service stop (can be done in Windows Services console too):
```cmd
C:\Windows-Service\TimerServiceAsync> sc.exe stop TimerServiceAsync
```

### Deinstallation:
```cmd
C:\Windows-Service\TimerServiceAsync> Uninstall
```

## Configuration
Edit XML configuration file **C:\Program Files\TimerServiceAsync\ServiceConfig.xml** to meet your needs.

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
		<timer>30000</timer>
		<!-- pulsetype: action to perform in every pulse
				0 - do nothing
				1 - start process every pulse
				2 - start process only if previous instance has stopped
				3 - start process only if previous instance is still running
				4 - stop process every pulse if still running and do nothing
				5 - stop process every pulse if still running and always start a new instance
				6 - stop process every pulse if still running and only start a new instance if it was not running
				7 - stop process every pulse if still running and only start a new instance if it was running
				8 - start process only one time now and on service start
				9 - start process only one time now, on service start and on service continuation
    -->
		<pulsetype>2</pulsetype>
	</service>
	<program>
		<!-- commandline: command line for program to start -->
		<commandline>cmd.exe</commandline>
		<!-- parameters: parameters for program if any -->
		<parameters>/k dir</parameters>
		<!-- workingdirectory: working directory for program if any -->
		<workingdirectory></workingdirectory>
		<!-- session: session where to start process
			service - service session (session 0)
			console - console session
			user:<NAMEPART> - search for session of first user whose name contains <NAMEPART> (ignore case)
			active - first found active user session
			disconnected - first found inactive user session
			<NUMBER> - session id <NUMBER>
		 -->
		<session>active</session>
		<!-- runas: true - run process as session user, false - run process as service account, probably LocalSystem -->
		<runas>false</runas>
		<!-- elevation: true - elevate process if possible, false - unelevate process if possible, none - do nothing
			UAC has to be enabled. Processes of a non administrative user and of LocalSystem cannot be elevated -->
		<elevation>none</elevation>
		<!-- hide: hide process window? -->
		<hide>false</hide>
	</program>
</serviceconfig>
```

## History

### 1.0.0 / 2020-01-02
Initial release
