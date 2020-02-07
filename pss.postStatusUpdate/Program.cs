using System;
using System.IO;
using System.Text;
using Microsoft.Azure.ServiceBus;

namespace pss.postStatusUpdate
{
	class Program
	{
		private static readonly string busConnectionString = Environment.GetEnvironmentVariable("queueConnection");
		private static readonly string busQueueName = Environment.GetEnvironmentVariable("queueName");
		private static readonly QueueClient statusQueue = new QueueClient(busConnectionString, busQueueName);

		static void Main(string[] args)
		{
			using (FileSystemWatcher watcher = new FileSystemWatcher())
			{
				watcher.Path = "/tmp/in-dir";
				watcher.NotifyFilter = NotifyFilters.LastAccess
				                       | NotifyFilters.LastWrite
				                       | NotifyFilters.FileName
				                       | NotifyFilters.DirectoryName;

				watcher.Filter = "*.json";

				// Add event handlers.
				watcher.Created += async (source, args) =>
				{
					StreamReader outputFile = new StreamReader(args.FullPath);
					await statusQueue.SendAsync(new Message (Encoding.UTF8.GetBytes(await outputFile.ReadToEndAsync())));
				};

				// Begin watching.
				watcher.EnableRaisingEvents = true;

				// Wait for the user to quit the program.
				Console.WriteLine("Press 'q' to quit the sample.");
				while (Console.Read() != 'q') ;
			}
		}
	}

}