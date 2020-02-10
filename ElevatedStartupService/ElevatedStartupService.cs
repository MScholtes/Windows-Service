// C# service that starts every program or link elevated in folder 
// C:\Users\<USERNAME>\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\StartupElevated when a user logs on and folder exists.
// service can be configured via ServiceConfig.xml in the directory of the binary
// Markus Scholtes, 2020/02/10

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Xml;

// set executable properties
using System.Reflection;
[assembly:AssemblyTitle("ElevatedStartupService")]
[assembly:AssemblyDescription("Service that starts up programs elevated on logon")]
[assembly:AssemblyConfiguration("")]
[assembly:AssemblyCompany("MS")]
[assembly:AssemblyProduct("ElevatedStartupService")]
[assembly:AssemblyCopyright("© Markus Scholtes 2020")]
[assembly:AssemblyTrademark("")]
[assembly:AssemblyCulture("")]
[assembly:AssemblyVersion("1.0.0.0")]
[assembly:AssemblyFileVersion("1.0.0.0")]

namespace NaSpElevatedStartupService
{
	public partial class ElevatedStartupService : ServiceBase
	{
		static int Main(string[] arguments)
		{ // entry point
			// declaration as int, transfer of parameters and return of a value are ignored when called via the service manager
			ElevatedStartupService service = new ElevatedStartupService();
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

		#region service structure

		public ElevatedStartupService()
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

			// check command line parameter of service, e.g. "sc start ElevatedStartupService VERBOSE"
			if ((args.Length > 0) && (args[0].ToUpper() == "VERBOSE"))
			{ // verbose mode per command line parameter
				forceverbose = true;
				WriteToLog("Verbose mode forced per command line parameter.");
			}

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

			// clear pausing flag
			servicepaused = false;
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

			if (changeDescription.Reason == SessionChangeReason.SessionLogon)
			{ // action only for logon
				string userName = GetUserFromSession((uint)changeDescription.SessionId);
				
				// get user profile path in registry
				string directoryName = GetUserProfilePath((uint)changeDescription.SessionId);
				if (directoryName != "")
				{ // profile found, append path to start menu
					directoryName += "\\AppData\\Roaming\\Microsoft\\Windows\\Start Menu\\Programs\\StartupElevated";
				}
				else
				{ // not found, try standard path
					directoryName = "C:\\Users\\" + userName + "\\AppData\\Roaming\\Microsoft\\Windows\\Start Menu\\Programs\\StartupElevated";
					WriteVerboseToLog("Could not retrieve user profile from operating system, trying '" + directoryName + "'");
				}
				WriteToLog("Session logon by user " + userName + " to session id " + changeDescription.SessionId.ToString() + ".");
				if (Directory.Exists(directoryName))
				{
					WriteVerboseToLog("Directory '"+ directoryName + "' found.");
					string[] fileList = Directory.GetFiles(directoryName);
					if (fileList.Length > 0)
					{
						foreach (string command in fileList)
						{ // compute list of programs to start
							if (command.ToLower().EndsWith("\\desktop.ini"))
							{ // do not start desktop.ini!
								WriteVerboseToLog("Ignoring '"+ command + "'.");
							}
							else
							{
								WriteVerboseToLog("Starting '"+ command + "'.");
								StartProcessInSession((uint)changeDescription.SessionId, command);
							}
						}

					}
					else
					{
						WriteVerboseToLog("Directory '"+ directoryName + "' is empty.");
					}
				}
				else
				{
					WriteVerboseToLog("Directory '"+ directoryName + "' not found.");
				}
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

		[DllImport("wtsapi32.dll", SetLastError = true)]
		private static extern uint WTSQueryUserToken(uint sessionId, ref IntPtr hToken);

		[DllImport("wtsapi32.dll")]
		static extern void WTSFreeMemory(IntPtr pMemory);

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
		
		[DllImport("userenv.dll", SetLastError=true, CharSet=CharSet.Auto)]
		static extern bool GetUserProfileDirectory(IntPtr hToken, StringBuilder path, ref int dwSize);

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

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool CloseHandle(IntPtr hHandle);
		#endregion

		#region session helper functions
		private static string GetUserFromSession(uint sessionId, bool withDomain = false)
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
					if (withDomain)
					{
						if (WTSQuerySessionInformation(IntPtr.Zero, sessionId, WTS_INFO_CLASS.WTSDomainName, out nameBuffer, out nameLen))
						{
							userName = Marshal.PtrToStringAnsi(nameBuffer) + "\\" + userName;
							WTSFreeMemory(nameBuffer);
						}
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
					string userName = GetUserFromSession(si.SessionID, true);

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

    // get path to user profile for user name or SID
    // need at least SE_TCB_NAME rights (that per default only LocalSystem has)
    private string GetUserProfilePath(uint sessionId)
    {
			// get token of session user
			IntPtr hSessionToken = GetTokenFromSession(sessionId);
			if (hSessionToken == IntPtr.Zero)
			{ // could not retrieve session token
				WriteVerboseToLog("Cannot retrieve token of session " + sessionId.ToString() + ". Error: " + Marshal.GetLastWin32Error().ToString());
				return "";
			}
    	
    	// get size of profile string
    	int dwSize = 0;
    	GetUserProfileDirectory(hSessionToken, null, ref dwSize);
    	
    	// get profile path of user
    	StringBuilder profilePath = new StringBuilder(dwSize);
    	if (!GetUserProfileDirectory(hSessionToken, profilePath, ref dwSize))
			{ // could not retrieve profile directory
				WriteVerboseToLog("Cannot retrieve profile path for user in session " + sessionId.ToString() + ". Error: " + Marshal.GetLastWin32Error().ToString());
				return "";
    	}
			
			return profilePath.ToString();    	
    }
		#endregion

		#region process helper functions
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

		private void StartProcessInSession(uint targetSessionId, string processName)
		{
			WriteVerboseToLog("Starting process " + processName + " in session " + targetSessionId.ToString() + ".");

			if ((targetSessionId == 0) || (targetSessionId == 65536))
			{ // in service session 0 or listening session 65536 there is no user logon
				WriteVerboseToLog("No user session, cancelling call.");
				return;
			}

			// start process as session user
			IntPtr hProcessToken = GetTokenFromSession(targetSessionId);
			if (hProcessToken == IntPtr.Zero)
			{ // could not retrieve session token
				WriteVerboseToLog("Cannot retrieve token of target session " + targetSessionId.ToString() + ". Error: " + Marshal.GetLastWin32Error().ToString());
				return;
			}
			
			// get elevated token
			GetElevatedToken(ref hProcessToken, true);

			IntPtr pEnvironment = IntPtr.Zero;
			if (!CreateEnvironmentBlock(out pEnvironment, hProcessToken, false))
			{ // could not create environment block
				pEnvironment = IntPtr.Zero;
				WriteVerboseToLog("Cannot create environment block for target session, continuing without.");
			}

			uint dwCreationFlags = 0x00000410; // CREATE_NEW_CONSOLE + CREATE_UNICODE_ENVIRONMENT
			PROCESS_INFORMATION pi = new PROCESS_INFORMATION();
			STARTUPINFO si = new STARTUPINFO();
			// Initialize struct
			si.cb = Marshal.SizeOf(si);
			si.dwFlags = 0x00000001; // STARTF_USESHOWWINDOW
			si.wShowWindow = 5; // SW_SHOW
			si.lpDesktop = "winsta0\\default";

			int rc = 0;
			if (!CreateProcessAsUser(hProcessToken, null, processName, IntPtr.Zero, IntPtr.Zero, false, dwCreationFlags, pEnvironment, null, ref si, out pi))
			{ // could not start process
				if (Marshal.GetLastWin32Error() == 193)
				{ // error 193: "%1 is not a valid Win32 application", then start with shellexecute
					// after start the first expression in double quotes is taken as windows title, so double the expression
					if (!CreateProcessAsUser(hProcessToken, "cmd.exe", "/c start \"" + processName + "\" \"" + processName + "\"", IntPtr.Zero, IntPtr.Zero, false, dwCreationFlags, pEnvironment, null, ref si, out pi))
					{ // still error
						rc = 1;
						WriteVerboseToLog("Cannot start process " + processName + ". Error: " + Marshal.GetLastWin32Error().ToString());
					}
					else
					{ // process started
						WriteVerboseToLog("File " + processName + " started with shellexecute in session " + targetSessionId.ToString() + ".");
					}
				}
				else
				{ // other error
					rc = 1;
					WriteVerboseToLog("Cannot start process " + processName + ". Error: " + Marshal.GetLastWin32Error().ToString());
				}
			}
			else
			{ // process started
				WriteVerboseToLog("Process " + processName + " started in session " + targetSessionId.ToString() + ".");
			}

			if (pEnvironment != IntPtr.Zero)
			{ // free memory of environment block (process has a copy)
				DestroyEnvironmentBlock(pEnvironment);
			}

			if (rc == 0)
			{ // close handles that only exist when no error occurred
				CloseHandle(pi.hThread);
				CloseHandle(pi.hProcess);
			}
			// close remaining handle
			CloseHandle(hProcessToken);
		}
		#endregion
	}
}
