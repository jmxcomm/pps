using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.ServiceBus;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace pss.getMediaFile
{
	class Program
	{
		private static readonly string baseDir = Environment.GetEnvironmentVariable("baseDir");
		private static readonly string busConnectionString = Environment.GetEnvironmentVariable("queueConnection");
		private static readonly string busQueueName = Environment.GetEnvironmentVariable("queueName");
		private static readonly string storageConnectionString = Environment.GetEnvironmentVariable("storageConnection");
		private static readonly string storageContainerName = Environment.GetEnvironmentVariable("containerName");

		private static readonly QueueClient queue = new QueueClient(busConnectionString, busQueueName);
		private static readonly BlobServiceClient storage = new BlobServiceClient(storageConnectionString); 
		private static readonly BlobContainerClient container = storage.GetBlobContainerClient(storageContainerName);

		static void Main(string[] args)
		{
			queue.RegisterMessageHandler(
				async (message, token) =>
				{
					JObject orderSpec = JsonConvert.DeserializeObject<JObject>(Encoding.UTF8.GetString(message.Body));

					BlobClient blob = container.GetBlobClient($"{orderSpec["ExternalId"]}.content.json");
					StreamReader stream = new StreamReader((await blob.DownloadAsync()).Value.Content);
					JObject order = JsonConvert.DeserializeObject<JObject>(await stream.ReadToEndAsync());

					JArray files = order["Files"].Value<JArray>();

					IEnumerable<Task> tasks = files.Select(async file =>
					{
						var path = Path.Combine(baseDir, file["Hash"].Value<string>() + ".pdf");

						if (File.Exists(path))
						{
							return;
						}

						HttpClient client = new HttpClient();
						var response  = await client.GetAsync(file["Url"].Value<string>());

						if (! HttpStatusCode.OK.Equals(response.StatusCode))
						{
							throw new Exception($"Failed to download pdf: {response.StatusCode}" + file["Url"].Value<string>());
						}

						using (Stream stream = File.Create(path))
						{
							await response.Content.CopyToAsync(stream);
						}
					});

					Task.WaitAll(tasks.ToArray());
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