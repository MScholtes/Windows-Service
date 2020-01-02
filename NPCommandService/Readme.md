# NPCommandService
Named Pipes service including client program. Executes the command send by the client per Named Pipe.

Remote call of service is of course possible if firewall allows this and the calling user is member of the Users group of the server.

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
Since all authenticated users can send messages to **NPCommandService** **do not install or use this code sample in production environment**!

Change the following code so that only trustworthy users can access the service:
```C#
	pipeSecurity.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
```

## Usage
All commands have to be executed in elevated context:

### Compilation:
```cmd
C:\Windows-Service\NPCommandService> Compile
```

### Installation:
```cmd
C:\Windows-Service\NPCommandService> Install
```

### Service configuration:
```cmd
C:\Windows-Service\NPCommandService> notepad "C:\Program Files\NPCommandService\ServiceConfig.xml"
```

### Manual service start (can be done in Windows Services console too), service starts automatic on system start per default:
```cmd
C:\Windows-Service\NPCommandService> sc.exe start NPCommandService
```

### One-time service start in verbose mode (can be done in Windows Services console too):
```cmd
C:\Windows-Service\NPCommandService> sc.exe start NPCommandService VERBOSE
```

### Call of Named Pipes service to execute **whoami.exe** with client program using default parameters from local machine (remote call is of course also possible):
```cmd
C:\Windows-Service\NPCommandService> NPCommandClient "whoami" "NamedPipesService" .
```

### Manual service stop (can be done in Windows Services console too):
```cmd
C:\Windows-Service\NPCommandService> sc.exe stop NPCommandService
```

### Deinstallation:
```cmd
C:\Windows-Service\NPCommandService> Uninstall
```

## Configuration
Edit XML configuration file **C:\Program Files\NPCommandService\ServiceConfig.xml** to meet your needs.

All of the configuration items are read only on service start.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<serviceconfig>
	<service>
		<!-- automatic logging of start, pause, continue and stop in application eventlog? -->
		<autolog>false</autolog>
		<!-- are pause and continue of service implemented? -->
		<canpauseandcontinue>false</canpauseandcontinue>
		<!-- stop of service enabled in service manager -->
		<canstop>true</canstop>
		<!-- loglevel: 0 - none, 1 - normal, 2 - verbose -->
		<loglevel>1</loglevel>
		<!-- logtarget: 0 - none. Or sum of: 1 - file, 2 - application log, 4 - console (only for interactive mode) -->
		<logtarget>1</logtarget>
		<!-- logpath: path to directory for logfiles (logtarget = 1), empty: %WINDIR%\Logs\Service -->
		<logpath></logpath>
		<!-- namedpipe: name of Named Pipe, empty: NamedPipesService -->
		<namedpipe>NamedPipesService</namedpipe>
		<!-- instancecount: count of server threads (default: 10) -->
		<instancecount>10</instancecount>
	</service>
</serviceconfig>
```

## History

### 1.0.0 / 2020-01-02
Initial release
