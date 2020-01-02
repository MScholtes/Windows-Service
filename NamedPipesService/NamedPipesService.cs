// C# demo of a Named Pipes service
// use the example client to send and receive messages
// Named Pipes code derived from https://github.com/webcoyote/CSNamedPipes
// service can be configured via ServiceConfig.xml in the directory of the binary
// Markus Scholtes, 2020/01/02

// search for text "*** INSERT YOUR CODE" to find the place where you can implement your own code

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Xml;

// set executable properties
using System.Reflection;
[assembly:AssemblyTitle("NamedPipesService")]
[assembly:AssemblyDescription("C# service that is controlled by Named Pipes")]
[assembly:AssemblyConfiguration("")]
[assembly:AssemblyCompany("MS")]
[assembly:AssemblyProduct("NamedPipesService")]
[assembly:AssemblyCopyright("© Markus Scholtes 2020")]
[assembly:AssemblyTrademark("")]
[assembly:AssemblyCulture("")]
[assembly:AssemblyVersion("1.0.0.0")]
[assembly:AssemblyFileVersion("1.0.0.0")]

namespace NaSpNamedPipesService
{
	public partial class NamedPipesService : ServiceBase
	{
		static int Main(string[] arguments)
		{ // entry point
			// declaration as int, transfer of parameters and return of a value are ignored when called via the service manager
			NamedPipesService service = new NamedPipesService();
			int rc = 0;

			if (Environment.UserInteractive)
			{ // interactive start (check is not 100% accurate)
				// call service start manually
				service.OnStart(arguments);

				Console.WriteLine("Press a key to exit.");
				do { // loop until input is entered or error in service
					System.Threading.Thread.Sleep(100); // too lazy to implement OnPause() and OnContinue() here
				} while (!Console.KeyAvailable && (service.ExitCode == 0));

				if (service.ExitCode == 0)
				{	// key was pressed, stop service manually
					Console.ReadKey(true);
					service.OnStop();
				}
				else
				{ // error in service, service is already stopped
					Console.WriteLine("Error 0x{0:X} in service " + service.ServiceName + ".", service.ExitCode);
					rc = service.ExitCode;
				}
			}
			else
				// start as service
				ServiceBase.Run(service);

			// return code is ignored by service manager, only useful for interactive call
			return rc;
		}

		int loglevel = 0; // initialization without logging
		bool forceverbose = false; // loglevel 2 forced by command line parameter
		int logtarget = 1; // default: log to file
		string logpath; // standard path for file
		string pipeName = "NamedPipesService"; // name of Named Pipes Server
		int pipeCount = 10; // instance count of Named Pipes Server

		#region service structure

		public NamedPipesService()
		{ // constructor, set base configuration that can only be set in constructor
			AutoLog = false; // automatic logging of start, pause, continue and stop in application log?
				// name of message source for eventlog
			ServiceName = Path.GetFileNameWithoutExtension(System.AppDomain.CurrentDomain.FriendlyName);
			CanPauseAndContinue = false; // pause and continue are implemented?
			CanStop = true; // prevent stopping in service manager?

			// read initial configuration from xml file
			ReadBaseConfiguration();
		}

		protected override void OnStart(string[] args)
		{ // called from service manager on service start

			// the service manager waits about 30 seconds for a service to start before failing
			// you can request additional time from service manager to wait in milliseconds:
			// RequestAdditionalTime(2000);

			// read configuration from xml file
			WriteToLog("Starting service " + ServiceName + "...");

			// check command line parameter of service, e.g. "sc start NamedPipesService VERBOSE"
			if ((args.Length > 0) && (args[0].ToUpper() == "VERBOSE"))
			{ // verbose mode per command line parameter
				forceverbose = true;
				WriteToLog("Verbose mode forced per command line parameter.");
			}

			// start pipeCount instances of Named Pipes Server pipeName
			StartServer();

			// on error while starting the service set ExitCode here to cancel service start and generate
			// interactive error message and error message in system log (example: 2 returns "File Not Found"):
			// if (ErrorHasHappened) ExitCode = 2;
			if (ExitCode != 0)
			{	// fail service start
				WriteToLog("Error at service start of service " + ServiceName + ".");
				Stop();
			}
			else
				WriteToLog("Service " + ServiceName + " started.");
		}

		protected override void OnStop()
		{ // called from service manager on service stop
			// ExitCode must be set before OnStop() is called!

			// stop all instances of Named Pipes Server
			StopServer();

			// request additional time from service manager to wait in milliseconds
			// RequestAdditionalTime(2000);
			WriteToLog("Service " + ServiceName + " stopped.");
		}
		#endregion

		#region service helper functions
		private void ReadBaseConfiguration()
		{ // read base configuration out of xml file: service and logging settings

			XmlDocument xmlDocument = new XmlDocument();
			try {
				// load xml file ServiceConfig.xml in directory of binary
				xmlDocument.Load(AppDomain.CurrentDomain.BaseDirectory + "\\ServiceConfig.xml");

				// read base configuration without logging (since the default may be a bad configuration)
				// logpath: path to directory for logfiles (logtarget = 1), empty: %WINDIR%\Logs\Service
				logpath = ReadXmlText(xmlDocument, "/serviceconfig/service/logpath", "").TrimEnd('\\');

				// where to log: 0 - none. Or sum of: 1 - file, 2 - application log, 4 - console (only for interactive mode)
				string tempStr = ReadXmlText(xmlDocument, "/serviceconfig/service/logtarget", "1");
				int tempInt;
				if (Int32.TryParse(tempStr, out tempInt))
					if ((tempInt >= 0) && (tempInt <= 7))
						logtarget = tempInt;

				// logging starts here if configuration enables it
				// loglevel: 0 - none, 1 - normal, 2 - verbose
				switch(ReadXmlText(xmlDocument, "/serviceconfig/service/loglevel", "1"))
				{
					case "0": loglevel = 0;
										break;
					case "1": loglevel = 1;
										break;
					case "2": loglevel = 2;
										WriteToLog("Detailed logging mode activated.");
										break;
					default:  loglevel = 1;
										WriteToLog("Invalid value for logging mode.");
										break;
				}

				// set base service configuration (can only be set in constructor)
				if (ReadXmlText(xmlDocument, "/serviceconfig/service/autolog", "false").ToLower() == "true") AutoLog = true;
				if (ReadXmlText(xmlDocument, "/serviceconfig/service/canpauseandcontinue", "false").ToLower() == "true") CanPauseAndContinue = true;
				if (ReadXmlText(xmlDocument, "/serviceconfig/service/canstop", "true").ToLower() == "false") CanStop = false;

				// namedpipe: name of Named Pipe, empty: NamedPipesService
				pipeName = ReadXmlText(xmlDocument, "/serviceconfig/service/namedpipe", "NamedPipesService");

				// instancecount: count of server threads (default: 10)
				tempStr = ReadXmlText(xmlDocument, "/serviceconfig/service/instancecount", "10");
				if (Int32.TryParse(tempStr, out tempInt))
				{
					if (tempInt > 0)
						pipeCount = tempInt;
					else
						WriteToLog("Invalid value for instance count.");
				}
				else
					WriteToLog("Error when setting the instance count.");
			}
			catch (Exception ex)
			{
				if (loglevel == 0) loglevel = 1;
				WriteToLog("Cannot read configuration file.");
				WriteToLog("Error: " + ex.Message);
				ExitCode = ex.HResult;
				if (ExitCode == 0) ExitCode = 1;
			}
		}

		private string ReadXmlText(XmlDocument xmlDocument, string xpath, string defaultvalue)
		{ // helper function to read a XML value with XPath
			if (xmlDocument.SelectSingleNode(xpath) == null) return defaultvalue;
			if (xmlDocument.SelectSingleNode(xpath).InnerText == "") return defaultvalue;
			WriteVerboseToLog("Configure setting " + xpath + " to " + xmlDocument.SelectSingleNode(xpath).InnerText);
			return xmlDocument.SelectSingleNode(xpath).InnerText;
		}

		private void WriteToLog(string message)
		{ // helper function to log according to logging configuration
			if ((loglevel > 0) || forceverbose)
			{ // only if logging is enabled or forced per parameter

				if ((logtarget & 4) > 0)
				{ // write message to console (only for interactive mode)
					Console.WriteLine(DateTime.Now + ": " + message);
				}
				if ((logtarget & 2) > 0)
				{ // write message to application log
					try {
						EventLog.WriteEntry(message);
					}
					catch
					{
						// ignore failure, just don't log
					}
				}
				if ((logtarget & 1) > 0)
				{ // write message to file (default: to %WINDIR%\Logs\Service)
					try {
						string logfiledir = logpath;
						if (logfiledir  == "") logfiledir = Environment.GetEnvironmentVariable("WINDIR") + "\\Logs\\Service";
						if (!Directory.Exists(logfiledir)) Directory.CreateDirectory(logfiledir);
						string logfilepath = logfiledir + "\\" + Path.GetFileNameWithoutExtension(System.AppDomain.CurrentDomain.FriendlyName) + DateTime.Now.ToString("yyyyMMdd") + ".log";
						using (StreamWriter sw = File.AppendText(logfilepath))
						{	sw.WriteLine(DateTime.Now + ": " + message); }
					}
					catch
					{
						// ignore failure, just don't log
					}
				}
			}
		}

		private void WriteVerboseToLog(string message)
		{ // helper function to log verbose
			if ((loglevel > 1) || forceverbose) WriteToLog(message);
		}
		#endregion

		#region named pipes routines
		// might parameterize buffer size so they can be passed by application
		private const Int32 SERVER_IN_BUFFER_SIZE = 65536;
		private const Int32 SERVER_OUT_BUFFER_SIZE = 65536;
		PipeSecurity pipeSecurity;
		Int32 instanceCounter;
		bool isRunning;
		Dictionary<PipeStream, IpcPipeData> pipeDict = new Dictionary<PipeStream, IpcPipeData>();

		struct IpcPipeData
		{
			public PipeStream pipe;
			public Object state;
			public Byte[] data;
		};


		// start pipeCount instances of Named Pipes Server pipeName
		private void StartServer()
		{
			// set flag to running
			isRunning = true;

			// provide full access to the current user so more pipe instances can be created
			pipeSecurity = new PipeSecurity();
			pipeSecurity.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
			// all authenticated users are allow to connect, read and write to pipe
			pipeSecurity.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));

			// start accepting connections
			WriteToLog("Starting " + pipeCount + " Named Pipes server instances for pipe \"" + pipeName + "\".");
			for (int i = 0; i < pipeCount; ++i) CreatePipe();
			WriteVerboseToLog("Named Pipes server instances for pipe \"" + pipeName + "\" started.");
		}

		// stop all Named Pipes Servers
		private void StopServer()
		{
			WriteToLog("Stopping Named Pipes server instances.");
			
			// close all pipes asynchronously
			lock(pipeDict)
			{
				isRunning = false;
				foreach(var pipe in pipeDict.Keys) pipe.Close();
			}

			// wait for all pipes to close
			for (;;)
			{
				int count;
				lock(pipeDict)
				{
					count = pipeDict.Count;
				}
				if (count == 0) break;
				System.Threading.Thread.Sleep(5);
			}
			WriteVerboseToLog("Named Pipes server instances stopped.");
		}

		// create a single Named Pipe
		private void CreatePipe()
		{
			// create message-mode pipe to simplify message transition
			// assume all messages will be smaller than the pipe buffer sizes
			NamedPipeServerStream pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, -1, // maximum instances
				PipeTransmissionMode.Message, PipeOptions.Asynchronous | PipeOptions.WriteThrough, SERVER_IN_BUFFER_SIZE,
				SERVER_OUT_BUFFER_SIZE, pipeSecurity);

			// asynchronously accept a client connection
			WriteVerboseToLog("Waiting for client connection...");
			pipe.BeginWaitForConnection(OnClientConnected, pipe);
		}

		// Named Pipes client connection
		private void OnClientConnected(IAsyncResult result)
		{
			// complete the client connection
			NamedPipeServerStream pipe = (NamedPipeServerStream) result.AsyncState;
			pipe.EndWaitForConnection(result);

			// create client pipe structure
			IpcPipeData pd = new IpcPipeData();
			pd.pipe = pipe;
			pd.state = null;
			pd.data = new Byte[SERVER_IN_BUFFER_SIZE];

			// add connection to connection list
			bool running;
			lock(pipeDict)
			{
				running = isRunning;
				if (running) pipeDict.Add(pd.pipe, pd);
			}

			// if server is still running
			if (running)
			{
				// prepare for next connection
				CreatePipe();

				// alert server that client connection exists -> increment count
				Int32 count = System.Threading.Interlocked.Increment(ref instanceCounter);
				WriteVerboseToLog("Client connected to server instance " + count + ".");
				pd.state = count;

				// accept messages
				WriteVerboseToLog("Server instance " + count + ": start reading message.");
				BeginRead(pd);
			}
			else
			{
				pipe.Close();
			}
		}

		// read message from Named Pipes client
		private void BeginRead(IpcPipeData pd)
		{
			// asynchronously read a request from the client
			bool isConnected = pd.pipe.IsConnected;
			if (isConnected)
			{
				try	{
					pd.pipe.BeginRead(pd.data, 0, pd.data.Length, OnAsyncMessage, pd);
				}
				catch (Exception)
				{
					isConnected = false;
				}
			}

			if (!isConnected)
			{
				pd.pipe.Close();
				WriteVerboseToLog("Server instance " + (Int32) pd.state + ": client disconnected.");
				lock(pipeDict)
				{
					pipeDict.Remove(pd.pipe);
				}
			}
		}

		// complete reading message from Named Pipes client
		private void OnAsyncMessage(IAsyncResult result)
		{
			// async read from client completed
			IpcPipeData pd = (IpcPipeData) result.AsyncState;
			Int32 bytesRead = pd.pipe.EndRead(result);
			if (bytesRead != 0)
			{
				WriteVerboseToLog("Server instance " + (Int32) pd.state + " received message: " + Encoding.UTF8.GetString(pd.data, 0, bytesRead));
				
				// *** INSERT YOUR CODE TO HANDLE MESSAGE FROM HERE
				string responseMessage = "Response of server instance " + (Int32) pd.state + " to message \"" + Encoding.UTF8.GetString(pd.data, 0, bytesRead) + "\"";
				pd.data = Encoding.UTF8.GetBytes(responseMessage);
				// *** INSERT YOUR CODE TO HANDLE MESSAGE TO HERE

				// write results to Named Pipe
				try	{
					WriteVerboseToLog("Server instance " + (Int32) pd.state + " sends message: " + Encoding.UTF8.GetString(pd.data, 0, pd.data.Length));
					pd.pipe.BeginWrite(pd.data, 0, pd.data.Length, OnAsyncWriteComplete, pd.pipe);
				}
				catch (Exception)
				{
					WriteVerboseToLog("Server instance " + (Int32) pd.state + " error, closing connection.");
					pd.pipe.Close();
				}
			}
			// more messages to come?
			BeginRead(pd);
		}

		// complete writing response message to Named Pipes client
		private void OnAsyncWriteComplete(IAsyncResult result)
		{
			PipeStream pipe = (PipeStream) result.AsyncState;
			pipe.EndWrite(result);
		}
		#endregion
	}
}
