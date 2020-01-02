// Named Pipes client to send commands to Named Pipes service
// derived from https://github.com/webcoyote/CSNamedPipes
// Markus Scholtes, 2020/01/02

// NPCommandClient <Command> <NameOfPipe> <Servername>
// default: NPCommandClient <Command> NamedPipesService .

using System;
using System.IO.Pipes;
using System.Text;
using System.Diagnostics;


public class NPCommandClient
{
	private const Int32 SERVER_IN_BUFFER_SIZE = 65536;
	private const Int32 SERVER_OUT_BUFFER_SIZE = 65536;

	static string command;
	static string pipeName = "NamedPipesService";
	static string server = ".";


	public static void Main(string[] arguments)
	{
		if (arguments.Length > 0)
		{ // 1. parameter: command to execute
			command = arguments[0];
			if (arguments.Length > 1)
			{
				// 2. parameter: name of the pipe
				pipeName = arguments[1];
				// 3. parameter: computername
				if (arguments.Length > 2)
				{
					server = arguments[2];
				}
			}
		}
		else
		{
			Console.WriteLine("Parameter missing.");
			return;
		}

		SendMessage(command);
	}

	private static void SendMessage(string message)
	{
		NamedPipeClientStream pipe = new NamedPipeClientStream(server, pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.WriteThrough);

		try {
			// connect (timeout in milliseconds)
			pipe.Connect(2000);

			// must Connect before setting ReadMode
			pipe.ReadMode = PipeTransmissionMode.Message;
		}
		catch (Exception e) {
			Console.WriteLine("Connection failed for request: " + e);
			return;
		}

		// asynchronously send data to the server
		byte[] output = Encoding.UTF8.GetBytes(message);
		Debug.Assert(output.Length < SERVER_IN_BUFFER_SIZE);
		Console.WriteLine("Client request: " + message);
		pipe.Write(output, 0, output.Length);

		// read the result
		byte[] data = new Byte[SERVER_OUT_BUFFER_SIZE];
		Int32 bytesRead = pipe.Read(data, 0, data.Length);
		Console.WriteLine("Server response: " + Encoding.UTF8.GetString(data, 0, bytesRead));

		// done with this one
		pipe.Close();
	}
}
