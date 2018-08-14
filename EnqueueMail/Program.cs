using System;
using System.IO;
using System.Threading.Tasks;
using AggregateMailService;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;

namespace EnqueueMail
{
    internal class Program
    {
#if DEBUG
        private static readonly string BasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            $"..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}");
#else
        private static readonly string BasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
#endif
        private static readonly IConfiguration Configuration =
            new ConfigurationBuilder().SetBasePath(BasePath).AddJsonFile("appsettings.json").Build();

        private static async Task Main()
        {
            var storageConnectionString = Configuration["AzureWebJobsStorage"];
            var storageAccount = CloudStorageAccount.Parse(storageConnectionString);

            var blobClient = storageAccount.CreateCloudBlobClient();
            var blob = blobClient.GetContainerReference("mailservice");
            if (await blob.CreateIfNotExistsAsync())
                await blob.SetPermissionsAsync(new BlobContainerPermissions
                {
                    PublicAccess = BlobContainerPublicAccessType.Container
                });

            const string fileName = "attachment.txt";
            string blobName;
            CloudBlockBlob block;
            do
            {
                blobName = $"{Guid.NewGuid()}/{fileName}";
                block = blob.GetBlockBlobReference(blobName);
            } while (await block.ExistsAsync());

            var buffer = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, BasePath, fileName));
            await block.UploadFromByteArrayAsync(buffer, 0, buffer.Length);

            var queueClient = storageAccount.CreateCloudQueueClient();
            var queue = queueClient.GetQueueReference("mailservice");
            await queue.CreateIfNotExistsAsync();

            var json = JsonConvert.SerializeObject(new QueueMail
            {
                From = "from@gmail.com",
                To = "to@gmail.com",
                Subject = "Hello Neo",
                Body = "Hello world",
                AttachedBlobNames = new[] {blobName}
            });
            var message = new CloudQueueMessage(json);
            await queue.AddMessageAsync(message);
        }
    }
}