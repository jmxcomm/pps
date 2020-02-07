using System;
using System.IO;
using System.Text;
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
	public static class TestHttpTrigger
	{
		private static readonly string storageConnectionString = Environment.GetEnvironmentVariable("storageConnection");
		private static readonly string storageContainerName = Environment.GetEnvironmentVariable("containerName");
		private static readonly BlobServiceClient storage = new BlobServiceClient(storageConnectionString); 
		private static readonly BlobContainerClient container = storage.GetBlobContainerClient(storageContainerName);

		[FunctionName("StatusTestReceiver")]
		public static async Task<IActionResult> StatusReceive(
			[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ssg-notify/{id}")]
			HttpRequest req, string id, ILogger log)
		{
			string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
			dynamic data = JsonConvert.DeserializeObject(requestBody);

			BlobClient blob = container.GetBlobClient($"requests/{id}.content.json");
			await blob.UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(blob))));

			return new OkObjectResult("");
		}

		[FunctionName("PdfTestServe")]
		public static async Task<IActionResult> StaticServe(
			[HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ssg-serve/{file}")]
			HttpRequest req, string file, ILogger log)
		{
			BlobClient blob = container.GetBlobClient($"content/{file}");

			if (await blob.ExistsAsync())
			{
				return new OkObjectResult((await blob.DownloadAsync()).Value.Content);
			}

			var error = new JObject
			{
				["status"] = 404,
				["detail"] = "File not found",
				["path"] = file
			};

			return new BadRequestObjectResult(JsonConvert.SerializeObject(error));
		}
	}
}
