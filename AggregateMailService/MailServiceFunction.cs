using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using StackExchange.Redis;

namespace AggregateMailService
{
    public static class MailServiceFunction
    {
        private const int RedisDbNumber = 7;
        private const int LockSeconds = 60;

        private static readonly List<SmtpServer> SmtpServers = new List<SmtpServer>
        {
            new SmtpServer("smtp.sendgrid.net", 587, "[username]", "[password]")
        };

        private static readonly List<string> Filters = new List<string>
        {
            "gmail.com"
        };

        private static readonly Lazy<IConnectionMultiplexer> Redis = new Lazy<IConnectionMultiplexer>(() =>
            ConnectionMultiplexer.Connect(
                "[namespace].redis.cache.windows.net:6380,password=[password],ssl=True,abortConnect=False"));

        private static readonly string StorageConnectionString =
            AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);

        private static readonly Lazy<CloudStorageAccount> StorageAccount =
            new Lazy<CloudStorageAccount>(() => CloudStorageAccount.Parse(StorageConnectionString));

        private static readonly Lazy<CloudBlobClient> BlobClient =
            new Lazy<CloudBlobClient>(() => StorageAccount.Value.CreateCloudBlobClient());

        private static readonly Lazy<CloudBlobContainer> BlobContainer =
            new Lazy<CloudBlobContainer>(() => BlobClient.Value.GetContainerReference("mailservice"));

        [FunctionName("MailService")]
        public static async Task RunAsync([QueueTrigger("mailservice", Connection = "AzureWebJobsStorage")]
            QueueMail myQueueItem, TraceWriter log, ExecutionContext context)
        {
            SmtpServer smtpServer;
            var toDomain = myQueueItem.To.Split('@').Last();
            if (Filters.Contains(toDomain))
            {
                var lua = $@"
local function GetServer(keys)
    for _,key in ipairs(keys) do
        local count = redis.call('GET', key)
        if(count ~= 1) then
            local current = redis.call('INCR', key)
            if(current == 1) then
                redis.call('EXPIRE', key, {LockSeconds})
                return key
            end
        end
    end
    return nil
end
return GetServer(KEYS)";

                var redisKeys = Enumerable.Range(0, SmtpServers.Count).Select(p => (RedisKey) $"{p}:{toDomain}")
                    .ToArray();
                var result = Redis.Value.GetDatabase(RedisDbNumber).ScriptEvaluate(lua, redisKeys);
                if (result.IsNull)
                    throw new ConstraintException();
                var index = int.Parse(result.ToString().Split(':').First());
                smtpServer = SmtpServers[index];
            }
            else
            {
                smtpServer = SmtpServers[0];
            }

            var smtpClient = new SmtpClient(smtpServer.Host, smtpServer.Port)
            {
                Credentials = new NetworkCredential(smtpServer.UserName, smtpServer.Password)
            };

            var mail = new MailMessage(myQueueItem.From, string.Join(",", myQueueItem.To))
            {
                Subject = myQueueItem.Subject,
                Body = myQueueItem.Body,
                BodyEncoding = Encoding.UTF8,
                IsBodyHtml = true
            };

            if (myQueueItem.AttachedBlobNames.Any())
            {
                if (!await BlobContainer.Value.ExistsAsync())
                    throw new Exception($"No blob");

                var tasks = myQueueItem.AttachedBlobNames.AsParallel().Select(blobname => Task.Run(async () =>
                {
                    var block = BlobContainer.Value.GetBlockBlobReference(blobname);
                    if (!await block.ExistsAsync())
                        throw new Exception($"No block");
                    var stream = await block.OpenReadAsync();
                    var fileName = blobname.Split('/').Last();
                    return new Attachment(stream, fileName);
                })).ToArray();
                var attachments = await Task.WhenAll(tasks);
                foreach (var attachment in attachments) mail.Attachments.Add(attachment);
            }

            smtpClient.SendCompleted += (sender, e) =>
            {
                smtpClient.Dispose();
                mail.Dispose();
                if (e.Cancelled)
                    throw new SmtpException($"Cancelled by smtp server.");
                if (e.Error != null)
                    throw e.Error;

                if (!(e.UserState is string[] blobnames)) return;
                if (blobnames.Any())
                    blobnames.AsParallel().ForAll(async blobname =>
                        await BlobContainer.Value.GetBlockBlobReference(blobname).DeleteAsync());
            };
            smtpClient.SendAsync(mail, myQueueItem.AttachedBlobNames);
        }
    }
}