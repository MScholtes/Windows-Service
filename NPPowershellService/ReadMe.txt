C# Named Pipes service that executes the received message as powershell commands

by Markus Scholtes, 2020


Compile.bat compiles the service and the client
Install.bat installs it to %ProgramFiles%\NPPowershellService
Uninstall.bat uninstalls and removes it from %ProgramFiles%\NPPowershellService

Named Pipes Server that listens for a client connection and execute messages received via Named Pipe as command.
Use client NPPowershellClient.exe to send messages.

The service can also be run interactively. Then it performs the action until a key is pressed. But in order to work it needs administrative rights.

The parameter VERBOSE given interactively or to service manager (e.g. by calling "sc start NPPowershellService VERBOSE") forces verbose logging.
