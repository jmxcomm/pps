using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace pss.poc
{
	public static class OrderHttpApi
	{
		private static readonly string busConnectionString = Environment.GetEnvironmentVariable("queueConnection");

		private static readonly string busQueueName = Environment.GetEnvironmentVariable("orderQueue");
		private static readonly QueueClient orderQueue = new QueueClient(busConnectionString, busQueueName);

		private static readonly string downloadQueueName = Environment.GetEnvironmentVariable("downloadQueue");
		private static readonly QueueClient downloadQueue = new QueueClient(busConnectionString, downloadQueueName);

		private static readonly string storageConnectionString = Environment.GetEnvironmentVariable("storageConnection");
		private static readonly string storageContainerName = Environment.GetEnvironmentVariable("containerName");
		private static readonly BlobServiceClient storage = new BlobServiceClient(storageConnectionString); 
		private static readonly BlobContainerClient container = storage.GetBlobContainerClient(storageContainerName);

		private static readonly MediaTypeCollection types = CreateContentCollection("application/json");

		private static readonly JsonSchema schema = JsonSchema.Parse(@"{
			'type': 'object',
			'not': { 'required': ['InternalId'] },
			'properties': {
				'ExternalId': { 'type': 'string', 'required': true },
				'OrderDate': { 'type': 'string', 'format': 'date' },
				'ArticleCode': { 'type': 'string', 'required': true },
				'Quantity': { 'type': 'integer', 'required': true},
				'Files': {
					'type': 'array',
					'required': true,
					'minItems': 1,
					'items': {
						'type': 'object',
						'properties': {
							'Url': { 'type': 'string', 'required': true },
							'Hash': { 'type': 'string', 'required': true, 'pattern': '[0-9a-f]{32}' }
						}
					}
				}
			}
		}");

		[FunctionName("ORDER_API_PLACE_ORDER")]
		public static async Task<IActionResult> placeOrder(
			[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "orders")]
			HttpRequest req, ILogger log)
		{
			try
			{
				JObject order = JsonConvert.DeserializeObject<JObject>(await new StreamReader(req.Body).ReadToEndAsync());
				if (!order.IsValid(schema, out var errors))
				{
					var error = new JObject
					{
						["status"] = 400,
						["detail"] = "order json was does not comply with schema",
						["errors"] = JsonConvert.DeserializeObject<JToken>(JsonConvert.SerializeObject(errors))
					};
					return new BadRequestObjectResult(JsonConvert.SerializeObject(error));
				}

				String internalId = Guid.NewGuid().ToString();
				order["InternalId"] = internalId;
				order["From"] = req.Host.ToString();
				order["ReceivedOn"] = DateTime.Now;

				BlobClient statusBlob = container.GetBlobClient($"{order["ExternalId"]}.status.json");
				BlobClient contentBlob = container.GetBlobClient($"{order["ExternalId"]}.content.json");
				if ((await statusBlob.ExistsAsync()) || (await contentBlob.ExistsAsync()))
				{
					var error = new JObject
					{
						["status"] = 400,
						["detail"] = "External id has been used.",
						["ExternalId"] = order["ExternalId"]
					};
					return new BadRequestObjectResult(JsonConvert.SerializeObject(error));
				}

				var status = new JObject
				{
					["status"] = "draft"
				};
				await statusBlob.UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(status))));
				await contentBlob.UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(order))));

				var messageBody = new JObject();

				messageBody["ExternalId"] = order["ExternalId"];
				messageBody["InternalId"] = order["InternalId"];

				var messageBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(messageBody));
				await orderQueue.SendAsync(new Message(messageBytes));
				await downloadQueue.SendAsync(new Message(messageBytes));

				var result = new JObject
				{
					["success"] = "ok",
					["InternalId"] = internalId,
					["ExternalId"] = order["ExternalId"]
				};

				return Json(result);
			}
			catch (Exception e)
			{
				log.LogCritical($"{e.Message}", e);
				return Error(400, "Failed to place order.", e);
			}
		}

		[FunctionName("ORDER_API_FORCE_ORDER_MEDIA_DOWNLOAD")]
		public static async Task<IActionResult> redownload(
			[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "redownload")]
			HttpRequest req, ILogger log)
		{
			JObject orderSpec = JsonConvert.DeserializeObject<JObject>(await new StreamReader(req.Body).ReadToEndAsync());
			var messageBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(orderSpec));
			await downloadQueue.SendAsync(new Message(messageBytes));
			return new OkObjectResult("");
		}

		public static IActionResult Error(int status, String title, Exception e)
		{
			var error = new JObject
			{
				["status"] = 400,
				["title"] = "Failed to fetch state machine",
				["detail"] = e.Message
			};
			return new BadRequestObjectResult(JsonConvert.SerializeObject(error))
			{
				ContentTypes = types
			};
		}

		private static OkObjectResult Json(object anything)
		{
			return Raw(JsonConvert.SerializeObject(anything));
		}

		private static OkObjectResult Raw(object anything)
		{
			return new OkObjectResult(anything);
		}

		private static MediaTypeCollection CreateContentCollection(String contentType)
		{
			MediaTypeCollection types = new MediaTypeCollection();
			types.Add(contentType);
			return types;
		}
	}
}