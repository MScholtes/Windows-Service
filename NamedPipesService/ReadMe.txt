C# demo service NamedPipesService

by Markus Scholtes, 2020


Compile.bat compiles the service
Install.bat installs it to %ProgramFiles%\NamedPipesService
Uninstall.bat uninstalls and removes it from %ProgramFiles%\NamedPipesService

Example Named Pipes Server that listens for a client connection and returns messages via Named Pipe. Use example client.

The service can also be run interactively. Then it performs the action until a key is pressed. But in order to work it needs administrative rights.

The parameter VERBOSE given interactively or to service manager (e.g. by calling "sc start NamedPipesService VERBOSE") forces verbose logging.

For your own service you may replace the code between the lines
				// *** INSERT YOUR CODE
with your personal service action code.
