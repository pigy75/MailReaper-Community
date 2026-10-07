using System.IO;
using GmailToPst.Core.Models;
using MimeKit;

namespace GmailToPst.Providers.Helpers;

public static class MimeParserHelper
{
    public static (EmailMessage Message, List<MimePart> AttachmentParts) ParseMimeMessage(byte[] rawBytes, string folderId, string folderName, string? externalId = null)
    {
        using var stream = new MemoryStream(rawBytes);
        var mime = MimeMessage.Load(stream);

        var attachments = new List<AttachmentInfo>();
        var attachmentParts = new List<MimePart>();

        var msgId = externalId ?? Guid.NewGuid().ToString();

        // 1. Esplora tutti i BodyParts per trovare allegati e immagini
        foreach (var entity in mime.BodyParts)
        {
            if (entity is MimePart part)
            {
                var isTextBody = (part.ContentType.MimeType.Equals("text/plain", StringComparison.OrdinalIgnoreCase) || 
                                  part.ContentType.MimeType.Equals("text/html", StringComparison.OrdinalIgnoreCase)) &&
                                 string.IsNullOrWhiteSpace(part.FileName);

                if (isTextBody) continue; // È il corpo del testo principale, non un allegato

                var isAttachment = part.IsAttachment ||
                                   !string.IsNullOrWhiteSpace(part.FileName) ||
                                   (part.ContentDisposition != null && !string.Equals(part.ContentDisposition.Disposition, "inline", StringComparison.OrdinalIgnoreCase)) ||
                                   part.ContentType.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase) ||
                                   part.ContentType.MediaType.Equals("application", StringComparison.OrdinalIgnoreCase) ||
                                   part.ContentType.MediaType.Equals("audio", StringComparison.OrdinalIgnoreCase) ||
                                   part.ContentType.MediaType.Equals("video", StringComparison.OrdinalIgnoreCase);

                if (isAttachment)
                {
                    var fileName = !string.IsNullOrWhiteSpace(part.FileName)
                        ? part.FileName
                        : (!string.IsNullOrWhiteSpace(part.ContentId)
                            ? $"{part.ContentId.Trim('<', '>')}.{part.ContentType.MediaSubtype}"
                            : $"allegato_{attachments.Count + 1}.{part.ContentType.MediaSubtype}");

                    long size = 0;
                    try
                    {
                        using var ms = new MemoryStream();
                        part.Content?.DecodeTo(ms);
                        size = ms.Length;
                    }
                    catch { }

                    if (size > 0)
                    {
                        var attInfo = new AttachmentInfo
                        {
                            Id = Guid.NewGuid().ToString(),
                            MessageId = msgId,
                            FileName = fileName,
                            ContentType = part.ContentType?.MimeType ?? "application/octet-stream",
                            SizeBytes = size
                        };

                        attachments.Add(attInfo);
                        attachmentParts.Add(part);
                    }
                }
            }
            else if (entity is MessagePart msgPart && msgPart.Message != null)
            {
                var fileName = !string.IsNullOrWhiteSpace(msgPart.Message.Subject)
                    ? $"{msgPart.Message.Subject}.eml"
                    : $"messaggio_{attachments.Count + 1}.eml";

                long size = 0;
                try
                {
                    using var ms = new MemoryStream();
                    msgPart.Message.WriteTo(ms);
                    size = ms.Length;
                }
                catch { }

                if (size > 0)
                {
                    attachments.Add(new AttachmentInfo
                    {
                        Id = Guid.NewGuid().ToString(),
                        MessageId = msgId,
                        FileName = fileName,
                        ContentType = "message/rfc822",
                        SizeBytes = size
                    });
                }
            }
        }

        var email = new EmailMessage
        {
            Id = msgId,
            MessageId = mime.MessageId ?? Guid.NewGuid().ToString(),
            FolderId = folderId,
            FolderName = folderName,
            Subject = string.IsNullOrWhiteSpace(mime.Subject) ? "(Nessun oggetto)" : mime.Subject,
            From = mime.From.Mailboxes.FirstOrDefault()?.Address ?? string.Empty,
            FromDisplayName = mime.From.Mailboxes.FirstOrDefault()?.Name ?? string.Empty,
            To = string.Join("; ", mime.To.Mailboxes.Select(m => string.IsNullOrWhiteSpace(m.Name) ? m.Address : $"{m.Name} <{m.Address}>")),
            Cc = mime.Cc.Mailboxes.Any() ? string.Join("; ", mime.Cc.Mailboxes.Select(m => string.IsNullOrWhiteSpace(m.Name) ? m.Address : $"{m.Name} <{m.Address}>")) : null,
            Bcc = mime.Bcc.Mailboxes.Any() ? string.Join("; ", mime.Bcc.Mailboxes.Select(m => string.IsNullOrWhiteSpace(m.Name) ? m.Address : $"{m.Name} <{m.Address}>")) : null,
            Date = mime.Date != DateTimeOffset.MinValue ? mime.Date : DateTimeOffset.UtcNow,
            SizeBytes = rawBytes.Length,
            BodyText = mime.TextBody,
            BodyHtml = mime.HtmlBody,
            HasAttachments = attachments.Any(),
            Attachments = attachments
        };

        return (email, attachmentParts);
    }
}
