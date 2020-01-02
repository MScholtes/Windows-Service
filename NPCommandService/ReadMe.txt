C# Named Pipes service that executes the received message

by Markus Scholtes, 2020


Compile.bat compiles the service and the client
Install.bat installs it to %ProgramFiles%\NPCommandService
Uninstall.bat uninstalls and removes it from %ProgramFiles%\NPCommandService

Named Pipes Server that listens for a client connection and execute messages receivedvia Named Pipe as command.
Use client NPCommandClient.exe to send messages.

The service can also be run interactively. Then it performs the action until a key is pressed. But in order to work it needs administrative rights.

The parameter VERBOSE given interactively or to service manager (e.g. by calling "sc start NPCommandService VERBOSE") forces verbose logging.
