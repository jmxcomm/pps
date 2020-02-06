using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace pss.poc
{
	public static class QueueTrigger
	{
		
		private static readonly string storageConnectionString = Environment.GetEnvironmentVariable("storageConnection");
		private static readonly string storageContainerName = Environment.GetEnvironmentVariable("containerName");
		private static readonly BlobServiceClient storage = new BlobServiceClient(storageConnectionString); 
		private static readonly BlobContainerClient container = storage.GetBlobContainerClient(storageContainerName);
		
		[FunctionName("QueueTrigger")]
		public static async Task RunAsync([ServiceBusTrigger("binteqordersstatus", Connection = "queueConnection")]
			string body, ILogger log)
		{
			try
			{
				log.LogInformation($"C# ServiceBus queue trigger function processed message: {body}");
				var order = JsonConvert.DeserializeObject<JObject>(body);
				string path = $"{order["ExternalId"]}.status.json";
				log.LogInformation($"path? : ({container}, {storageContainerName}) {path}");

				BlobClient blob = container.GetBlobClient(path);
				await blob.UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes(body)), true, CancellationToken.None);
			}
			catch (Exception e)
			{
				log.LogError($"Failed: {e.Message}");
				throw e;
			}
		}
	}
}