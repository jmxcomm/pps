using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace pss.poc
{
	public static class QueueTrigger
	{
		private static readonly string statusNotififcationUrl = Environment.GetEnvironmentVariable("statusNotificationUrl");
		
		private static readonly string storageConnectionString = Environment.GetEnvironmentVariable("storageConnection");
		private static readonly string storageContainerName = Environment.GetEnvironmentVariable("containerName");
		private static readonly BlobServiceClient storage = new BlobServiceClient(storageConnectionString); 
		private static readonly BlobContainerClient container = storage.GetBlobContainerClient(storageContainerName);

		private static readonly string busConnectionString = Environment.GetEnvironmentVariable("queueConnection");
		private static readonly string busQueueName = Environment.GetEnvironmentVariable("statusNotifyQueue");
		private static readonly QueueClient notifyQueue = new QueueClient(busConnectionString, busQueueName);

		[FunctionName("Status_Process_Queue_Trigger")]
		public static async Task Status([ServiceBusTrigger("binteqordersstatus", Connection = "queueConnection")]
			string body, ILogger log)
		{
			log.LogInformation($"C# ServiceBus queue trigger function processed message: {body}");

			var order = JsonConvert.DeserializeObject<JObject>(body);
			BlobClient blob = container.GetBlobClient($"{order["ExternalId"]}.status.json");
			await blob.UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes(body)), true, CancellationToken.None);

			var message = new JObject
			{
				["ExternalId"] = order["ExternalId"],
				["InternalId"] = order["InternalId"]
			};

			await notifyQueue.SendAsync(new Message (Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message))));
		}

		[FunctionName("Status_Notify_Queue_Trigger")]
		public static async Task Notify([ServiceBusTrigger("binteqordersnotify", Connection = "queueConnection")]
			string body, ILogger log)
		{
			log.LogInformation($"C# ServiceBus queue trigger function processed message: {body}");
			var orderSpec = JsonConvert.DeserializeObject<JObject>(body);

			BlobClient blob = container.GetBlobClient($"{orderSpec["ExternalId"]}.status.json");
			Stream stream = (await blob.DownloadAsync()).Value.Content;
			var orderStatus = JsonConvert.DeserializeObject<JObject>(await new StreamReader(stream).ReadToEndAsync());

			var payload = new JObject
			{
				["ExternalId"] = orderStatus["ExternalId"],
				["InternalId"] = orderStatus["InternalId"],
				["Status"] = orderStatus["Status"]
			};

			HttpClient client = new HttpClient();
			ByteArrayContent content = new ByteArrayContent(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)));
			log.LogInformation("url: " + statusNotififcationUrl + "/" + orderStatus["ExternalId"].Value<String>());
			var response = await client.PostAsync(statusNotififcationUrl + "/" + orderStatus["ExternalId"].Value<String>(), content);

			if ( HttpStatusCode.OK.Equals(response.StatusCode) )
			{
				throw new Exception("Notify failed");
			}
		}
	}
}