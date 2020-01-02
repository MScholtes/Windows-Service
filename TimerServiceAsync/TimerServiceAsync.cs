// C# timer service
// starts a program or script every 30 seconds
// service can be configured via ServiceConfig.xml in the directory of the binary
// Markus Scholtes, 2020/01/02

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Timers;
using System.Xml;

// set executable properties
using System.Reflection;
[assembly:AssemblyTitle("TimerServiceAsync")]
[assembly:AssemblyDescription("TimerService that starts a process entry every 5 seconds")]
[assembly:AssemblyConfiguration("")]
[assembly:AssemblyCompany("MS")]
[assembly:AssemblyProduct("TimerServiceAsync")]
[assembly:AssemblyCopyright("© Markus Scholtes 2020")]
[assembly:AssemblyTrademark("")]
[assembly:AssemblyCulture("")]
[assembly:AssemblyVersion("1.0.0.0")]
[assembly:AssemblyFileVersion("1.0.0.0")]

namespace NaSpTimerServiceAsync
{
	public partial class TimerServiceAsync : ServiceBase
	{
		static int Main(string[] arguments)
		{ // entry point
			// declaration as int, transfer of parameters and return of a value are ignored when called via the service manager
			TimerServiceAsync service = new TimerServiceAsync();
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
		double timerinterval = 30000; // milliseconds
		int pulsetype = 1; // 0 - no action, 1 - start action every pulse ...
		bool oncerun; // marker for "start action only one time"
		string processtostart; // process to start
		string parameters; // parameters for process
		string workingdirectory; // working directory for process
		string session; // target session for process
		bool hide; // hide process window
		bool runas; // run process as session user
		int elevate = -1; // -1 - no action, 0 - unelevate, 1 - elevate
		public Process process = null; // started process

		#region service structure
		// timer object for action pulse
		Timer timer = new Timer();

		public TimerServiceAsync()
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
			WriteToLog("Starting service " + ServiceName + "...");
			// prepare and start timer
			ElapsedEventHandler eventHandler = new ElapsedEventHandler(TimeTick);
			timer.Elapsed += eventHandler;
			timer.Interval = timerinterval; // milliseconds
			timer.AutoReset = true;
			timer.Enabled = true;

			// check command line parameter of service, e.g. "sc start TimerServiceAsync VERBOSE"
			if ((args.Length > 0) && (args[0].ToUpper() == "VERBOSE"))
			{ // verbose mode per command line parameter
				forceverbose = true;
				WriteToLog("Verbose mode forced per command line parameter.");
			}

			// manually start action asynchronous - service manager does not have to wait
			// execute the event handler as independant thread, independant thread calls callback function on finish
			eventHandler.BeginInvoke(this, null, new AsyncCallback(TimeTick_ElapsedCallback), eventHandler);

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

		protected override void OnPause()
		{ // called from service manager on service start

			// request additional time from service manager to wait in milliseconds
			// RequestAdditionalTime(2000);
			timer.Enabled = false;
			WriteToLog("Service " + ServiceName + " paused.");
		}

		// declare delegate for callback
		private delegate void AsyncMethodCaller(object source, ElapsedEventArgs e);

		protected override void OnContinue()
		{ // called from service manager on service continuation

			// request additional time from service manager to wait in milliseconds
			// RequestAdditionalTime(2000);
			WriteToLog("Service " + ServiceName + " continued.");
			if (pulsetype == 9) oncerun = false; // start once
			timer.Enabled = true; //reenable timer

			// manually start action TimeTick() asynchronous
			AsyncMethodCaller caller = new AsyncMethodCaller(this.TimeTick);
			caller.BeginInvoke(this, null, new AsyncCallback(TimeTick_ElapsedCallback), caller);
		}

		protected override void OnStop()
		{ // called from service manager on service stop
			// ExitCode must be set before OnStop() is called!

			// request additional time from service manager to wait in milliseconds
			// RequestAdditionalTime(2000);
			timer.Stop();
			timer.Close(); // end timer
			WriteToLog("Service " + ServiceName + " stopped.");
		}
		#endregion

		private void TimeTick(object source, ElapsedEventArgs e)
		{ // worker function that is initially called manually and then by timer event

			// configuration is read and set on every pulse (where possible)
			ReadConfiguration();
			if (timer.Interval != timerinterval)
				timer.Interval = timerinterval; // set time interval in milliseconds (only if changed to avoid resetting)

			// call action if no fatal error and configured to
			if (ExitCode == 0)
			{ // only if no fatal error

				// check if process is still running from last pulse
				bool processalive = false;
				if (process != null)
				{
					if (process.HasExited)
						process.Close();
					else
						processalive = true;
				}
				if (processalive)
				{ // process is still running
					WriteVerboseToLog(processtostart + " is still active.");
					if ((pulsetype & 4) == 4)
					{ // end process if configured to
						WriteVerboseToLog("Stopping process " + processtostart + ".");
						try {
							process.Kill();
							process.Close();
							process = null;
						}
						catch (Exception ex)
						{
							WriteToLog("Cannot stop process " + processtostart + ".");
							WriteToLog("Error: " + ex.Message);
						}
					}
				}
				else
					WriteVerboseToLog(processtostart + " is stopped.");

				if (((pulsetype & 11) == 1) || (((pulsetype & 11) == 2) && !processalive) || (((pulsetype & 11) == 3) && processalive) || ((pulsetype >= 8) && !oncerun))
				{ // call action
					WriteToLog("Starting " + processtostart + " with the parameters " + parameters);
					if (process != null) process.Close();
					StartProcessInSession(session, processtostart, parameters, workingdirectory, runas, hide);

					if (pulsetype >= 8) oncerun = true; // notice action call for "one time modes"
				}
			}

			// on error set ExitCode here to stop service and generate error message in system log
			// (example: 5 returns "Access Denied"):
			// if (ErrorHasHappened) ExitCode = 5;
			if (ExitCode != 0) Stop();
		}

		private void TimeTick_ElapsedCallback(IAsyncResult ar)
		{ // callback function for asynchronous start of event handler
			ElapsedEventHandler handlerDelegate = ar.AsyncState as ElapsedEventHandler;
			// call EndInvoke just to be sure to free all resources since there is no result to obtain
			if (handlerDelegate != null) handlerDelegate.EndInvoke(ar);
		}

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

				// timer value in milliseconds
				tempStr = ReadXmlText(xmlDocument, "/serviceconfig/service/timer", "30000");
				if (Int32.TryParse(tempStr, out tempInt))
					if ((tempInt > 0) && (tempInt <= Int32.MaxValue))
						timerinterval = tempInt;

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

		private void ReadConfiguration()
		{ // read advanced configuration out of xml file

			XmlDocument xmlDocument = new XmlDocument();
			try {
				// load xml file ServiceConfig.xml in directory of binary
				xmlDocument.Load(AppDomain.CurrentDomain.BaseDirectory + "\\ServiceConfig.xml");

				// logpath: path to directory for logfiles (logtarget = 1), empty: %WINDIR%\Logs\Service
				string tempStr = ReadXmlText(xmlDocument, "/serviceconfig/service/logpath", "").TrimEnd('\\');
				if (logpath != tempStr) logpath = tempStr;

				// where to log: 0 - none. Or sum of: 1 - file, 2 - application log, 4 - console (only for interactive mode)
				tempStr = ReadXmlText(xmlDocument, "/serviceconfig/service/logtarget", "1");
				int tempInt;
				if (Int32.TryParse(tempStr, out tempInt))
				{
					if ((tempInt >= 0) && (tempInt <= 7))
						logtarget = tempInt;
					else
						WriteToLog("Invalid value for logging target.");
				}
				else
					WriteToLog("Error when setting the logging target.");

				// loglevel: 0 - none, 1 - normal, 2 - verbose
				switch(ReadXmlText(xmlDocument, "/serviceconfig/service/loglevel", "1"))
				{
					case "0": if (loglevel != 0)
										{
											WriteToLog("Log deactivated.");
											loglevel = 0;
										}
										break;
					case "1": if (loglevel != 1)
										{
											loglevel = 1;
											WriteToLog("Normal logging mode activated.");
										}
										break;
					case "2": if (loglevel != 2)
										{
											loglevel = 2;
											WriteToLog("Detailed logging mode activated.");
										}
										break;
					default:  WriteToLog("Invalid value for logging mode.");
										break;
				}

				// timer value in milliseconds
				tempStr = ReadXmlText(xmlDocument, "/serviceconfig/service/timer", "30000");
				if (Int32.TryParse(tempStr, out tempInt))
				{
					if ((tempInt > 0) && (tempInt <= Int32.MaxValue))
						timerinterval = tempInt;
					else
						WriteToLog("Invalid value for time interval.");
				}
				else
					WriteToLog("Error when setting the time interval.");

				// pulsetype: action to perform in every pulse
				tempStr = ReadXmlText(xmlDocument, "/serviceconfig/service/pulsetype", "1");
				if (Int32.TryParse(tempStr, out tempInt))
				{
					if ((tempInt >= 0) && (tempInt <= 9))
					{
						if (pulsetype != tempInt)
						{
							pulsetype = tempInt;
							WriteToLog("Set pulse type to mode " + pulsetype.ToString() + ".");
						}
					}
					else
						WriteToLog("Invalid value for pulse type.");
				}
				else
					WriteToLog("Error when setting the pulse type.");

				// commandline: command line for program to start
				processtostart = Environment.ExpandEnvironmentVariables(ReadXmlText(xmlDocument, "/serviceconfig/program/commandline", "cmd.exe"));
				// parameters: parameters for program if any
				parameters = ReadXmlText(xmlDocument, "/serviceconfig/program/parameters", "");
				// workingdirectory: working directory for program if any
				workingdirectory = Environment.ExpandEnvironmentVariables(ReadXmlText(xmlDocument, "/serviceconfig/program/workingdirectory", ""));
				// session: target session for process (default: first active user session)
				session = ReadXmlText(xmlDocument, "/serviceconfig/program/session", "active");
				// runas: run process as session user
				tempStr = ReadXmlText(xmlDocument, "/serviceconfig/program/runas", "false").ToLower();
				if (tempStr == "true")
					runas = true;
				else
					runas = false;
				// elevate: elevation of process
				tempStr = ReadXmlText(xmlDocument, "/serviceconfig/program/elevation", "none").ToLower();
				switch (tempStr)
				{
					case "true":
						elevate = 1;
						break;
					case "false":
						elevate = 0;
						break;
					case "none":
						elevate = -1;
						break;
					default:
						WriteToLog("Invalid value for elevation.");
						break;
				}
				// hide: hide process window
				tempStr = ReadXmlText(xmlDocument, "/serviceconfig/program/hide", "false").ToLower();
				if (tempStr == "true")
					hide = true;
				else
					hide = false;
			}
			catch (Exception ex)
			{
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
					catch {
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
					catch {
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

		#region Win32 API declarations
		// Win32 API declarations from www.pinvoke.net (mostly)

		// session management
		private enum WTS_CONNECTSTATE_CLASS
		{
			WTSActive,
			WTSConnected,
			WTSConnectQuery,
			WTSShadow,
			WTSDisconnected,
			WTSIdle,
			WTSListen,
			WTSReset,
			WTSDown,
			WTSInit
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct WTS_SESSION_INFO
		{
			public readonly uint SessionID;
			[MarshalAs(UnmanagedType.LPStr)]
			public readonly String pWinStationName;
			public readonly WTS_CONNECTSTATE_CLASS State;
		}

		private enum WTS_INFO_CLASS
		{
			WTSInitialProgram,
			WTSApplicationName,
			WTSWorkingDirectory,
			WTSOEMId,
			WTSSessionId,
			WTSUserName,
			WTSWinStationName,
			WTSDomainName,
			WTSConnectState,
			WTSClientBuildNumber,
			WTSClientName,
			WTSClientDirectory,
			WTSClientProductId,
			WTSClientHardwareId,
			WTSClientAddress,
			WTSClientDisplay,
			WTSClientProtocolType,
			WTSIdleTime,
			WTSLogonTime,
			WTSIncomingBytes,
			WTSOutgoingBytes,
			WTSIncomingFrames,
			WTSOutgoingFrames,
			WTSClientInfo,
			WTSSessionInfo,
		}

		[DllImport("kernel32.dll")]
		private static extern uint WTSGetActiveConsoleSessionId();

		[DllImport("wtsapi32.dll", SetLastError = true)]
		private static extern int WTSEnumerateSessions(
			IntPtr hServer,
			[MarshalAs(UnmanagedType.U4)] int Reserved,
			[MarshalAs(UnmanagedType.U4)] int Version,
			ref IntPtr ppSessionInfo,
			[MarshalAs(UnmanagedType.U4)] ref int pCount);

		[DllImport("Wtsapi32.dll")]
		private static extern bool WTSQuerySessionInformation(IntPtr hServer, uint sessionId, WTS_INFO_CLASS wtsInfoClass, out IntPtr ppBuffer, out int pBytesReturned);

		[DllImport("wtsapi32.dll")]
		static extern void WTSFreeMemory(IntPtr pMemory);

		[DllImport("wtsapi32.dll", SetLastError = true)]
		private static extern uint WTSQueryUserToken(uint sessionId, ref IntPtr hToken);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool CloseHandle(IntPtr hHandle);

		// token management
		private enum SECURITY_IMPERSONATION_LEVEL
		{
			SecurityAnonymous = 0,
			SecurityIdentification = 1,
			SecurityImpersonation = 2,
			SecurityDelegation = 3,
		}

		private enum TOKEN_TYPE
		{
			TokenPrimary = 1,
			TokenImpersonation = 2
		}

		private enum TOKEN_ELEVATION_TYPE
		{
		 TokenElevationTypeDefault = 1,
		 TokenElevationTypeFull,
		 TokenElevationTypeLimited
		}

		private enum TOKEN_INFORMATION_CLASS
		{
			TokenUser = 1,
			TokenGroups,
			TokenPrivileges,
			TokenOwner,
			TokenPrimaryGroup,
			TokenDefaultDacl,
			TokenSource,
			TokenType,
			TokenImpersonationLevel,
			TokenStatistics,
			TokenRestrictedSids,
			TokenSessionId,
			TokenGroupsAndPrivileges,
			TokenSessionReference,
			TokenSandBoxInert,
			TokenAuditPolicy,
			TokenOrigin,
			TokenElevationType,
			TokenLinkedToken,
			TokenElevation,
			TokenHasRestrictions,
			TokenAccessInformation,
			TokenVirtualizationAllowed,
			TokenVirtualizationEnabled,
			TokenIntegrityLevel,
			TokenUIAccess,
			TokenMandatoryPolicy,
			TokenLogonSid,
			MaxTokenInfoClass
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct TOKEN_LINKED_TOKEN
		{
			public IntPtr LinkedToken;
		}

		[DllImport("advapi32.dll", SetLastError=true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool OpenProcessToken(IntPtr hProcess, uint dwDesiredAccess, out IntPtr hToken);

		[DllImport("advapi32.dll", CharSet=CharSet.Auto, SetLastError=true)]
		private extern static bool DuplicateTokenEx(
			IntPtr hExistingToken,
			uint dwDesiredAccess,
			/*ref*/ IntPtr lpTokenAttributes, // since we only call this with null we omit ref and type for laziness
			SECURITY_IMPERSONATION_LEVEL ImpersonationLevel,
			TOKEN_TYPE TokenType,
			out IntPtr phNewToken);

		[DllImport("advapi32.dll", SetLastError=true)]
		private static extern bool GetTokenInformation(IntPtr hToken, TOKEN_INFORMATION_CLASS TokenInformationClass,
			IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

		[DllImport("advapi32.dll", SetLastError=true)]
		private static extern bool SetTokenInformation(IntPtr hToken, TOKEN_INFORMATION_CLASS TokenInformationClass,
			IntPtr TokenInformation, uint TokenInformationLength);

		// Process management
		[DllImport("userenv.dll", SetLastError=true)]
		private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

		[DllImport("userenv.dll", SetLastError=true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct STARTUPINFO
		{
			public int cb;
			public string lpReserved;
			public string lpDesktop;
			public string lpTitle;
			public int dwX;
			public int dwY;
			public int dwXSize;
			public int dwYSize;
			public int dwXCountChars;
			public int dwYCountChars;
			public int dwFillAttribute;
			public int dwFlags;
			public short wShowWindow;
			public short cbReserved2;
			public IntPtr lpReserved2;
			public IntPtr hStdInput;
			public IntPtr hStdOutput;
			public IntPtr hStdError;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct PROCESS_INFORMATION
		{
			public IntPtr hProcess;
			public IntPtr hThread;
			public int dwProcessId;
			public int dwThreadId;
		}

		[DllImport("advapi32.dll", SetLastError=true, CharSet=CharSet.Auto)]
		private static extern bool CreateProcessAsUser(IntPtr hToken, string lpApplicationName, string lpCommandLine,
		 /* ref */ IntPtr lpProcessAttributes, /* ref */ IntPtr lpThreadAttributes, // since we only call this with null we omit ref and type for laziness
		 bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory,
		 ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

		#endregion
		#region process and session helper functions
		// managed code

		private uint GetConsoleSession()
		{ // return console session id, returns 0xFFFFFFFF if no one is logged on
			return WTSGetActiveConsoleSessionId();
		}

		private uint GetUserSession(string partofusername, WTS_CONNECTSTATE_CLASS sessionstate)
		{ // get the first found user session with desired name port or - if name part string is null - with desired sessionstate

			uint sessionId = 0xFFFFFFFF;
			IntPtr pSessionInfo = IntPtr.Zero;
			int sessionCount = 0;
			int dataSize = Marshal.SizeOf(typeof(WTS_SESSION_INFO));

			if (WTSEnumerateSessions(IntPtr.Zero, 0, 1, ref pSessionInfo, ref sessionCount) != 0) // IntPtr.Zero = WTS_CURRENT_SERVER_HANDLE
			{ // get information array of logon sessions
				IntPtr currentSessionInfo = pSessionInfo;

				for (int i = 0; i < sessionCount; i++)
				{	// enumerate sessions (walk through array)
					WTS_SESSION_INFO si = (WTS_SESSION_INFO)Marshal.PtrToStructure(currentSessionInfo, typeof(WTS_SESSION_INFO));
					currentSessionInfo += dataSize;

					if (partofusername != null)
					{ // search for session with part of username
						IntPtr nameBuffer;
						int nameLen;

						// query for user name of session
						if (WTSQuerySessionInformation(IntPtr.Zero, si.SessionID, WTS_INFO_CLASS.WTSUserName, out nameBuffer, out nameLen))
						{ // Session 0 and "listening" session 65536 return username "\0"
							string username = Marshal.PtrToStringAnsi(nameBuffer);
							if (username.Length > 0)
							{ // session has a user
								if (username.ToLower().Contains(partofusername.ToLower()))
								{ // user name of session contains search word
									sessionId = si.SessionID;
									break;
								}
							}
							// free memory
							WTSFreeMemory(nameBuffer);
						}
					}
					else
					{ // check if session has desired state and is not the "service" session 0
						if ((si.State == sessionstate) && (si.SessionID != 0))
						{
							sessionId = si.SessionID;
							break;
						}
					}
				}
				// free memory
				WTSFreeMemory(pSessionInfo);
			}

			return sessionId; // retrun found session id or 0xFFFFFFFF
		}

		private IntPtr GetTokenFromSession(uint sessionid)
		{ // get security token of session

			IntPtr hSessionToken = IntPtr.Zero;
			IntPtr hDuplicatedToken = IntPtr.Zero;

			// get security token of session
			// SE_TCB_NAME privilege required to get token!
			if (WTSQueryUserToken(sessionid, ref hSessionToken) != 0)
			{ // duplicate token to create a new process
				if (!DuplicateTokenEx(hSessionToken, 0, IntPtr.Zero, SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, out hDuplicatedToken))
					hDuplicatedToken = IntPtr.Zero;

				CloseHandle(hSessionToken);
			}

			return hDuplicatedToken;
		}

		private IntPtr GetTokenFromCurrentProcess()
		{ // get token of current process

			IntPtr hToken;
			// 0x018B = TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID
			if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x018B, out hToken))
			{ // could not open process token
				return IntPtr.Zero;
			}

			IntPtr hDuplicatedToken;
			if (!DuplicateTokenEx(hToken, 0, IntPtr.Zero, SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, out hDuplicatedToken))
			{ // could not duplicate process token
				CloseHandle(hToken);
				return IntPtr.Zero;
			}

			CloseHandle(hToken);
			// return duplicated token
			return hDuplicatedToken;
		}

		private void GetElevatedToken(ref IntPtr hToken, bool elevate)
		{ // get linked token from unelevated token

			TOKEN_ELEVATION_TYPE requestedElevation = TOKEN_ELEVATION_TYPE.TokenElevationTypeLimited;
			if (!elevate) requestedElevation = TOKEN_ELEVATION_TYPE.TokenElevationTypeFull;

			uint dwSize = (uint)sizeof(TOKEN_ELEVATION_TYPE);
			IntPtr TokenInformation = Marshal.AllocHGlobal((int)dwSize);

			// retrieve information "TokenElevationType" for token hToken
			if (GetTokenInformation(hToken, TOKEN_INFORMATION_CLASS.TokenElevationType, TokenInformation, dwSize, out dwSize))
			{
				TOKEN_ELEVATION_TYPE currentElevation = (TOKEN_ELEVATION_TYPE)Marshal.ReadInt32(TokenInformation);
				if (currentElevation == requestedElevation)
				{ // UAC enabled and linked token exist. Token has not the requested state so we must retrieve linked token
					WriteVerboseToLog("Token has not the desired elevation state, retrieving linked token.");

					TOKEN_LINKED_TOKEN linkedToken;
					linkedToken.LinkedToken = IntPtr.Zero;
					dwSize = (uint)Marshal.SizeOf(linkedToken);

					// we have to hand unmanaged memory to GetTokenInformation, so we have to marshal
					IntPtr tokenBuffer = Marshal.AllocCoTaskMem(Marshal.SizeOf(linkedToken));
					Marshal.StructureToPtr(linkedToken, tokenBuffer, false);

					if (GetTokenInformation(hToken, TOKEN_INFORMATION_CLASS.TokenLinkedToken, tokenBuffer, dwSize, out dwSize))
					{ // retrieve linked token

						// token is stored in unmanaged memory, convert to managed
						linkedToken = (TOKEN_LINKED_TOKEN)Marshal.PtrToStructure(tokenBuffer, typeof(TOKEN_LINKED_TOKEN));

						IntPtr hElevated;
						if (DuplicateTokenEx(linkedToken.LinkedToken, 0, IntPtr.Zero, SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, out hElevated))
						{ // linked token successfully duplicated, free original token and return linked token
							CloseHandle(hToken);
							hToken = hElevated;
							WriteVerboseToLog("Successfully gained elevated token.");
						}

						// close handle
						CloseHandle(linkedToken.LinkedToken);
					}
					else
						WriteVerboseToLog("Could not retrieve linked token. Error: " + Marshal.GetLastWin32Error().ToString());

					// Free the unmanaged memory
					Marshal.FreeHGlobal(tokenBuffer);
				}
				else
				{ // UAC enabled and token has the desired state
					if (currentElevation == TOKEN_ELEVATION_TYPE.TokenElevationTypeDefault)
					{ // UAC disabled or token is not administrative or token belongs to LOCALSYSTEM
						WriteVerboseToLog("Cannot change elevation: UAC is disabled or token is not administrative or token belongs to LOCALSYSTEM.");
					}
					else
					{ // token already has the desired state, nothing to do
						WriteVerboseToLog("Token already has the desired elevation state.");
					}
				}
			}
			else
			{ // Error getting information on token
				WriteVerboseToLog("Could not get token information. Error: " + Marshal.GetLastWin32Error().ToString());
			}

			// Free the unmanaged memory.
			 Marshal.FreeHGlobal(TokenInformation);
		}

		private bool SetTokenSession(ref IntPtr hToken, uint sessionId)
		{ // set session id of token

			// alloc 4 bytes for the value
			IntPtr sessionIDPtr = Marshal.AllocHGlobal(4);

			// convert value to byte[] and copy to sessionIDPtr
			Marshal.Copy(BitConverter.GetBytes(sessionId), 0, sessionIDPtr, 4);

			if (!SetTokenInformation(hToken, TOKEN_INFORMATION_CLASS.TokenSessionId, sessionIDPtr, 4))
			{ // failed to set session id of token
				return false;
			}

			// free memory
			Marshal.FreeHGlobal(sessionIDPtr);
			return true;
		}
		#endregion

		private void StartProcess(string processName, string parameter, string workingDirectory)
		{ // function to start process, on failure process object is null

			ProcessStartInfo startInfo = new ProcessStartInfo
			{ // configuration for the process start
				FileName = processName,
				Arguments = parameter,
				CreateNoWindow = false,
				ErrorDialog = false,
				LoadUserProfile = false,
				RedirectStandardError = false,
				RedirectStandardInput = false,
				RedirectStandardOutput = false,
				UseShellExecute = true,
				WindowStyle = ProcessWindowStyle.Normal,
				WorkingDirectory = workingDirectory
			};

			process = new Process();
			process.StartInfo = startInfo;
			try {
				// start process
				process.Start();
			}
			catch (Exception ex)
			{
				WriteToLog("Cannot start process '" + processName + "'.");
				WriteToLog("Error: " + ex.Message);
				process = null;
			}
		}

		private void StartProcessInSession(string targetsession, string processName, string parameter, string workingDirectory, bool runas, bool hide)
		{
			uint targetSessionId = 0xFFFFFFFF;

			process = null;
			WriteVerboseToLog("Starting process " + processName + " in session " + targetsession + ".");

			int tempInt;
			if (Int32.TryParse(targetsession, out tempInt))
			{ // target session specified as number
				if (tempInt >= 0) targetSessionId = (uint)tempInt;
			}
			else
			{ // target session specified as text
				if ((targetsession.Length > 5) && (targetsession.ToLower().Substring(0, 5) == "user:"))
					{ // search for session of user with name part is given after "user:"
						targetSessionId = GetUserSession(targetsession.Substring(5), WTS_CONNECTSTATE_CLASS.WTSActive);
					}
				else
				{ // search for specified session type
					switch (targetsession.ToLower())
					{
						case "service":
						case "services":
							targetSessionId = 0;
							break;

						case "console":
							targetSessionId = GetConsoleSession();
							break;

						case "active":
							targetSessionId = GetUserSession(null, WTS_CONNECTSTATE_CLASS.WTSActive);
							break;

						case "disconnected":
							targetSessionId = GetUserSession(null, WTS_CONNECTSTATE_CLASS.WTSDisconnected);
							break;
					}
				}
			}

			if (targetSessionId == 0xFFFFFFFF)
			{ // could not find selected session
				WriteVerboseToLog("Target session " + targetsession + " not found or accessible.");
				return;
			}

			if ((targetSessionId == 0) && (Process.GetCurrentProcess().SessionId == 0))
			{ // in session 0
				WriteVerboseToLog("Start in session of service requested, direct start");
				// ignore hide parameter because no one "sees" session 0
				// ignore runas parameter because there is user for session 0
				StartProcess(processName, parameter, workingDirectory);
				return;
			}

			WriteVerboseToLog("Start in session " + targetSessionId.ToString() + " requested.");
			IntPtr hProcessToken;
			if (runas)
			{ // start process as session user
				if (targetSessionId == 0)
				{ // cannot start with runas in session 0
					WriteVerboseToLog("Cannot start program in service session because there is no session user.");
					return;
				}

				hProcessToken = GetTokenFromSession(targetSessionId);
				if (hProcessToken == IntPtr.Zero)
				{ // could not retrieve session token
					WriteVerboseToLog("Cannot retrieve token of target session " + targetsession + ". Error: " + Marshal.GetLastWin32Error().ToString());
					return;
				}

			}
			else
			{ // start process as own process user
				hProcessToken = GetTokenFromCurrentProcess();
				if (hProcessToken == IntPtr.Zero)
				{ // could not retrieve session token
					WriteVerboseToLog("Cannot retrieve token of target session " + targetsession + ". Error: " + Marshal.GetLastWin32Error().ToString());
					return;
				}

				// do I have to change the session?
				if (targetSessionId != Process.GetCurrentProcess().SessionId)
					if (!SetTokenSession(ref hProcessToken, targetSessionId))
					{ // could not set session id of token
						WriteVerboseToLog("Cannot set session " + targetsession + ". Error: " + Marshal.GetLastWin32Error().ToString());
						return;
					}
			}

			if (elevate >= 0)
			{ // elevation or unelevation requested
				GetElevatedToken(ref hProcessToken, elevate == 1);
			}

			IntPtr pEnvironment = IntPtr.Zero;
			if (!CreateEnvironmentBlock(out pEnvironment, hProcessToken, false))
			{ // could not create environment block
				pEnvironment = IntPtr.Zero;
				WriteVerboseToLog("Cannot create environment block for target session, continuing without.");
			}

			if (parameter != "") processName += " " + parameter;
			if (workingDirectory == "") workingDirectory = null;
			uint dwCreationFlags = 0x00000410; // CREATE_NEW_CONSOLE + CREATE_UNICODE_ENVIRONMENT
			PROCESS_INFORMATION pi = new PROCESS_INFORMATION();
			STARTUPINFO si = new STARTUPINFO();
			// Initialize struct
			si.cb = Marshal.SizeOf(si);
			si.dwFlags = 0x00000001; // STARTF_USESHOWWINDOW
			if (hide)
				si.wShowWindow = 0; // SW_HIDE
			else
				si.wShowWindow = 5; // SW_SHOW
			si.lpDesktop = "winsta0\\default";

			int rc = 0;
			if (!CreateProcessAsUser(hProcessToken, null, processName, IntPtr.Zero, IntPtr.Zero, false, dwCreationFlags, pEnvironment, workingDirectory, ref si, out pi))
			{ // could not start process
				WriteVerboseToLog("Cannot start process " + processName + ". Error: " + Marshal.GetLastWin32Error().ToString());
				rc = -1;
			}
			WriteVerboseToLog("Process " + processName + " started in session " + targetSessionId.ToString() + ".");

			if (pEnvironment != IntPtr.Zero)
			{ // free memory of environment block (process has a copy)
				DestroyEnvironmentBlock(pEnvironment);
			}

			if (rc == 0)
			{ // store process object
				process = Process.GetProcessById(pi.dwProcessId);
				// close handles that only exist when no error occurred
				CloseHandle(pi.hThread);
				CloseHandle(pi.hProcess);
			}
			// close remaining handle
			CloseHandle(hProcessToken);
		}
	}
}
