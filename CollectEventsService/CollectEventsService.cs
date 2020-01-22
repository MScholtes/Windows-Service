// C# event collecting service
// reads events of all (!) event logs and write them to one file
// service can be configured via ServiceConfig.xml in the directory of the binary
// Markus Scholtes, 2020/01/02

using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Timers;
using System.Xml;

// set executable properties
using System.Reflection;
[assembly:AssemblyTitle("CollectEventsService")]
[assembly:AssemblyDescription("Event collecting service")]
[assembly:AssemblyConfiguration("")]
[assembly:AssemblyCompany("MS")]
[assembly:AssemblyProduct("CollectEventsService")]
[assembly:AssemblyCopyright("© Markus Scholtes 2020")]
[assembly:AssemblyTrademark("")]
[assembly:AssemblyCulture("")]
[assembly:AssemblyVersion("1.0.0.0")]
[assembly:AssemblyFileVersion("1.0.0.0")]

namespace NaSpCollectEventsService
{
	public partial class CollectEventsService : ServiceBase
	{
		static int Main(string[] arguments)
		{ // entry point
			// declaration as int, transfer of parameters and return of a value are ignored when called via the service manager
			CollectEventsService service = new CollectEventsService();
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
		double timerinterval = 300000; // milliseconds
		DateTime lastcheck; // time of the last check
		string filename = ""; // path for output file
		string computers = ""; // which computers to query, empty for localhost or comma separated list of computer names. localhost for local machine
													// service account must have access right to event log on remote machine
		string eventlogs = ""; // eventlogs: which event logs to query, empty for all event logs or comma separated list of log names
		int eventlevel = 0; // event information level: all (default) - 0, up to critical - 1, up to error - 2, up to warning- 3, up to informational - 4, up to verbose - 5
		bool CSV = false; // format of output file, txt (default) or csv
		int rotationtype = 0; // rotation of output file: 0 - never (default), 1 - when rotationsize is reached, 2 - every hour, 3 - every day, 4 - every month
		int rotationsize = 0; // size when file is to rotate in KB
		int rotationcount = 0; // maximal number of output files to keep or 0 for don't check (default)

		#region service structure
		// timer object for action pulse
		Timer timer = new Timer();

		public CollectEventsService()
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
			// define first start timestamp for event query
			lastcheck = DateTime.Now;
			// prepare and start timer
			timer.Elapsed += new ElapsedEventHandler(TimeTick);
			timer.Interval = timerinterval; // milliseconds
			timer.AutoReset = true;
			timer.Enabled = true;

			// check command line parameter of service, e.g. "sc start CollectEventsService VERBOSE"
			if ((args.Length > 0) && (args[0].ToUpper() == "VERBOSE"))
			{ // verbose mode per command line parameter
				forceverbose = true;
				WriteToLog("Verbose mode forced per command line parameter.");
			}

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
			timer.Enabled = true; //reenable timer
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

		#region service action
		private void TimeTick(object source, ElapsedEventArgs e)
		{ // worker function that is initially called manually and then by timer event

			// configuration is read and set on every pulse (where possible)
			ReadConfiguration();
			if (timer.Interval != timerinterval)
				timer.Interval = timerinterval; // set time interval in milliseconds (only if changed to avoid resetting)

			// call action if no fatal error
			if (ExitCode == 0)
			{ // call action
				WriteVerboseToLog("Calling service action.");
				DateTime newcheck = DateTime.Now;
				WriteVerboseToLog("Querying event logs from " + lastcheck.ToString() + " to " + newcheck.ToString() + ".");

				List<string> computerNames = null;
				// query local computer or as comma separated list in computers given computer names
				if (computers != "")
				{ // yes, connect to computers in list
					computerNames = new List<string>(computers.Split(new char[] {',',';'}).Where(val => val.Trim() != "").Select(val => val.Trim()).ToArray());
				}
				else
				{ // no, query only local computer
					computerNames = new List<string>();
					computerNames.Add(Environment.MachineName);
				}

				foreach (string computer in computerNames)
				{ // query entries for all logs
					bool successful = true;

					EventLogSession session;
					// connect to computer ("localhost" for local computer). On local computer prevent network access
					if ((computer.ToUpper() == Environment.MachineName) || (computer.ToLower() == "localhost"))
					{ 
						WriteVerboseToLog("Starting a session with localhost");
						session = new EventLogSession();
					}
					else
					{ 
						WriteVerboseToLog("Starting a session with computer " + computer);
						session = new EventLogSession(computer);
					}

					List<string> logNames = null;
					// query all or as comma separated list in eventlogs given logs
					if (eventlogs != "")
					{ // yes, only read those logs
						logNames = new List<string>(eventlogs.Split(new char[] {',',';'}).Where(val => val.Trim() != "").Select(val => val.Trim()).ToArray());
					}
					else
					{ // no entries for logs, read all logs
						try { // retrieve all log names
							logNames = new List<string>(session.GetLogNames());
						}
						catch (Exception ex)
						{ // cannot retrieve log names
							WriteToLog("Error connecting to computer " + computer + " to retrieve event log names: " + ex.Message);
							successful = false;
						}
					}

					if (successful)
					{
						// sort log names
						logNames.Sort();

						int logCount = 0;
						List<EventEntry> eventList = new List<EventEntry>();
						foreach (string name in logNames)
						{ // query entries for all logs
							if (GetEntries(ref eventList, name, computer, session, lastcheck, newcheck)) logCount++;
						}

						if (eventList.Count > 0)
						{ // only write events if there are any
							string filetowrite = filename;

							if (filetowrite == "")
							{	// default output file name
								if (CSV)
									filetowrite = Environment.ExpandEnvironmentVariables("%WINDIR%\\Logs\\CollectedEvents.csv");
								else
									filetowrite = Environment.ExpandEnvironmentVariables("%WINDIR%\\Logs\\CollectedEvents.txt");
							}

							string pathWithoutExtension = filetowrite.LastIndexOf('.') == -1 ? filetowrite : filetowrite.Substring(0, filetowrite.LastIndexOf('.'));
							string extension = Path.GetExtension(filetowrite);
							string openFilename = "";
							Int64 currentFilesize = 0;
							FileStream fs = null;
							StreamWriter sw = null;

							foreach (EventEntry entry in eventList.OrderBy(a => a.timeCreated))
							{ // output events sorted by time
								string textToWrite = entry.message;

								// create file name
								string currentFilename = filetowrite;
								switch (rotationtype)
								{ // use date stamp for name for rotation 2 and 3
									case 2:
										currentFilename = pathWithoutExtension + entry.timeCreated.ToString("yyyyMMddHH") + extension;
										break;
									case 3:
										currentFilename = pathWithoutExtension + entry.timeCreated.ToString("yyyyMMdd") + extension;
										break;
									case 4:
										currentFilename = pathWithoutExtension + entry.timeCreated.ToString("yyyyMM") + extension;
										break;
								}

								try
								{
									if ((currentFilename != openFilename) && !File.Exists(currentFilename))
									{
										// generate new file with headers
										if (sw != null) sw.Dispose();
										if (fs != null) fs.Dispose();

										fs = new FileStream(currentFilename, FileMode.Append);
										sw = new StreamWriter(fs, Encoding.Default);
										openFilename = currentFilename;
										WriteVerboseToLog("Creating file " + currentFilename);

										if (CSV)
										{ // generate headers
											if (computers == "")
												currentFilesize = WriteToFile(sw, "\"time created\";\"log\";\"id\";\"source\";\"level\";\"description\"");
											else
												currentFilesize = WriteToFile(sw, "\"time created\";\"computer\";\"log\";\"id\";\"source\";\"level\";\"description\"");
										}
										else
										{
											if (computers == "")
												currentFilesize = WriteToFile(sw, "time created\tlog\tid\tsource\tlevel\tdescription");
											else
												currentFilesize = WriteToFile(sw, "time created\tcomputer\tlog\tid\tsource\tlevel\tdescription");
										}

										if (rotationtype > 1)
										{ // delete old files according to rotationcount only for date name format
											switch (rotationtype)
											{
												case 2: // hourly -> supply RegEx-Mask for hourly timestamp
													DeleteFiles(pathWithoutExtension, extension, "[0-9]{10}", rotationcount);
													break;
												case 3: // daily -> supply RegEx-Mask for daily timestamp
													DeleteFiles(pathWithoutExtension, extension, "[0-9]{8}", rotationcount);
													break;
												case 4: // monthly -> supply RegEx-Mask for monthly timestamp
													DeleteFiles(pathWithoutExtension, extension, "[0-9]{6}", rotationcount);
													break;
												default: // unknown file rotation type
													WriteToLog("Unknown rotation type " + rotationtype.ToString() + ".");
													break;
											}
										}
									}
									else
									{
										if (sw == null)
										{
											fs = new FileStream(currentFilename, FileMode.Append);
											sw = new StreamWriter(fs, Encoding.Default);
											openFilename = currentFilename;
										}

										if (rotationtype == 1)
										{ // rotationtype 1 - numbering of output files

											if (currentFilesize == 0)
											{ // retrieve file size
												currentFilesize = (new FileInfo(currentFilename)).Length;
											}

											if (currentFilesize > (long)1024*rotationsize)
											{
												// create new file with header
												if (sw != null) sw.Dispose();
												if (fs != null) fs.Dispose();

												// rotate and delete old files according to rotationcount
												RotateFiles(pathWithoutExtension, extension, rotationcount);

												fs = new FileStream(currentFilename, FileMode.Append);
												sw = new StreamWriter(fs, Encoding.Default);
												WriteVerboseToLog("Creating file " + currentFilename);

												if (CSV)
												{
													if (computers == "")
														currentFilesize = WriteToFile(sw, "\"time created\";\"log\";\"id\";\"source\";\"level\";\"description\"");
													else
														currentFilesize = WriteToFile(sw, "\"time created\";\"computer\";\"log\";\"id\";\"source\";\"level\";\"description\"");
												}
												else
												{
													if (computers == "")
														currentFilesize = WriteToFile(sw, "time created\tlog\tid\tsource\tlevel\tdescription");
													else
														currentFilesize = WriteToFile(sw, "time created\tcomputer\tlog\tid\tsource\tlevel\tdescription");
												}
											}
										}
									}

									// write text to file
									currentFilesize += WriteToFile(sw, textToWrite);
								}
								catch (Exception ex)
								{
									WriteToLog("Error writing to file " + currentFilename + ": " + ex.Message);
									successful = false;
									break;
								}
							}

							// cleanup
							if (sw != null) sw.Dispose();
							if (fs != null) fs.Dispose();
						}

						if (successful) WriteVerboseToLog("Successfully processed " + eventList.Count + " events from " + logCount + " logs, access errors with " + (int)(logNames.Count - logCount) + " logs.");
					}

				}
				lastcheck = newcheck;
			}

			// on error set ExitCode here to stop service and generate error message in system log
			// (example: 5 returns "Access Denied"):
			// if (ErrorHasHappened) ExitCode = 5;
			if (ExitCode != 0) Stop();
		}

		private int WriteToFile(StreamWriter stream, string textToWrite)
		{ // write to file and return number of bytes written
			stream.WriteLine(textToWrite);
			return textToWrite.Length + 2;
		}

		private void DeleteFiles(string fileTrunk, string fileExtension, string fileMask, int numberToKeep)
		{ // delete files according to maximum file number
			// Windows searches files with short names too when providing a file mask. E.g. searching for
			// "file??????.log" may return a file name "file2019123101.log" that has a short name "file20~1.log".
			// To circumvent this we first search with a wildcard '*' and then filter with the regular expression that
			// is given in fileMask

			if (numberToKeep <= 0) return; // nothing to delete

			// separate directory and filename for search
			string directory;
			try {
				directory = Path.GetDirectoryName(fileTrunk);
				if (string.IsNullOrEmpty(directory)) directory = ".";
			}
			catch
			{
				directory = ".";
			}
			string fileName;
			try {
				fileName = Path.GetFileName(fileTrunk);
			}
			catch
			{
				fileName = "*";
			}

			// get all files fitting to woldcard
			try {
				string[] fileList = Directory.GetFiles(directory, fileName + "*" + fileExtension);
				Array.Sort(fileList); // sort (sorting by GetFiles() alone is not guaranteed)

				int numberFound = 0;
				foreach (string item in fileList)
				{ // now count the file names that fit to regular expression fileMask
					if (Regex.IsMatch(item, fileName + fileMask + fileExtension, RegexOptions.IgnoreCase)) numberFound++;
				}

				if (numberFound > numberToKeep) // are there files to delete?
				{
					int counter = 0, deleted = 0;
					do { // just delete the needed files that fit to regular expression fileMask
						if (Regex.IsMatch(fileList[counter], fileName + fileMask + fileExtension, RegexOptions.IgnoreCase))
						{ // delete supernumerary files
							Console.WriteLine("Deleting file " + fileList[counter]);
							File.Delete(fileList[counter]);
							deleted++;
						}
						counter++;
					} while (numberFound - deleted > numberToKeep);

				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error deleting unnecessary files '" + fileName + "*" + fileExtension + "': " + ex.Message);
			}
		}

		private void RotateFiles(string fileTrunk, string fileExtension, int numberToKeep)
		{ // rotate files from <NAME>.<EXT> to <NAME><n>.<EXT> and delete the supernumerary (more than numberToKeep)
			int start = 1;

			// get latest file
			while (File.Exists(fileTrunk + "-" + start.ToString() + fileExtension)) start++;

			for (int back = start - 1; back > 0; back--)
			{ // iterate from back to front so renaming can succeed
				if ((numberToKeep <= 0) || (back < numberToKeep - 1))
				{ // file is to keep, rename it
					WriteToLog("Renaming " + fileTrunk + "-" + back.ToString() + fileExtension + " to " + fileTrunk + "-" + (back+1).ToString() + fileExtension + ".");
					try
					{
						File.Move(fileTrunk + "-" + back.ToString() + fileExtension, fileTrunk + "-" + (back+1).ToString() + fileExtension);
					}
					catch (Exception ex)
					{
						WriteToLog("Error renaming file: " + ex.Message);
					}
				}
				else
				{ // file is supernumerary, delete it
					WriteToLog("Deleting " + fileTrunk + "-" + back.ToString() + fileExtension + ".");
					try
					{
						File.Delete(fileTrunk + "-" + back.ToString() + fileExtension);
					}
					catch (Exception ex)
					{
						WriteToLog("Error deleting file: " + ex.Message);
					}
				}
			}

			if (File.Exists(fileTrunk + fileExtension))
			{ // rename or delete the original file (the file without a number in its name)
				if (numberToKeep != 1)
				{ // file is to keep, rename it
					WriteToLog("Renaming " + fileTrunk + fileExtension + " to " + fileTrunk + "-1" + fileExtension + ".");
					try {
						File.Move(fileTrunk + fileExtension, fileTrunk + "-1" + fileExtension);
					}
					catch (Exception ex) {
						WriteToLog("Error renaming file: " + ex.Message);
					}
				}
				else
				{ // file is supernumerary, delete it
					WriteToLog("Deleting " + fileTrunk + fileExtension + ".");
					try {
						File.Delete(fileTrunk + fileExtension);
					}
					catch (Exception ex) {
						WriteToLog("Error deleting file: " + ex.Message);
					}
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

				// timer value in milliseconds
				tempStr = ReadXmlText(xmlDocument, "/serviceconfig/service/timer", "300000");
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
				tempStr = ReadXmlText(xmlDocument, "/serviceconfig/service/timer", "300000");
				if (Int32.TryParse(tempStr, out tempInt))
				{
					if ((tempInt > 0) && (tempInt <= Int32.MaxValue))
					{
						if (timerinterval != tempInt)
						{
							timerinterval = tempInt;
							WriteToLog("Timer interval set to " + timerinterval.ToString() + " ms.");
						}
					}
					else
						WriteToLog("Invalid value for time interval.");
				}
				else
					WriteToLog("Error when setting the time interval.");

				// filename: path and name to target file, empty: %WINDIR%\Logs\CollectedEvents.txt or %WINDIR%\Logs\CollectedEvents.csv (depending on format)
				tempStr = Environment.ExpandEnvironmentVariables(ReadXmlText(xmlDocument, "/serviceconfig/event/filename", ""));
				if (filename != tempStr)
				{
					filename = tempStr;
					WriteToLog("Output file set to " + filename + ".");
				}

				// computers: which computers to query, empty for localhost or comma separated list of computer names. localhost for local machine
				tempStr = ReadXmlText(xmlDocument, "/serviceconfig/event/computers", "").Trim();
				if (computers != tempStr)
				{
					computers = tempStr;
					WriteToLog("Computer list set to " + computers + ".");
				}

				// eventlogs: which event logs to query, empty for all event logs or comma separated list of log names
				tempStr = ReadXmlText(xmlDocument, "/serviceconfig/event/eventlogs", "").Trim();
				if (eventlogs != tempStr)
				{
					eventlogs = tempStr;
					WriteToLog("Event logs list set to " + eventlogs + ".");
				}

				// eventlevel: all (default) - 0, up to critical - 1, up to error - 2, up to warning- 3, up to informational - 4, up to verbose - 5
				tempStr = ReadXmlText(xmlDocument, "/serviceconfig/event/eventlevel", "0");
				if (Int32.TryParse(tempStr, out tempInt))
				{
					if ((tempInt >= 0) && (tempInt <= 5))
					{
						if (eventlevel != tempInt)
						{
							eventlevel = tempInt;
							WriteToLog("Event information level set to " + eventlevel.ToString() + ".");
						}
					}
					else
						WriteToLog("Invalid value for information level.");
				}
				else
					WriteToLog("Error when setting the information level.");

				// format of output file, txt (default) or csv
				if (ReadXmlText(xmlDocument, "/serviceconfig/event/format", "txt").ToLower() == "csv")
				{
					if (!CSV)
					{
						WriteToLog("Format of output file set to CSV.");
						CSV = true;
					}
				}
				else
				{
					if (CSV)
					{
						WriteToLog("Format of output file set to TXT.");
						CSV = false;
					}
				}

				// filerotation: when to create a new file: none - never (default), hourly - every hour, daily - every day, (size in integer) - when (size in integer) in KB is reached
				tempStr = ReadXmlText(xmlDocument, "/serviceconfig/event/filerotation", "none").ToLower();
				switch(tempStr)
				{
					case "none": if (rotationtype != 0)
										{
											WriteToLog("File rotation deactivated.");
											rotationtype = 0;
										}
										break;
					case "hourly": if (rotationtype != 2)
										{
											rotationtype = 2;
											WriteToLog("A new output file is generated every hour.");
										}
										break;
					case "daily": if (rotationtype != 3)
										{
											rotationtype = 3;
											WriteToLog("A new output file is generated every day.");
										}
										break;
					case "monthly": if (rotationtype != 4)
										{
											rotationtype = 4;
											WriteToLog("A new output file is generated every month.");
										}
										break;
					default:  if (Int32.TryParse(tempStr, out tempInt))
										{
											if (tempInt > 0)
											{
												if ((rotationtype != 1) || (tempInt != rotationsize))
												{
													rotationtype = 1;
													rotationsize = tempInt;
													WriteToLog("A new output file is generated when file reaces size of " + rotationsize.ToString() + " KB.");
												}
											}
											else
												WriteToLog("Invalid value for file rotation.");
										}
										else
											WriteToLog("Error when setting the file rotation type.");
										break;
				}

				// filecount: maximal number of output files to keep or 0 for don't check (default)
				tempStr = ReadXmlText(xmlDocument, "/serviceconfig/event/filecount", "0");
				if (Int32.TryParse(tempStr, out tempInt))
				{
					if (tempInt >= 0)
					{
						if (tempInt != rotationcount)
						{
							rotationcount = tempInt;
							if (rotationcount == 0)
								WriteToLog("File rotation deactivated.");
							else
								WriteToLog("File rotation set to " + rotationcount.ToString() + " files.");
						}
					}
					else
						WriteToLog("Invalid value for file rotation count.");
				}
				else
					WriteToLog("Error when setting the file rotation count.");
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

		#region reading event log
		public class EventEntry
		{
			public DateTime timeCreated { get; set; }
			public string message { get; set; }

			public EventEntry(DateTime timeStamp, string messageText)
			{
					timeCreated = timeStamp;
					message = messageText;
			}
		}

		private bool GetEntries(ref List<EventEntry> eventList, string logName, string computerName, EventLogSession session, DateTime startTime, DateTime endTime)
		{
			string eventQuery;
			if (eventlevel == 0)
				// query for all information levels
				eventQuery = string.Format("*[System/TimeCreated/@SystemTime > '{0}'] and *[System/TimeCreated/@SystemTime <= '{1}']", startTime.ToUniversalTime().ToString("o"), endTime.ToUniversalTime().ToString("o"));
			else
				// level: LogAlways - 0, Critical - 1, Error - 2, Warning - 3, Informational - 4, Verbose - 5
				// there are different levels for auditing logs!
				eventQuery = string.Format("*[System/TimeCreated/@SystemTime > '{0}'] and *[System/TimeCreated/@SystemTime <= '{1}'] and *[System/Level <= {2}]", startTime.ToUniversalTime().ToString("o"), endTime.ToUniversalTime().ToString("o"), eventlevel.ToString());

			// define event log query
			EventLogQuery eventLogQuery = new EventLogQuery(logName, PathType.LogName, eventQuery);
			eventLogQuery.Session = session;

			try
			{ // start query
				EventLogReader eventLogReader = new EventLogReader(eventLogQuery);

				int count = 0;
				for (EventRecord eventRecord = eventLogReader.ReadEvent(); eventRecord != null; eventRecord = eventLogReader.ReadEvent())
				{ // enumerate all found events
					count++;
					// read Event details
					DateTime timeCreated = DateTime.Now;
					if (eventRecord.TimeCreated.HasValue) timeCreated = eventRecord.TimeCreated.Value;

					string eventLevel;
					try {
						eventLevel = eventRecord.LevelDisplayName;
						if (eventRecord.Level == 0) eventLevel = "LogAlways";
					}
					catch {
						eventLevel = "Unknown";
					}

					string eventLine;
					if (CSV)
					{
						if (computers == "")
							eventLine = "\"" + timeCreated.ToString() + "\";\"" + logName + "\";" + eventRecord.Id.ToString() + ";\"" + eventRecord.ProviderName + "\";\"" + eventLevel + "\";\"";
						else
							eventLine = "\"" + timeCreated.ToString() + "\";\"" + computerName + "\";\"" + logName + "\";" + eventRecord.Id.ToString() + ";\"" + eventRecord.ProviderName + "\";\"" + eventLevel + "\";\"";
					}
					else
					{
						if (computers == "")
							eventLine = timeCreated.ToString() + "\t" + logName + "\t" + eventRecord.Id.ToString() + "\t" + eventRecord.ProviderName + "\t" + eventLevel + "\t";
						else
							eventLine = timeCreated.ToString() + "\t" + computerName + "\t" + logName + "\t" + eventRecord.Id.ToString() + "\t" + eventRecord.ProviderName + "\t" + eventLevel + "\t";
					}

					try
					{
						if (!String.IsNullOrEmpty(eventRecord.FormatDescription()))
						{
							if (CSV)
								eventList.Add(new EventEntry(timeCreated, eventLine + eventRecord.FormatDescription().Replace("\"", "\"\"") + "\""));
							else
								eventList.Add(new EventEntry(timeCreated, eventLine + eventRecord.FormatDescription().Replace("\n", "\n\t")));
						}
						else
						{ // description not available, try to interpret raw data
							string rawDescription = "";
							foreach (EventProperty eventProperty in eventRecord.Properties)
							{
								rawDescription += eventProperty.Value.ToString();
							}
							if (CSV)
								eventList.Add(new EventEntry(timeCreated, eventLine + rawDescription.Replace("\"", "\"\"") + "\""));
							else
								eventList.Add(new EventEntry(timeCreated, eventLine + rawDescription.Replace("\n", "\n\t")));
						}
					}
					catch (Exception e)
					{
						if (CSV)
							eventList.Add(new EventEntry(timeCreated, eventLine + "### Error reading the event log entry: " + e.Message.Replace("\"", "\"\"") + "\""));
						else
							eventList.Add(new EventEntry(timeCreated, eventLine + "### Error reading the event log entry: " + e.Message.Replace("\n", "\n\t")));
					}
				}

				WriteVerboseToLog("Processed event log \"" + logName + "\" from computer " + computerName + ": " + count + " entries");
				return true;
			}
			catch (Exception e)
			{
				WriteToLog("Error opening the event log \"" + logName + "\" from computer " + computerName + ": " + e.Message);
				return false;
			}
		}
		#endregion
	}
}
