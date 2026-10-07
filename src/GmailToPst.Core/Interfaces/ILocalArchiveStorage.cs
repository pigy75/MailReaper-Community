using GmailToPst.Core.Models;

namespace GmailToPst.Core.Interfaces;

public interface ILocalArchiveStorage : IAsyncDisposable
{
    Task InitializeAsync(string archiveBasePath, string accountEmail, int? year = null, CancellationToken cancellationToken = default);
    Task SaveFolderHierarchyAsync(IEnumerable<EmailFolder> folders, CancellationToken cancellationToken = default);
    Task<List<EmailFolder>> GetFoldersAsync(CancellationToken cancellationToken = default);
    Task<List<EmailFolder>> GetFoldersAsync(int? yearFilter, CancellationToken cancellationToken = default);
    Task SaveMessageAsync(EmailMessage message, byte[] rawEmlBytes, CancellationToken cancellationToken = default);
    Task<List<EmailMessage>> GetMessagesInFolderAsync(string folderId, int skip = 0, int take = 100, string? searchKeyword = null, CancellationToken cancellationToken = default);
    Task<List<EmailMessage>> GetMessagesInFolderAsync(string folderId, int skip, int take, string? searchKeyword, int? yearFilter, CancellationToken cancellationToken = default);
    Task<int> GetMessageCountInFolderAsync(string folderId, string? searchKeyword = null, CancellationToken cancellationToken = default);
    Task<int> GetMessageCountInFolderAsync(string folderId, string? searchKeyword, int? yearFilter, CancellationToken cancellationToken = default);
    Task<EmailMessage?> GetMessageDetailsAsync(string messageId, CancellationToken cancellationToken = default);
    Task<byte[]> GetRawEmlBytesAsync(string messageId, CancellationToken cancellationToken = default);
    Task<byte[]> GetAttachmentBytesAsync(string attachmentId, CancellationToken cancellationToken = default);
    Task<int> GetTotalMessageCountAsync(int? yearFilter = null, CancellationToken cancellationToken = default);
    Task<long> GetTotalSizeBytesAsync(int? yearFilter = null, CancellationToken cancellationToken = default);
    Task<List<int>> GetAvailableYearsAsync(CancellationToken cancellationToken = default);
    Task<HashSet<string>> GetExistingMessageIdsAsync(string? folderId = null, CancellationToken cancellationToken = default);
    Task<int> DeleteYearDataAsync(int year, CancellationToken cancellationToken = default);
    Task<(int TotalCount, long TotalBytes)> ExtractAttachmentsAsync(string targetDirectory, AttachmentFilterOptions filterOptions, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default);
}
