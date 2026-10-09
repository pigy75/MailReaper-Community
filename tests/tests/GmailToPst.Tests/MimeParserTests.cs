using GmailToPst.Providers.Helpers;
using MimeKit;
using System.IO;
using System.Text;
using Xunit;

namespace GmailToPst.Tests;

public class MimeParserTests
{
    [Fact]
    public void ParseMimeMessage_ExtractsHeadersBodiesAndAttachments()
    {
        var builder = new BodyBuilder
        {
            TextBody = "Ciao, questo è il corpo testo.",
            HtmlBody = "<html><body><h1>Ciao</h1><p>questo è il corpo HTML.</p></body></html>"
        };
        builder.Attachments.Add("documento.pdf", Encoding.UTF8.GetBytes("Fake PDF content"), new ContentType("application", "pdf"));

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Mario Rossi", "mario.rossi@example.com"));
        mime.To.Add(new MailboxAddress("Luigi Bianchi", "luigi.bianchi@example.com"));
        mime.Subject = "Oggetto di test backup 2022";
        mime.Date = new DateTimeOffset(2022, 5, 15, 10, 30, 0, TimeSpan.Zero);
        mime.Body = builder.ToMessageBody();

        byte[] rawBytes;
        using (var ms = new MemoryStream())
        {
            mime.WriteTo(ms);
            rawBytes = ms.ToArray();
        }

        var (email, attParts) = MimeParserHelper.ParseMimeMessage(rawBytes, "INBOX", "Posta in arrivo", "msg-123");

        Assert.Equal("msg-123", email.Id);
        Assert.Equal("Oggetto di test backup 2022", email.Subject);
        Assert.Equal("mario.rossi@example.com", email.From);
        Assert.Equal("Mario Rossi", email.FromDisplayName);
        Assert.Contains("luigi.bianchi@example.com", email.To);
        Assert.Equal(2022, email.Date.Year);
        Assert.True(email.HasAttachments);
        Assert.Single(email.Attachments);
        Assert.Equal("documento.pdf", email.Attachments[0].FileName);
        Assert.NotNull(email.BodyHtml);
        Assert.NotNull(email.BodyText);
        Assert.Single(attParts);
    }
}
