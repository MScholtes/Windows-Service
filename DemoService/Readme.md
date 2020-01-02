# DemoService
Demonstration of a Windows Service written in C#, can be used as template for own timer based services.

The services writes an entry to the chosen log target (file and/or event log) every time pulse.

For your own service you may replace the line
```C#
  WriteToLog("Called service action.");
```
with your personal service action code.

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

### Manual service start (can be done in Windows Services console too), service starts automatic on system start per default:
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
Edit XML configuration file **C:\Program Files\DemoService\ServiceConfig.xml** to meet your needs.

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

### 1.0.0 / 2020-01-02
Initial release
