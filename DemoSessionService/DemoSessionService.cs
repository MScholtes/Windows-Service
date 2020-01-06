// C# demo of a service
// service that logs session events
// service can be configured via ServiceConfig.xml in the directory of the binary
// Markus Scholtes, 2020/01/06

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Xml;

// set executable properties
using System.Reflection;
[assembly:AssemblyTitle("DemoSessionService")]
[assembly:AssemblyDescription("Service that logs session events")]
[assembly:AssemblyConfiguration("")]
[assembly:AssemblyCompany("MS")]
[assembly:AssemblyProduct("DemoSessionService")]
[assembly:AssemblyCopyright("© Markus Scholtes 2020")]
[assembly:AssemblyTrademark("")]
[assembly:AssemblyCulture("")]
[assembly:AssemblyVersion("1.0.0.0")]
[assembly:AssemblyFileVersion("1.0.0.0")]

namespace NaSpDemoSessionService
{
	public partial class DemoSessionService : ServiceBase
	{
		static int Main(string[] arguments)
		{ // entry point
			// declaration as int, transfer of parameters and return of a value are ignored when called via the service manager
			DemoSessionService service = new DemoSessionService();
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

		bool servicepaused = false; // has service paused?
		int loglevel = 0; // initialization without logging
		bool forceverbose = false; // loglevel 2 forced by command line parameter
		int logtarget = 1; // default: log to file
		string logpath; // standard path for file
		int userCount = 0; // number of users

		#region service structure

		public DemoSessionService()
		{ // constructor, set base configuration that can only be set in constructor
			AutoLog = false; // automatic logging of start, pause, continue and stop in application log?
				// name of message source for eventlog
			ServiceName = Path.GetFileNameWithoutExtension(System.AppDomain.CurrentDomain.FriendlyName);
			CanPauseAndContinue = false; // pause and continue are implemented?
			CanStop = true; // prevent stopping in service manager?
			CanHandleSessionChangeEvent = true; // true is required for OnSessionChange to be called from Service Manager

			// read initial configuration from xml file
			ReadBaseConfiguration();
		}

		protected override void OnStart(string[] args)
		{ // called from service manager on service start

			// the service manager waits about 30 seconds for a service to start before failing
			// you can request additional time from service manager to wait in milliseconds:
			// RequestAdditionalTime(2000);
			WriteToLog("Starting service " + ServiceName + "...");

			// check command line parameter of service, e.g. "sc start DemoSessionService VERBOSE"
			if ((args.Length > 0) && (args[0].ToUpper() == "VERBOSE"))
			{ // verbose mode per command line parameter
				forceverbose = true;
				WriteToLog("Verbose mode forced per command line parameter.");
			}

			// initialize user counter
			userCount = GetSessionCount();
			WriteVerboseToLog(userCount.ToString() + " users are logged on.");
			WriteToLog("Service " + ServiceName + " started.");
		}

		protected override void OnPause()
		{ // called from service manager on service pause

			// request additional time from service manager to wait in milliseconds
			// RequestAdditionalTime(2000);

			// set pausing flag
			servicepaused = true;
			WriteToLog("Service " + ServiceName + " paused.");
		}

		protected override void OnContinue()
		{ // called from service manager on service continuation

			// request additional time from service manager to wait in milliseconds
			// RequestAdditionalTime(2000);

			// have to initialize user counter on continue
			userCount = GetSessionCount();
			servicepaused = false;
			WriteVerboseToLog(userCount.ToString() + " users are logged on.");

			WriteToLog("Service " + ServiceName + " continued.");
		}

		protected override void OnStop()
		{ // called from service manager on service stop
			// ExitCode must be set before OnStop() is called!

			// request additional time from service manager to wait in milliseconds
			// RequestAdditionalTime(2000);
			WriteToLog("Service " + ServiceName + " stopped.");
		}

		protected override void OnSessionChange(SessionChangeDescription changeDescription)
		{ // handle a session change notice

			// if service has paused do nothing
			if (servicepaused) return;

			if ((loglevel > 1) || forceverbose)
			{ // print session information only in verbose mode
				// GetSessionCount() is "expensive", so first check for verbose mode
				WriteToLog("Session change notice received: " + changeDescription.Reason.ToString() + ", session ID: " +  changeDescription.SessionId.ToString());
				WriteToLog(GetSessionCount().ToString() + " users are logged on.");
			}

			switch (changeDescription.Reason)
			{
				case SessionChangeReason.ConsoleConnect:
						WriteToLog("Console connect to session id " + changeDescription.SessionId.ToString());
						break;

				case SessionChangeReason.ConsoleDisconnect:
						WriteToLog("Console disconnect from session id " + changeDescription.SessionId.ToString());
						break;

				case SessionChangeReason.RemoteConnect:
						WriteToLog("Remote connect to session id " + changeDescription.SessionId.ToString());
						break;

				case SessionChangeReason.RemoteDisconnect:
						WriteToLog("Remote disconnect from session id " + changeDescription.SessionId.ToString());
						break;

				case SessionChangeReason.SessionLogon:
						userCount += 1;
						WriteToLog("Session logon by the user " + GetUserFromSession((uint)changeDescription.SessionId) + " to session id: " + changeDescription.SessionId.ToString() + ", total users logged on: " + userCount.ToString());
						break;

				case SessionChangeReason.SessionLogoff:
						userCount -= 1;
						WriteToLog("Session logoff from session id " + changeDescription.SessionId.ToString() + ", total users logged on: " + userCount.ToString());
						break;

				case SessionChangeReason.SessionRemoteControl:
						WriteToLog("Remote control to session of user " + GetUserFromSession((uint)changeDescription.SessionId) + " with session id " + changeDescription.SessionId.ToString());
						break;

				case SessionChangeReason.SessionLock:
						WriteToLog("Session locked by the user " + GetUserFromSession((uint)changeDescription.SessionId) + " on session id " + changeDescription.SessionId.ToString());
						break;

				case SessionChangeReason.SessionUnlock:
						WriteToLog("Session unlocked by the user " + GetUserFromSession((uint)changeDescription.SessionId) + " on session id " + changeDescription.SessionId.ToString());
						break;

				default:
						WriteToLog("Unhandled session event " + changeDescription.Reason.ToString() + " on session of user " + GetUserFromSession((uint)changeDescription.SessionId) + " with session id " + changeDescription.SessionId.ToString());
						break;
			}
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

		[DllImport("wtsapi32.dll", SetLastError = true)]
		private static extern int WTSEnumerateSessions(IntPtr hServer, [MarshalAs(UnmanagedType.U4)] int Reserved,
			[MarshalAs(UnmanagedType.U4)] int Version, ref IntPtr ppSessionInfo, [MarshalAs(UnmanagedType.U4)] ref int pCount);

		[DllImport("wtsapi32.dll")]
		private static extern bool WTSQuerySessionInformation(IntPtr hServer, uint sessionId, WTS_INFO_CLASS wtsInfoClass, out IntPtr ppBuffer, out int pBytesReturned);

		[DllImport("wtsapi32.dll")]
		static extern void WTSFreeMemory(IntPtr pMemory);
		#endregion

		#region session helper functions
		private static string GetUserFromSession(uint sessionId)
		{
			IntPtr nameBuffer;
			int nameLen;
			string userName = "UNKNOWN";

			// query user name of session
			if (WTSQuerySessionInformation(IntPtr.Zero, sessionId, WTS_INFO_CLASS.WTSUserName, out nameBuffer, out nameLen))
			{ // convert user name from buffer to string
				userName = Marshal.PtrToStringAnsi(nameBuffer);
				// free memory
				WTSFreeMemory(nameBuffer);
				if (userName.Length == 0)
					// service session 0 and "listening" session 65536 return user name "\0"
					userName = "SYSTEM";
				else
				{ // query domain name of session user
					if (WTSQuerySessionInformation(IntPtr.Zero, sessionId, WTS_INFO_CLASS.WTSDomainName, out nameBuffer, out nameLen))
					{
						userName = Marshal.PtrToStringAnsi(nameBuffer) + "\\" + userName;
						WTSFreeMemory(nameBuffer);
					}
				}
			}

			return userName;
		}

		private int GetSessionCount()
		{ // get the number of user sessions, ignore the listening session 65536 and service session 0
			IntPtr pSessionInfo = IntPtr.Zero;
			int sessionCount = 0, usersessionCount = 0;

			if (WTSEnumerateSessions(IntPtr.Zero, 0, 1, ref pSessionInfo, ref sessionCount) != 0) // IntPtr.Zero = WTS_CURRENT_SERVER_HANDLE
			{ // get information array of logon sessions
				IntPtr currentSessionInfo = pSessionInfo;
				int dataSize = Marshal.SizeOf(typeof(WTS_SESSION_INFO));

				for (int i = 0; i < sessionCount; i++)
				{	// enumerate sessions (walk through array)
					WTS_SESSION_INFO si = (WTS_SESSION_INFO)Marshal.PtrToStructure(currentSessionInfo, typeof(WTS_SESSION_INFO));
					currentSessionInfo += dataSize;

					// get user name of session user
					string userName = GetUserFromSession(si.SessionID);

					// print session information only in verbose mode
					if (i == 0) WriteVerboseToLog("Session list:");
					WriteVerboseToLog("Session ID " + si.SessionID.ToString() + " ("+ si.pWinStationName + ") with user " + userName + ", state " + si.State.ToString());

					// check if session has an "active" state and someone is logged on
					if (((si.State == WTS_CONNECTSTATE_CLASS.WTSActive) || (si.State == WTS_CONNECTSTATE_CLASS.WTSConnected) || (si.State == WTS_CONNECTSTATE_CLASS.WTSDisconnected)) && (userName != "SYSTEM"))
						usersessionCount++;
				}
				// free memory
				WTSFreeMemory(pSessionInfo);
			}

			return usersessionCount; // return user session count
		}
		#endregion
	}
}
