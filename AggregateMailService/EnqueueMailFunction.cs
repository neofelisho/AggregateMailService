using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;

namespace AggregateMailService
{
    public static class EnqueueMailFunction
    {
        private static readonly string StorageConnectionString =
            AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);

        private static readonly Lazy<CloudStorageAccount> StorageAccount =
            new Lazy<CloudStorageAccount>(() => CloudStorageAccount.Parse(StorageConnectionString));

        private static readonly Lazy<CloudBlobClient> BlobClient =
            new Lazy<CloudBlobClient>(() => StorageAccount.Value.CreateCloudBlobClient());

        private static readonly Lazy<CloudBlobContainer> BlobContainer =
            new Lazy<CloudBlobContainer>(() => BlobClient.Value.GetContainerReference("mailservice"));

        private static readonly Lazy<CloudQueueClient> QueueClient =
            new Lazy<CloudQueueClient>(() => StorageAccount.Value.CreateCloudQueueClient());

        private static readonly Lazy<CloudQueue> Queue =
            new Lazy<CloudQueue>(() => QueueClient.Value.GetQueueReference("mailservice"));

        [FunctionName("SendMail")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)]
            HttpRequest req, TraceWriter log)
        {
            if (!req.Form.TryGetValue("From", out var from))
                return new BadRequestErrorMessageResult($"Key:From is missing.");
            if (!req.Form.TryGetValue("To", out var to))
                return new BadRequestErrorMessageResult($"Key:To is missing.");
            if (!req.Form.TryGetValue("Subject", out var subject))
                return new BadRequestErrorMessageResult($"Key:Subject is missing.");
            if (!req.Form.TryGetValue("Body", out var body))
                return new BadRequestErrorMessageResult($"Key:Body is missing.");

            var blobNames = new ConcurrentBag<string>();
            if (req.Form.Files.Count > 0)
            {
                if (await BlobContainer.Value.CreateIfNotExistsAsync())
                    await BlobContainer.Value.SetPermissionsAsync(new BlobContainerPermissions
                    {
                        PublicAccess = BlobContainerPublicAccessType.Container
                    });
                foreach (var file in req.Form.Files)
                {
                    var fileName = file.FileName;
                    string blobName;
                    CloudBlockBlob block;
                    do
                    {
                        blobName = $"{Guid.NewGuid()}/{fileName}";
                        block = BlobContainer.Value.GetBlockBlobReference(blobName);
                    } while (await block.ExistsAsync());

                    blobNames.Add(blobName);
                    var stream = file.OpenReadStream();
                    await block.UploadFromStreamAsync(stream);
                }
            }

            var queueMail = new QueueMail
            {
                From = from[0],
                To = to[0],
                Subject = subject[0],
                Body = body[0],
                AttachedBlobNames = blobNames.ToArray()
            };

            await Queue.Value.CreateIfNotExistsAsync();
            var json = JsonConvert.SerializeObject(queueMail);
            var message = new CloudQueueMessage(json);
            await Queue.Value.AddMessageAsync(message);

            return new OkObjectResult($"Enqueue mail success.");
        }
    }
}