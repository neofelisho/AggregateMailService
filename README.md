# AggregateMailService

## MailServiceFunction
- Azure function.
- Storage queue triggered function.
- Execute lua script on Redis to achieve atmoic operation between multiple function hosts.
- Send mail by mail server list and by the limit of lock seconds.

## EnqueueMailFunction
- Azure function.
- HTTP POST API.
- Enqueue mail with attachments into storage queue.

## EnqueueMail
- Console App.
- Directly enqueue mail to queue to test the queue triggered function.
