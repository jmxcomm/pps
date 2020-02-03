using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.ServiceBus;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace pss.poc
{
	public static class ProductsHttpApi
	{
		private static readonly string busConnectionString = Environment.GetEnvironmentVariable("queueConnection");
		private static readonly string busQueueName = Environment.GetEnvironmentVariable("queueName");
		private static readonly QueueClient broker = new QueueClient(busConnectionString, busQueueName);

		private static readonly string storageConnectionString = Environment.GetEnvironmentVariable("storageConnection");
		private static readonly string storageContainerName = Environment.GetEnvironmentVariable("containerName");
		private static readonly BlobServiceClient storage = new BlobServiceClient(storageConnectionString); 
		private static readonly BlobContainerClient container = storage.GetBlobContainerClient(storageContainerName);

		[FunctionName("PRODUCT_API_GET_ALL")]
		public static async Task<IActionResult> getAll(
			[HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "products")]
			HttpRequest req, ILogger log)
		{
			try
			{
				BlobClient blob = container.GetBlobClient("products.json");
				return new OkObjectResult((await blob.DownloadAsync()).Value.Content);
			}
			catch (Exception e)
			{
				log.LogCritical($"", e);
				return Error(400, "Failed to fetch all products", e);
			}
		}

		[FunctionName("PRODUCT_API_GET_BY_ID")]
		public static async Task<IActionResult> getById(
			[HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "products/{id}")]
			HttpRequest req, string id, ILogger log)
		{
			try
			{
				BlobClient blob = container.GetBlobClient("products.json");
				var info = await blob.DownloadAsync();
				String content = await new StreamReader(info.Value.Content).ReadToEndAsync();
				JArray products = JsonConvert.DeserializeObject<JArray>(content);

				var result = products.First(product => product["id"].Value<string>() == id);

				return new OkObjectResult(JsonConvert.SerializeObject(result));
			}
			catch (Exception e)
			{
				log.LogCritical($"{e.Message}", e);
				return Error(400, "Failed to fetch product by id", e);
			}
		}

		[FunctionName("ORDER_CREATE")]
		public static async Task<IActionResult> putOrder(
			[HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "orders")]
			HttpRequest req, ILogger log)
		{
			try
			{
				JObject payload = JsonConvert.DeserializeObject<JObject>(await new StreamReader(req.Body).ReadToEndAsync());

				BlobClient blob = container.GetBlobClient($"{payload["ExternalId"]}.json");
				await blob.UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload))));

				var messageBody = new JObject();
				messageBody["ExternalId"] = payload["ExternalId"];
				var messageBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(messageBody));
				await broker.SendAsync(new Message(messageBytes));

				var responseBody = new JObject();
				responseBody["status"] = "success";
				return new OkObjectResult(JsonConvert.SerializeObject (responseBody));
			}
			catch (Exception e)
			{
				log.LogCritical($"{e.Message}", e);
				return Error(400, "Failed to create the order", e);
			}
		}

		public static IActionResult Error(int status, String title, Exception e)
		{
			var error = new JObject
			{
				["status"] = 400,
				["title"] = "Failed to fetch state machine",
				["detail"] = e.Message
			};
			return new BadRequestObjectResult(JsonConvert.SerializeObject(error));
		}
	}
}