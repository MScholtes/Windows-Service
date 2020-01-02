// C# demo of a service
// writes a log entry every 5 seconds
// service can be configured via ServiceConfig.xml in the directory of the binary
// Markus Scholtes, 2020/01/02

using System;
using System.IO;
using System.ServiceProcess;
using System.Text;
using System.Timers;
using System.Xml;

// set executable properties
using System.Reflection;
[assembly:AssemblyTitle("DemoService")]
[assembly:AssemblyDescription("C# demo service that writes a log entry every 5 seconds")]
[assembly:AssemblyConfiguration("")]
[assembly:AssemblyCompany("MS")]
[assembly:AssemblyProduct("DemoService")]
[assembly:AssemblyCopyright("© Markus Scholtes 2019")]
[assembly:AssemblyTrademark("")]
[assembly:AssemblyCulture("")]
[assembly:AssemblyVersion("1.0.0.0")]
[assembly:AssemblyFileVersion("1.0.0.0")]

namespace NaSpDemoService
{
	public partial class DemoService : ServiceBase
	{
		static int Main(string[] arguments)
		{ // entry point
			// declaration as int, transfer of parameters and return of a value are ignored when called via the service manager
			DemoService service = new DemoService();
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
		double timerinterval = 5000; // milliseconds
		int pulsetype = 1; // 0 - no action, 1 - start action every pulse ...
		bool oncerun; // marker for "start action only one time"

		#region service structure
		// timer object for action pulse
		Timer timer = new Timer();

		public DemoService()
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
			timer.Elapsed += new ElapsedEventHandler(TimeTick);
			timer.Interval = timerinterval; // milliseconds
			timer.AutoReset = true;
			timer.Enabled = true;

			// check command line parameter of service, e.g. "sc start DemoService VERBOSE"
			if ((args.Length > 0) && (args[0].ToUpper() == "VERBOSE"))
			{ // verbose mode per command line parameter
				forceverbose = true;
				WriteToLog("Verbose mode forced per command line parameter.");
			}

			TimeTick(this, null); // manually start action synchronous - if it takes too long (> 30 sec.), service manager will fail the service

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

		protected override void OnContinue()
		{ // called from service manager on service continuation

			// request additional time from service manager to wait in milliseconds
			// RequestAdditionalTime(2000);
			WriteToLog("Service " + ServiceName + " continued.");
			if (pulsetype == 9) oncerun = false; // start once
			timer.Enabled = true; //reenable timer
			TimeTick(this, null); // manually start action synchronous
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
			if ((ExitCode == 0) && ((pulsetype == 1) || ((pulsetype >= 8) && !oncerun)))
			{ // call action
				WriteToLog("Called service action.");
				if (pulsetype >= 8) oncerun = true; // notice action call for "one time modes"
			}

			// on error set ExitCode here to stop service and generate error message in system log
			// (example: 5 returns "Access Denied"):
			// if (ErrorHasHappened) ExitCode = 5;
			if (ExitCode != 0) Stop();
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
				tempStr = ReadXmlText(xmlDocument, "/serviceconfig/service/timer", "5000");
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
				tempStr = ReadXmlText(xmlDocument, "/serviceconfig/service/timer", "5000");
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
				switch(ReadXmlText(xmlDocument, "/serviceconfig/service/pulsetype", "1"))
				{
					case "0": if (pulsetype != 0)
										{
											pulsetype = 0;
											WriteToLog("Deactivated timer action.");
										}
										break;
					case "1": if (pulsetype != 1)
										{
											pulsetype = 1;
											WriteToLog("Regular timer action activated.");
										}
										break;
					case "8": if (pulsetype != 8)
										{
											oncerun = false;
											pulsetype = 8;
											WriteToLog("One-time timer action activated.");
										}
										break;
					case "9": if (pulsetype != 9)
										{
											oncerun = false;
											pulsetype = 9;
											WriteToLog("One-time timer action now and on service continuation activated.");
										}
										break;
					default:  WriteToLog("Invalid value for pulse type.");
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
	}
}
