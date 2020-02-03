using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace pss.poc
{
	public static class ProductsHttpApi
	{
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
				var error = new JObject
				{
					["status"] = 400,
					["title"] = "Failed to fetch state machine",
					["detail"] = e.Message
				};
				return new BadRequestObjectResult(JsonConvert.SerializeObject(error));
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
				log.LogCritical($"Failed uploading blob: {e.Message}", e);
				var error = new JObject
				{
					["status"] = 400,
					["title"] = "Failed to fetch state machine",
					["detail"] = e.Message
				};
				await Console.Error.WriteLineAsync(e.ToString());
				return new BadRequestObjectResult(JsonConvert.SerializeObject(error));
			}
		}
	}
}