using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.ServiceBus;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace pss.agent
{
	class Program
	{
		private static readonly string baseDir = Environment.GetEnvironmentVariable("baseDir");
		private static readonly string busConnectionString = Environment.GetEnvironmentVariable("queueConnection");
		private static readonly string busQueueName = Environment.GetEnvironmentVariable("queueName");
		private static readonly string storageConnectionString = Environment.GetEnvironmentVariable("storageConnection");
		private static readonly string storageContainerName = Environment.GetEnvironmentVariable("containerName");

		private static readonly QueueClient orderQueue = new QueueClient(busConnectionString, busQueueName);
		private static readonly BlobServiceClient storage = new BlobServiceClient(storageConnectionString); 
		private static readonly BlobContainerClient container = storage.GetBlobContainerClient(storageContainerName);

		static void Main(string[] args)
		{
			orderQueue.RegisterMessageHandler(
				async (message, token) =>
				{
					JObject order = JsonConvert.DeserializeObject<JObject>(Encoding.UTF8.GetString(message.Body));
					var path = Path.Combine(baseDir, $"{order["ExternalId"]}.json");

					BlobClient blob = container.GetBlobClient($"{order["ExternalId"]}.content.json");
					var info = await blob.DownloadAsync();

					StreamWriter outputFile = new StreamWriter(path);
					await outputFile.WriteAsync(await new StreamReader(info.Value.Content).ReadToEndAsync());
					outputFile.Close();
				},
				(error) =>
				{
					Console.WriteLine($"Received An Error: {error}, {error.Exception.Message}");
					return Task.CompletedTask;
				}
			);

			Console.WriteLine("Press 'q' to quit the sample.");
			while (Console.Read() != 'q') ;
		}
	}
}