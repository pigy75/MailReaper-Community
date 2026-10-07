using GmailToPst.Core.Models;

namespace GmailToPst.Core.Interfaces;

public interface IEmailProvider : IAsyncDisposable
{
    Task<bool> ConnectAndAuthenticateAsync(AccountConfig config, CancellationToken cancellationToken = default);
    Task<List<EmailFolder>> GetFoldersAsync(CancellationToken cancellationToken = default);
    Task<int> CountMessagesAsync(BackupFilter filter, CancellationToken cancellationToken = default);
    IAsyncEnumerable<(EmailMessage Message, byte[] RawEmlBytes)> FetchMessagesAsync(
        BackupFilter filter,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
