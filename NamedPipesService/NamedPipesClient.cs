// Example Named Pipes Client
// derived from https://github.com/webcoyote/CSNamedPipes
// Markus Scholtes, 2020/01/02

// NamedPipesClient <NameOfPipe> <Servername> <CountOfRequests>
// default: NamedPipesClient NamedPipesService . 10

using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Diagnostics;


public class NamedPipesClient
{
	private const Int32 SERVER_IN_BUFFER_SIZE = 65536;
	private const Int32 SERVER_OUT_BUFFER_SIZE = 65536;
	
	static string pipeName = "NamedPipesService";
	static string server = ".";
	static int count = 10;
	static Int32 instanceCounter = 0;


	public static void Main(string[] arguments)
	{
		if (arguments.Length > 0)
		{ // 1. parameter: name of the pipe
			pipeName = arguments[0];
			// 2. parameter: computername
			if (arguments.Length > 1) 
			{
				server = arguments[1];
				// 3. parameter: count of requests
				if (arguments.Length > 2) 
				{
					int tempInt;
					if (Int32.TryParse(arguments[2], out tempInt))
						count = tempInt;
				}
			}
		}	

		// for testing create multiple clients
		for (Int32 i = 1; i <= count; i++)
		{
			Thread t = new Thread(ThreadProc);
			t.Start(i);
		}

		// wait until all responses received and displayed
		do { 
			System.Threading.Thread.Sleep(10);
		} while (instanceCounter < count);
	}

	private static void ThreadProc(Object index)
	{
		NamedPipeClientStream pipe = new NamedPipeClientStream(server, pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.WriteThrough);

		try {
			// connect (timeout in milliseconds)
			pipe.Connect(2000);
			
			// must Connect before setting ReadMode
			pipe.ReadMode = PipeTransmissionMode.Message;
		}
		catch (Exception e) {
			Console.WriteLine("Connection failed for test request " + (Int32)index + ": " + e);
			System.Threading.Interlocked.Increment(ref instanceCounter);
			return;
		}

		// asynchronously send data to the server
		string message = "Test request " + (Int32)index;
		byte[] output = Encoding.UTF8.GetBytes(message);
		Debug.Assert(output.Length < SERVER_IN_BUFFER_SIZE);
		Console.WriteLine("Client request " + (Int32)index + ": " + message);
		pipe.Write(output, 0, output.Length);

		// read the result
		byte[] data = new Byte[SERVER_OUT_BUFFER_SIZE];
		Int32 bytesRead = pipe.Read(data, 0, data.Length);
		Console.WriteLine("Server response to request " + (Int32)index + ": " + Encoding.UTF8.GetString(data, 0, bytesRead));

		// done with this one
		pipe.Close();
		System.Threading.Interlocked.Increment(ref instanceCounter);
	}
}
