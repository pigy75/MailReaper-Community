using System.IO;
using GmailToPst.Core.Interfaces;
using GmailToPst.Core.Models;
using Microsoft.Data.Sqlite;

namespace GmailToPst.Storage.Database;

public class SqliteArchiveStorage : ILocalArchiveStorage
{
    private string _connectionString = string.Empty;
    private string _archiveBasePath = string.Empty;
    private string _emlDirectory = string.Empty;
    private string _attachmentsDirectory = string.Empty;
    private string _remoteDbPath = string.Empty;
    private string _activeDbPath = string.Empty;
    private bool _isRemoteStorage = false;
    private SqliteConnection? _connection;

    public async Task InitializeAsync(string archiveBasePath, string accountEmail, int? year = null, CancellationToken cancellationToken = default)
    {
        _archiveBasePath = archiveBasePath;
        var sanitizedEmail = string.Concat(accountEmail.Split(Path.GetInvalidFileNameChars()));
        
        // Percorsi per file pesanti (EML e Allegati) sempre sul disco di destinazione (es. NAS Z:\)
        var fullArchiveDir = Path.Combine(_archiveBasePath, sanitizedEmail);
        _emlDirectory = Path.Combine(fullArchiveDir, "eml");
        _attachmentsDirectory = Path.Combine(fullArchiveDir, "attachments");

        Directory.CreateDirectory(fullArchiveDir);
        Directory.CreateDirectory(_emlDirectory);
        Directory.CreateDirectory(_attachmentsDirectory);

        _remoteDbPath = Path.Combine(fullArchiveDir, "archive.db");

        // Rileva se la destinazione è su rete o NAS (Z:\ o \\server\...)
        _isRemoteStorage = _archiveBasePath.StartsWith(@"\\") || 
                           (Path.IsPathRooted(_archiveBasePath) && 
                            !_archiveBasePath.StartsWith(@"C:", StringComparison.OrdinalIgnoreCase));

        if (_isRemoteStorage)
        {
            // Cache indice su SSD locale (%LOCALAPPDATA%) per prestazioni istantanee a 0ms
            var localCacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GmailToPst", "IndexCache", sanitizedEmail);
            Directory.CreateDirectory(localCacheDir);
            _activeDbPath = Path.Combine(localCacheDir, "archive.db");

            // Se il database locale manca del tutto, copialo dal NAS in Task.Run
            if (!File.Exists(_activeDbPath) && File.Exists(_remoteDbPath))
            {
                try
                {
                    await Task.Run(() => File.Copy(_remoteDbPath, _activeDbPath, true), cancellationToken);
                }
                catch { }
            }
        }
        else
        {
            _activeDbPath = _remoteDbPath;
        }

        _connectionString = $"Data Source={_activeDbPath};";

        _connection = new SqliteConnection(_connectionString);
        await _connection.OpenAsync(cancellationToken);
        using (var walCmd = new SqliteCommand(@"
            PRAGMA busy_timeout = 30000;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA cache_size = -64000;
            PRAGMA temp_store = MEMORY;
        ", _connection))
        {
            await walCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await InitializeSchemaAsync(cancellationToken);

        // Migra automaticamente eventuali archivi creati in precedenza con suffisso _YYYY (es. pigy75@gmail.com_2012, _2016, _2022)
        await MigrateLegacyArchivesAsync(archiveBasePath, sanitizedEmail, cancellationToken);
    }

    private async Task InitializeSchemaAsync(CancellationToken cancellationToken)
    {
        // Verifica se lo schema è già presente per evitare di ricreare/verificare indici ad ogni apertura su disco di rete
        using (var checkCmd = new SqliteCommand("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='Messages';", _connection))
        {
            var result = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(cancellationToken));
            if (result > 0)
            {
                return;
            }
        }

        var sql = @"
            CREATE TABLE IF NOT EXISTS Folders (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                FullPath TEXT NOT NULL,
                ParentId TEXT,
                TotalCount INTEGER DEFAULT 0,
                IsSystemFolder INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS Messages (
                Id TEXT PRIMARY KEY,
                MessageId TEXT NOT NULL,
                ThreadId TEXT,
                FolderId TEXT NOT NULL,
                FolderName TEXT NOT NULL,
                Subject TEXT,
                FromAddress TEXT,
                FromDisplayName TEXT,
                ToAddress TEXT,
                CcAddress TEXT,
                BccAddress TEXT,
                DateUnix INTEGER NOT NULL,
                DateString TEXT NOT NULL,
                Year INTEGER NOT NULL,
                HasAttachments INTEGER DEFAULT 0,
                IsRead INTEGER DEFAULT 0,
                SizeBytes INTEGER DEFAULT 0,
                BodyText TEXT,
                BodyHtml TEXT,
                RawEmlPath TEXT NOT NULL,
                FOREIGN KEY (FolderId) REFERENCES Folders(Id)
            );

            CREATE TABLE IF NOT EXISTS Attachments (
                Id TEXT PRIMARY KEY,
                MessageId TEXT NOT NULL,
                FileName TEXT NOT NULL,
                ContentType TEXT NOT NULL,
                SizeBytes INTEGER DEFAULT 0,
                LocalBlobPath TEXT NOT NULL,
                FOREIGN KEY (MessageId) REFERENCES Messages(Id)
            );

            CREATE INDEX IF NOT EXISTS IX_Messages_FolderId ON Messages(FolderId);
            CREATE INDEX IF NOT EXISTS IX_Messages_DateUnix ON Messages(DateUnix);
            CREATE INDEX IF NOT EXISTS IX_Messages_Year ON Messages(Year);
            CREATE INDEX IF NOT EXISTS IX_Messages_Folder_Date ON Messages(FolderId, DateUnix DESC);
            CREATE INDEX IF NOT EXISTS IX_Messages_Folder_Year_Date ON Messages(FolderId, Year, DateUnix DESC);
            CREATE INDEX IF NOT EXISTS IX_Messages_Year_Date ON Messages(Year, DateUnix DESC);
            CREATE INDEX IF NOT EXISTS IX_Attachments_MessageId ON Attachments(MessageId);
        ";

        using var cmd = new SqliteCommand(sql, _connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MigrateLegacyArchivesAsync(string basePath, string sanitizedEmail, CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(basePath)) return;
            var directories = Directory.GetDirectories(basePath, $"{sanitizedEmail}_*");
            if (directories.Length == 0) return;

            foreach (var legacyDir in directories)
            {
                var legacyDbPath = Path.Combine(legacyDir, "archive.db");
                if (!File.Exists(legacyDbPath)) continue;

                // Copia file EML e Attachments
                var legacyEmlDir = Path.Combine(legacyDir, "eml");
                if (Directory.Exists(legacyEmlDir))
                {
                    CopyDirectory(legacyEmlDir, _emlDirectory);
                }

                var legacyAttDir = Path.Combine(legacyDir, "attachments");
                if (Directory.Exists(legacyAttDir))
                {
                    CopyDirectory(legacyAttDir, _attachmentsDirectory);
                }

                // Importa record dal legacy db
                using var legacyConn = new SqliteConnection($"Data Source={legacyDbPath};");
                await legacyConn.OpenAsync(cancellationToken);

                // Importa cartelle
                using (var cmd = new SqliteCommand("SELECT Id, Name, FullPath, ParentId, TotalCount, IsSystemFolder FROM Folders", legacyConn))
                using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    var folders = new List<EmailFolder>();
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        folders.Add(new EmailFolder
                        {
                            Id = reader.GetString(0),
                            Name = reader.GetString(1),
                            FullPath = reader.GetString(2),
                            ParentId = reader.IsDBNull(3) ? null : reader.GetString(3),
                            TotalCount = reader.GetInt32(4),
                            IsSystemFolder = reader.GetInt32(5) == 1
                        });
                    }
                    await SaveFolderHierarchyAsync(folders, cancellationToken);
                }

                // Importa messaggi
                using (var msgCmd = new SqliteCommand(@"
                    SELECT Id, MessageId, ThreadId, FolderId, FolderName, Subject,
                           FromAddress, FromDisplayName, ToAddress, CcAddress, BccAddress,
                           DateUnix, DateString, HasAttachments, IsRead, SizeBytes,
                           BodyText, BodyHtml, RawEmlPath
                    FROM Messages", legacyConn))
                using (var msgReader = await msgCmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await msgReader.ReadAsync(cancellationToken))
                    {
                        var msg = new EmailMessage
                        {
                            Id = msgReader.GetString(0),
                            MessageId = msgReader.GetString(1),
                            ThreadId = msgReader.IsDBNull(2) ? null : msgReader.GetString(2),
                            FolderId = msgReader.GetString(3),
                            FolderName = msgReader.GetString(4),
                            Subject = msgReader.IsDBNull(5) ? "(Nessun oggetto)" : msgReader.GetString(5),
                            From = msgReader.GetString(6),
                            FromDisplayName = msgReader.IsDBNull(7) ? "" : msgReader.GetString(7),
                            To = msgReader.GetString(8),
                            Cc = msgReader.IsDBNull(9) ? null : msgReader.GetString(9),
                            Bcc = msgReader.IsDBNull(10) ? null : msgReader.GetString(10),
                            Date = DateTimeOffset.FromUnixTimeSeconds(msgReader.GetInt64(11)),
                            HasAttachments = msgReader.GetInt32(13) == 1,
                            IsRead = msgReader.GetInt32(14) == 1,
                            SizeBytes = msgReader.GetInt64(15),
                            BodyText = msgReader.IsDBNull(16) ? null : msgReader.GetString(16),
                            BodyHtml = msgReader.IsDBNull(17) ? null : msgReader.GetString(17),
                            RawEmlPath = msgReader.GetString(18)
                        };

                        // Aggiorna RawEmlPath per puntare alla cartella unificata
                        var fileName = Path.GetFileName(msg.RawEmlPath);
                        var folderName = string.Concat(msg.FolderName.Split(Path.GetInvalidFileNameChars()));
                        var newEmlPath = Path.Combine(_emlDirectory, folderName, fileName);
                        msg.RawEmlPath = newEmlPath;

                        await InsertMessageRecordAsync(msg, cancellationToken);
                    }
                }

                // Importa allegati
                using (var attCmd = new SqliteCommand("SELECT Id, MessageId, FileName, ContentType, SizeBytes, LocalBlobPath FROM Attachments", legacyConn))
                using (var attReader = await attCmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await attReader.ReadAsync(cancellationToken))
                    {
                        var id = attReader.GetString(0);
                        var msgId = attReader.GetString(1);
                        var fileName = attReader.GetString(2);
                        var contentType = attReader.GetString(3);
                        var sizeBytes = attReader.GetInt64(4);
                        var oldBlobPath = attReader.GetString(5);

                        var safeFileName = string.Concat(fileName.Split(Path.GetInvalidFileNameChars()));
                        var newBlobPath = Path.Combine(_attachmentsDirectory, msgId, safeFileName);

                        var insertAttSql = @"
                            INSERT INTO Attachments (Id, MessageId, FileName, ContentType, SizeBytes, LocalBlobPath)
                            VALUES (@Id, @MessageId, @FileName, @ContentType, @SizeBytes, @LocalBlobPath)
                            ON CONFLICT(Id) DO UPDATE SET LocalBlobPath = excluded.LocalBlobPath;
                        ";

                        using var insCmd = new SqliteCommand(insertAttSql, _connection);
                        insCmd.Parameters.AddWithValue("@Id", id);
                        insCmd.Parameters.AddWithValue("@MessageId", msgId);
                        insCmd.Parameters.AddWithValue("@FileName", fileName);
                        insCmd.Parameters.AddWithValue("@ContentType", contentType);
                        insCmd.Parameters.AddWithValue("@SizeBytes", sizeBytes);
                        insCmd.Parameters.AddWithValue("@LocalBlobPath", newBlobPath);
                        await insCmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
            }
        }
        catch
        {
            // Migrazione legacy non bloccante
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(targetDir, Path.GetFileName(file));
            if (!File.Exists(destFile))
            {
                File.Copy(file, destFile, true);
            }
        }
        foreach (var sub in Directory.GetDirectories(sourceDir))
        {
            var destSub = Path.Combine(targetDir, Path.GetFileName(sub));
            CopyDirectory(sub, destSub);
        }
    }

    public async Task SaveFolderHierarchyAsync(IEnumerable<EmailFolder> folders, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        using var transaction = _connection!.BeginTransaction();

        var sql = @"
            INSERT INTO Folders (Id, Name, FullPath, ParentId, TotalCount, IsSystemFolder)
            VALUES (@Id, @Name, @FullPath, @ParentId, @TotalCount, @IsSystemFolder)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                FullPath = excluded.FullPath,
                ParentId = excluded.ParentId,
                TotalCount = excluded.TotalCount,
                IsSystemFolder = excluded.IsSystemFolder;
        ";

        foreach (var folder in folders)
        {
            using var cmd = new SqliteCommand(sql, _connection, transaction);
            cmd.Parameters.AddWithValue("@Id", folder.Id);
            cmd.Parameters.AddWithValue("@Name", folder.Name);
            cmd.Parameters.AddWithValue("@FullPath", folder.FullPath);
            cmd.Parameters.AddWithValue("@ParentId", (object?)folder.ParentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TotalCount", folder.TotalCount);
            cmd.Parameters.AddWithValue("@IsSystemFolder", folder.IsSystemFolder ? 1 : 0);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<List<int>> GetAvailableYearsAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var years = new List<int>();
        var sql = "SELECT DISTINCT Year FROM Messages ORDER BY Year DESC;";
        using var cmd = new SqliteCommand(sql, _connection);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            years.Add(reader.GetInt32(0));
        }
        return years;
    }

    public Task<List<EmailFolder>> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        return GetFoldersAsync(null, cancellationToken);
    }

    public async Task<List<EmailFolder>> GetFoldersAsync(int? yearFilter, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var flatList = new List<EmailFolder>();

        var sql = @"
            SELECT f.Id, f.Name, f.FullPath, f.ParentId, f.IsSystemFolder,
                   COUNT(m.Id) AS MessageCount
            FROM Folders f
            LEFT JOIN Messages m ON f.Id = m.FolderId ";

        if (yearFilter.HasValue)
        {
            sql += " AND m.Year = @Year ";
        }

        sql += @"
            GROUP BY f.Id, f.Name, f.FullPath, f.ParentId, f.IsSystemFolder
            ORDER BY f.IsSystemFolder DESC, f.Name ASC;
        ";

        using var cmd = new SqliteCommand(sql, _connection);
        if (yearFilter.HasValue)
        {
            cmd.Parameters.AddWithValue("@Year", yearFilter.Value);
        }

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            flatList.Add(new EmailFolder
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                FullPath = reader.GetString(2),
                ParentId = reader.IsDBNull(3) ? null : reader.GetString(3),
                IsSystemFolder = reader.GetInt32(4) == 1,
                FilteredCount = reader.GetInt32(5),
                TotalCount = reader.GetInt32(5)
            });
        }

        // Se non ci sono cartelle inserite ma ci sono messaggi, ricava le cartelle dai messaggi
        if (!flatList.Any())
        {
            var fallbackSql = "SELECT FolderId, FolderName, COUNT(Id) FROM Messages ";
            if (yearFilter.HasValue) fallbackSql += " WHERE Year = @Year ";
            fallbackSql += " GROUP BY FolderId, FolderName;";

            using var fbCmd = new SqliteCommand(fallbackSql, _connection);
            if (yearFilter.HasValue) fbCmd.Parameters.AddWithValue("@Year", yearFilter.Value);

            using var fbReader = await fbCmd.ExecuteReaderAsync(cancellationToken);
            while (await fbReader.ReadAsync(cancellationToken))
            {
                flatList.Add(new EmailFolder
                {
                    Id = fbReader.GetString(0),
                    Name = fbReader.GetString(1),
                    FullPath = fbReader.GetString(1),
                    FilteredCount = fbReader.GetInt32(2),
                    TotalCount = fbReader.GetInt32(2)
                });
            }
        }

        return RebuildHierarchy(flatList);
    }

    private static List<EmailFolder> RebuildHierarchy(List<EmailFolder> flatList)
    {
        var rootFolders = new List<EmailFolder>();
        var lookup = flatList.ToDictionary(f => f.Id, f => f);

        foreach (var folder in flatList)
        {
            if (!string.IsNullOrEmpty(folder.ParentId) && lookup.TryGetValue(folder.ParentId, out var parent))
            {
                parent.SubFolders.Add(folder);
            }
            else
            {
                rootFolders.Add(folder);
            }
        }

        return rootFolders;
    }

    public async Task SaveMessageAsync(EmailMessage message, byte[] rawEmlBytes, CancellationToken cancellationToken = default)
    {
        EnsureOpen();

        // 1. Assicura che la cartella esista nel DB per soddisfare la Foreign Key
        var folderEnsureSql = @"
            INSERT INTO Folders (Id, Name, FullPath, IsSystemFolder)
            VALUES (@FolderId, @FolderName, @FolderName, 1)
            ON CONFLICT(Id) DO UPDATE SET Name = excluded.Name;
        ";
        using (var folderCmd = new SqliteCommand(folderEnsureSql, _connection))
        {
            folderCmd.Parameters.AddWithValue("@FolderId", message.FolderId);
            folderCmd.Parameters.AddWithValue("@FolderName", message.FolderName);
            await folderCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 2. Salva file EML su disco
        var sanitizedFolder = string.Concat(message.FolderName.Split(Path.GetInvalidFileNameChars()));
        var targetEmlFolder = Path.Combine(_emlDirectory, sanitizedFolder);
        Directory.CreateDirectory(targetEmlFolder);

        var emlFileName = $"{message.Id}.eml";
        var emlFilePath = Path.Combine(targetEmlFolder, emlFileName);
        await File.WriteAllBytesAsync(emlFilePath, rawEmlBytes, cancellationToken);
        message.RawEmlPath = emlFilePath;

        // 3. Salva metadati nel DB SQLite
        await InsertMessageRecordAsync(message, cancellationToken);

        // 4. Salva ed estrai allegati se presenti
        if (message.Attachments.Any())
        {
            var msgAttDir = Path.Combine(_attachmentsDirectory, message.Id);
            try
            {
                Directory.CreateDirectory(msgAttDir);
            }
            catch { }

            List<MimeKit.MimePart>? mimeAttachments = null;
            try
            {
                using var emlStream = new MemoryStream(rawEmlBytes);
                var mime = await MimeKit.MimeMessage.LoadAsync(emlStream, cancellationToken);
                mimeAttachments = mime.Attachments.OfType<MimeKit.MimePart>().ToList();
            }
            catch { }

            for (int i = 0; i < message.Attachments.Count; i++)
            {
                var att = message.Attachments[i];
                var safeFileName = SanitizeAndTruncateFileName(att.FileName, 60, $"allegato_{i + 1}.bin");
                var attPath = Path.Combine(msgAttDir, safeFileName);
                att.LocalBlobPath = attPath;

                if (mimeAttachments != null && i < mimeAttachments.Count)
                {
                    try
                    {
                        using var attFs = File.Create(attPath);
                        mimeAttachments[i].Content?.DecodeTo(attFs);
                    }
                    catch
                    {
                        // Fallback in caso di problemi di percorso o caratteri speciali
                        try
                        {
                            var fallbackPath = Path.Combine(msgAttDir, $"att_{i + 1}_{Guid.NewGuid().ToString().Substring(0, 4)}.bin");
                            using var attFs = File.Create(fallbackPath);
                            mimeAttachments[i].Content?.DecodeTo(attFs);
                            att.LocalBlobPath = fallbackPath;
                        }
                        catch { }
                    }
                }

                try
                {
                    var attSql = @"
                        INSERT INTO Attachments (Id, MessageId, FileName, ContentType, SizeBytes, LocalBlobPath)
                        VALUES (@Id, @MessageId, @FileName, @ContentType, @SizeBytes, @LocalBlobPath)
                        ON CONFLICT(Id) DO UPDATE SET LocalBlobPath = excluded.LocalBlobPath;
                    ";

                    using var attCmd = new SqliteCommand(attSql, _connection);
                    attCmd.Parameters.AddWithValue("@Id", att.Id);
                    attCmd.Parameters.AddWithValue("@MessageId", message.Id);
                    attCmd.Parameters.AddWithValue("@FileName", att.FileName);
                    attCmd.Parameters.AddWithValue("@ContentType", att.ContentType);
                    attCmd.Parameters.AddWithValue("@SizeBytes", att.SizeBytes);
                    attCmd.Parameters.AddWithValue("@LocalBlobPath", att.LocalBlobPath);

                    await attCmd.ExecuteNonQueryAsync(cancellationToken);
                }
                catch { }
            }
        }
    }

    public static string SanitizeAndTruncateFileName(string? fileName, int maxNameLength = 60, string defaultName = "allegato.bin")
    {
        if (string.IsNullOrWhiteSpace(fileName)) return defaultName;

        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var cleanedChars = fileName
            .Select(c => invalidChars.Contains(c) || c < 32 || c == '"' || c == '<' || c == '>' || c == '|' || c == ':' || c == '*' || c == '?' || c == '\\' || c == '/' ? '_' : c)
            .ToArray();

        var cleaned = new string(cleanedChars).Trim().Trim('.', ' ', '_');
        if (string.IsNullOrWhiteSpace(cleaned)) return defaultName;

        var ext = Path.GetExtension(cleaned);
        if (ext.Length > 15) ext = "";

        var nameWithoutExt = Path.GetFileNameWithoutExtension(cleaned);
        if (string.IsNullOrWhiteSpace(nameWithoutExt)) nameWithoutExt = "allegato";

        if (nameWithoutExt.Length > maxNameLength)
        {
            nameWithoutExt = nameWithoutExt.Substring(0, maxNameLength).TrimEnd('.', ' ', '_');
        }

        var finalName = $"{nameWithoutExt}{ext}";
        return string.IsNullOrWhiteSpace(finalName) ? defaultName : finalName;
    }

    private async Task InsertMessageRecordAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var msgSql = @"
            INSERT INTO Messages (
                Id, MessageId, ThreadId, FolderId, FolderName, Subject,
                FromAddress, FromDisplayName, ToAddress, CcAddress, BccAddress,
                DateUnix, DateString, Year, HasAttachments, IsRead, SizeBytes,
                BodyText, BodyHtml, RawEmlPath
            ) VALUES (
                @Id, @MessageId, @ThreadId, @FolderId, @FolderName, @Subject,
                @FromAddress, @FromDisplayName, @ToAddress, @CcAddress, @BccAddress,
                @DateUnix, @DateString, @Year, @HasAttachments, @IsRead, @SizeBytes,
                @BodyText, @BodyHtml, @RawEmlPath
            ) ON CONFLICT(Id) DO UPDATE SET
                Subject = excluded.Subject,
                FolderId = excluded.FolderId,
                FolderName = excluded.FolderName,
                BodyText = excluded.BodyText,
                BodyHtml = excluded.BodyHtml,
                RawEmlPath = excluded.RawEmlPath,
                Year = excluded.Year;
        ";

        using var cmd = new SqliteCommand(msgSql, _connection);
        cmd.Parameters.AddWithValue("@Id", message.Id);
        cmd.Parameters.AddWithValue("@MessageId", message.MessageId);
        cmd.Parameters.AddWithValue("@ThreadId", (object?)message.ThreadId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FolderId", message.FolderId);
        cmd.Parameters.AddWithValue("@FolderName", message.FolderName);
        cmd.Parameters.AddWithValue("@Subject", (object?)message.Subject ?? "(Nessun oggetto)");
        cmd.Parameters.AddWithValue("@FromAddress", message.From);
        cmd.Parameters.AddWithValue("@FromDisplayName", message.FromDisplayName);
        cmd.Parameters.AddWithValue("@ToAddress", message.To);
        cmd.Parameters.AddWithValue("@CcAddress", (object?)message.Cc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@BccAddress", (object?)message.Bcc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DateUnix", message.Date.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("@DateString", message.Date.ToString("o"));
        cmd.Parameters.AddWithValue("@Year", message.Date.Year);
        cmd.Parameters.AddWithValue("@HasAttachments", message.HasAttachments ? 1 : 0);
        cmd.Parameters.AddWithValue("@IsRead", message.IsRead ? 1 : 0);
        cmd.Parameters.AddWithValue("@SizeBytes", message.SizeBytes);
        cmd.Parameters.AddWithValue("@BodyText", (object?)message.BodyText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@BodyHtml", (object?)message.BodyHtml ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RawEmlPath", message.RawEmlPath);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<List<EmailMessage>> GetMessagesInFolderAsync(
        string folderId,
        int skip = 0,
        int take = 100,
        string? searchKeyword = null,
        CancellationToken cancellationToken = default)
    {
        return GetMessagesInFolderAsync(folderId, skip, take, searchKeyword, null, cancellationToken);
    }

    public async Task<List<EmailMessage>> GetMessagesInFolderAsync(
        string folderId,
        int skip,
        int take,
        string? searchKeyword,
        int? yearFilter,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var list = new List<EmailMessage>();

        var sql = @"
            SELECT Id, MessageId, ThreadId, FolderId, FolderName, Subject,
                   FromAddress, FromDisplayName, ToAddress, CcAddress, BccAddress,
                   DateUnix, DateString, HasAttachments, IsRead, SizeBytes, RawEmlPath
            FROM Messages
            WHERE (FolderId = @FolderId OR @FolderId = '')
        ";

        if (yearFilter.HasValue)
        {
            sql += " AND Year = @Year ";
        }

        if (!string.IsNullOrWhiteSpace(searchKeyword))
        {
            sql += " AND (Subject LIKE @Search OR FromAddress LIKE @Search OR FromDisplayName LIKE @Search OR ToAddress LIKE @Search)";
        }

        sql += " ORDER BY DateUnix DESC LIMIT @Take OFFSET @Skip;";

        using var cmd = new SqliteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@FolderId", folderId);
        cmd.Parameters.AddWithValue("@Skip", skip);
        cmd.Parameters.AddWithValue("@Take", take);

        if (yearFilter.HasValue)
        {
            cmd.Parameters.AddWithValue("@Year", yearFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchKeyword))
        {
            cmd.Parameters.AddWithValue("@Search", $"%{searchKeyword}%");
        }

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var msg = new EmailMessage
            {
                Id = reader.GetString(0),
                MessageId = reader.GetString(1),
                ThreadId = reader.IsDBNull(2) ? null : reader.GetString(2),
                FolderId = reader.GetString(3),
                FolderName = reader.GetString(4),
                Subject = reader.IsDBNull(5) ? "(Nessun oggetto)" : reader.GetString(5),
                From = reader.GetString(6),
                FromDisplayName = reader.IsDBNull(7) ? "" : reader.GetString(7),
                To = reader.GetString(8),
                Cc = reader.IsDBNull(9) ? null : reader.GetString(9),
                Bcc = reader.IsDBNull(10) ? null : reader.GetString(10),
                Date = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(11)),
                HasAttachments = reader.GetInt32(13) == 1,
                IsRead = reader.GetInt32(14) == 1,
                SizeBytes = reader.GetInt64(15),
                RawEmlPath = reader.GetString(16)
            };

            list.Add(msg);
        }

        return list;
    }

    public Task<int> GetMessageCountInFolderAsync(string folderId, string? searchKeyword = null, CancellationToken cancellationToken = default)
    {
        return GetMessageCountInFolderAsync(folderId, searchKeyword, null, cancellationToken);
    }

    public async Task<int> GetMessageCountInFolderAsync(string folderId, string? searchKeyword, int? yearFilter, CancellationToken cancellationToken = default)
    {
        EnsureOpen();

        var sql = "SELECT COUNT(Id) FROM Messages WHERE (FolderId = @FolderId OR @FolderId = '')";
        if (yearFilter.HasValue)
        {
            sql += " AND Year = @Year ";
        }
        if (!string.IsNullOrWhiteSpace(searchKeyword))
        {
            sql += " AND (Subject LIKE @Search OR FromAddress LIKE @Search OR FromDisplayName LIKE @Search OR ToAddress LIKE @Search)";
        }

        using var cmd = new SqliteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@FolderId", folderId);
        if (yearFilter.HasValue)
        {
            cmd.Parameters.AddWithValue("@Year", yearFilter.Value);
        }
        if (!string.IsNullOrWhiteSpace(searchKeyword))
        {
            cmd.Parameters.AddWithValue("@Search", $"%{searchKeyword}%");
        }

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task<EmailMessage?> GetMessageDetailsAsync(string messageId, CancellationToken cancellationToken = default)
    {
        EnsureOpen();

        var sql = @"
            SELECT Id, MessageId, ThreadId, FolderId, FolderName, Subject,
                   FromAddress, FromDisplayName, ToAddress, CcAddress, BccAddress,
                   DateUnix, DateString, HasAttachments, IsRead, SizeBytes,
                   BodyText, BodyHtml, RawEmlPath
            FROM Messages
            WHERE Id = @Id;
        ";

        using var cmd = new SqliteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@Id", messageId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var msg = new EmailMessage
        {
            Id = reader.GetString(0),
            MessageId = reader.GetString(1),
            ThreadId = reader.IsDBNull(2) ? null : reader.GetString(2),
            FolderId = reader.GetString(3),
            FolderName = reader.GetString(4),
            Subject = reader.IsDBNull(5) ? "(Nessun oggetto)" : reader.GetString(5),
            From = reader.GetString(6),
            FromDisplayName = reader.IsDBNull(7) ? "" : reader.GetString(7),
            To = reader.GetString(8),
            Cc = reader.IsDBNull(9) ? null : reader.GetString(9),
            Bcc = reader.IsDBNull(10) ? null : reader.GetString(10),
            Date = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(11)),
            HasAttachments = reader.GetInt32(13) == 1,
            IsRead = reader.GetInt32(14) == 1,
            SizeBytes = reader.GetInt64(15),
            BodyText = reader.IsDBNull(16) ? null : reader.GetString(16),
            BodyHtml = reader.IsDBNull(17) ? null : reader.GetString(17),
            RawEmlPath = reader.GetString(18)
        };

        // Carica allegati
        var attSql = "SELECT Id, MessageId, FileName, ContentType, SizeBytes, LocalBlobPath FROM Attachments WHERE MessageId = @MessageId;";
        using var attCmd = new SqliteCommand(attSql, _connection);
        attCmd.Parameters.AddWithValue("@MessageId", messageId);

        using var attReader = await attCmd.ExecuteReaderAsync(cancellationToken);
        while (await attReader.ReadAsync(cancellationToken))
        {
            msg.Attachments.Add(new AttachmentInfo
            {
                Id = attReader.GetString(0),
                MessageId = attReader.GetString(1),
                FileName = attReader.GetString(2),
                ContentType = attReader.GetString(3),
                SizeBytes = attReader.GetInt64(4),
                LocalBlobPath = attReader.GetString(5)
            });
        }

        return msg;
    }

    public async Task<byte[]> GetRawEmlBytesAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var msg = await GetMessageDetailsAsync(messageId, cancellationToken);
        if (msg == null || string.IsNullOrEmpty(msg.RawEmlPath) || !File.Exists(msg.RawEmlPath))
        {
            throw new FileNotFoundException($"File EML non trovato per il messaggio {messageId}");
        }

        return await File.ReadAllBytesAsync(msg.RawEmlPath, cancellationToken);
    }

    public async Task<byte[]> GetAttachmentBytesAsync(string attachmentId, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var sql = "SELECT LocalBlobPath FROM Attachments WHERE Id = @Id;";
        using var cmd = new SqliteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@Id", attachmentId);

        var pathObj = await cmd.ExecuteScalarAsync(cancellationToken);
        var path = pathObj?.ToString();

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            throw new FileNotFoundException($"File allegato non trovato per ID {attachmentId}");
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    public async Task<int> GetTotalMessageCountAsync(int? yearFilter = null, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var sql = "SELECT COUNT(Id) FROM Messages";
        if (yearFilter.HasValue) sql += " WHERE Year = @Year";
        sql += ";";

        using var cmd = new SqliteCommand(sql, _connection);
        if (yearFilter.HasValue) cmd.Parameters.AddWithValue("@Year", yearFilter.Value);

        var res = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(res);
    }

    public async Task<long> GetTotalSizeBytesAsync(int? yearFilter = null, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var sql = "SELECT IFNULL(SUM(SizeBytes), 0) FROM Messages";
        if (yearFilter.HasValue) sql += " WHERE Year = @Year";
        sql += ";";

        using var cmd = new SqliteCommand(sql, _connection);
        if (yearFilter.HasValue) cmd.Parameters.AddWithValue("@Year", yearFilter.Value);

        var res = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(res);
    }

    public async Task<(int TotalCount, long TotalBytes)> ExtractAttachmentsAsync(
        string targetDirectory,
        AttachmentFilterOptions filterOptions,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        Directory.CreateDirectory(targetDirectory);

        var messagesSql = @"
            SELECT Id, FolderId, FolderName, Subject, DateString, Year, RawEmlPath
            FROM Messages
            WHERE 1=1
        ";
        if (!string.IsNullOrEmpty(filterOptions.FolderId))
        {
            messagesSql += " AND FolderId = @FolderId";
        }
        if (filterOptions.Year.HasValue)
        {
            messagesSql += " AND Year = @Year";
        }

        var candidateMessages = new List<(string Id, string FolderId, string FolderName, string Subject, string DateString, int Year, string RawEmlPath)>();

        using (var cmd = new SqliteCommand(messagesSql, _connection))
        {
            if (!string.IsNullOrEmpty(filterOptions.FolderId))
                cmd.Parameters.AddWithValue("@FolderId", filterOptions.FolderId);
            if (filterOptions.Year.HasValue)
                cmd.Parameters.AddWithValue("@Year", filterOptions.Year.Value);

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidateMessages.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? "Email" : reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt32(5),
                    reader.GetString(6)
                ));
            }
        }

        var progressState = new BackupProgress
        {
            Phase = BackupPhase.Exporting,
            TotalMessages = candidateMessages.Count,
            StatusMessage = $"Scansione allegati su {candidateMessages.Count} messaggi..."
        };
        progress?.Report(progressState);

        int extractedCount = 0;
        long extractedBytes = 0;
        int processedMsgs = 0;

        foreach (var msg in candidateMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processedMsgs++;

            var destDir = targetDirectory;
            if (filterOptions.OrganizeInSubfolders)
            {
                var safeFolder = string.Concat(msg.FolderName.Split(Path.GetInvalidFileNameChars()));
                destDir = Path.Combine(targetDirectory, $"{msg.Year}", safeFolder);
                Directory.CreateDirectory(destDir);
            }

            var attSql = "SELECT Id, FileName, SizeBytes, LocalBlobPath FROM Attachments WHERE MessageId = @MessageId;";
            var dbAttachments = new List<(string Id, string FileName, long SizeBytes, string LocalBlobPath)>();

            using (var attCmd = new SqliteCommand(attSql, _connection))
            {
                attCmd.Parameters.AddWithValue("@MessageId", msg.Id);
                using var attReader = await attCmd.ExecuteReaderAsync(cancellationToken);
                while (await attReader.ReadAsync(cancellationToken))
                {
                    dbAttachments.Add((
                        attReader.GetString(0),
                        attReader.GetString(1),
                        attReader.GetInt64(2),
                        attReader.GetString(3)
                    ));
                }
            }

            if (dbAttachments.Any())
            {
                foreach (var att in dbAttachments)
                {
                    if (!filterOptions.MatchesFilter(att.FileName, att.SizeBytes))
                        continue;

                    byte[] bytes;
                    if (File.Exists(att.LocalBlobPath))
                    {
                        bytes = await File.ReadAllBytesAsync(att.LocalBlobPath, cancellationToken);
                    }
                    else
                    {
                        bytes = await GetAttachmentBytesAsync(att.Id, cancellationToken);
                    }

                    await SaveExtractedFileAsync(destDir, att.FileName, msg.Subject, msg.DateString, bytes, cancellationToken);
                    extractedCount++;
                    extractedBytes += bytes.Length;
                }
            }
            else if (!string.IsNullOrEmpty(msg.RawEmlPath) && File.Exists(msg.RawEmlPath))
            {
                // Fallback: estrai direttamente dal file .EML
                try
                {
                    using var emlStream = File.OpenRead(msg.RawEmlPath);
                    var mime = await MimeKit.MimeMessage.LoadAsync(emlStream, cancellationToken);

                    foreach (var entity in mime.BodyParts)
                    {
                        if (entity is MimeKit.MimePart part)
                        {
                            var isTextBody = (part.ContentType.MimeType.Equals("text/plain", StringComparison.OrdinalIgnoreCase) || 
                                              part.ContentType.MimeType.Equals("text/html", StringComparison.OrdinalIgnoreCase)) &&
                                             string.IsNullOrWhiteSpace(part.FileName);

                            if (isTextBody) continue;

                            var fileName = !string.IsNullOrWhiteSpace(part.FileName)
                                ? part.FileName
                                : (!string.IsNullOrWhiteSpace(part.ContentId)
                                    ? $"{part.ContentId.Trim('<', '>')}.{part.ContentType.MediaSubtype}"
                                    : $"allegato_{extractedCount + 1}.{part.ContentType.MediaSubtype}");

                            byte[] bytes;
                            using (var ms = new MemoryStream())
                            {
                                part.Content?.DecodeTo(ms);
                                bytes = ms.ToArray();
                            }

                            if (bytes.Length > 0 && filterOptions.MatchesFilter(fileName, bytes.Length))
                            {
                                await SaveExtractedFileAsync(destDir, fileName, msg.Subject, msg.DateString, bytes, cancellationToken);
                                extractedCount++;
                                extractedBytes += bytes.Length;
                            }
                        }
                    }
                }
                catch { }
            }

            if (processedMsgs % 10 == 0 || processedMsgs == candidateMessages.Count)
            {
                progressState.ProcessedMessages = processedMsgs;
                progressState.BytesDownloaded = extractedBytes;
                progressState.StatusMessage = $"Scansione ({processedMsgs}/{candidateMessages.Count}) - Allegati estratti: {extractedCount} ({FormatStorageBytes(extractedBytes)})";
                progress?.Report(progressState);
            }
        }

        progressState.Phase = BackupPhase.Completed;
        progressState.StatusMessage = $"Estrazione completata: {extractedCount} allegati estratti ({FormatStorageBytes(extractedBytes)}).";
        progress?.Report(progressState);

        return (extractedCount, extractedBytes);
    }

    private static string FormatStorageBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    private static async Task SaveExtractedFileAsync(string destDir, string originalFileName, string emailSubject, string emailDate, byte[] bytes, CancellationToken cancellationToken)
    {
        var safeDate = "";
        if (DateTime.TryParse(emailDate, out var dt))
        {
            safeDate = dt.ToString("yyyyMMdd_");
        }

        var safeSubject = SanitizeAndTruncateFileName(emailSubject, 30, "oggetto");
        var safeFileName = SanitizeAndTruncateFileName(originalFileName, 50, "allegato.bin");

        var finalFileName = $"{safeDate}{safeSubject}_{safeFileName}";
        var targetFilePath = Path.Combine(destDir, finalFileName);

        int counter = 1;
        while (File.Exists(targetFilePath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(finalFileName);
            var ext = Path.GetExtension(finalFileName);
            targetFilePath = Path.Combine(destDir, $"{nameWithoutExt}_{counter}{ext}");
            counter++;
        }

        await File.WriteAllBytesAsync(targetFilePath, bytes, cancellationToken);
    }

    public async Task<HashSet<string>> GetExistingMessageIdsAsync(string? folderId = null, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sql = "SELECT Id FROM Messages";
        if (!string.IsNullOrEmpty(folderId))
        {
            sql += " WHERE FolderId = @FolderId";
        }

        using var cmd = new SqliteCommand(sql, _connection);
        if (!string.IsNullOrEmpty(folderId))
        {
            cmd.Parameters.AddWithValue("@FolderId", folderId);
        }

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            set.Add(reader.GetString(0));
        }

        return set;
    }

    public async Task<int> DeleteYearDataAsync(int year, CancellationToken cancellationToken = default)
    {
        EnsureOpen();

        // 1. Trova tutti i messaggi dell'anno da eliminare
        var messagesToDelete = new List<(string Id, string FolderId, string RawEmlPath)>();
        var selectSql = "SELECT Id, FolderId, RawEmlPath FROM Messages WHERE Year = @Year;";
        using (var cmd = new SqliteCommand(selectSql, _connection))
        {
            cmd.Parameters.AddWithValue("@Year", year);
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                messagesToDelete.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2)
                ));
            }
        }

        if (!messagesToDelete.Any()) return 0;

        // 2. Elimina i file fisici su disco (.eml e cartelle allegati)
        foreach (var msg in messagesToDelete)
        {
            try
            {
                if (!string.IsNullOrEmpty(msg.RawEmlPath) && File.Exists(msg.RawEmlPath))
                {
                    File.Delete(msg.RawEmlPath);
                }

                var attDir = Path.Combine(_attachmentsDirectory, msg.Id);
                if (Directory.Exists(attDir))
                {
                    Directory.Delete(attDir, true);
                }
            }
            catch { }
        }

        // 3. Elimina i record dal database SQLite
        using var transaction = _connection!.BeginTransaction();

        using (var delAttCmd = new SqliteCommand(@"
            DELETE FROM Attachments WHERE MessageId IN (
                SELECT Id FROM Messages WHERE Year = @Year
            );", _connection, transaction))
        {
            delAttCmd.Parameters.AddWithValue("@Year", year);
            await delAttCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var delMsgCmd = new SqliteCommand("DELETE FROM Messages WHERE Year = @Year;", _connection, transaction))
        {
            delMsgCmd.Parameters.AddWithValue("@Year", year);
            await delMsgCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        // 4. Esegui VACUUM per compattare il database SQLite
        try
        {
            using var vacuumCmd = new SqliteCommand("VACUUM;", _connection);
            await vacuumCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch { }

        return messagesToDelete.Count;
    }

    private void EnsureOpen()
    {
        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
        {
            throw new InvalidOperationException("Il database di archiviazione locale non è inizializzato.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            try
            {
                using var checkpointCmd = new SqliteCommand("PRAGMA wal_checkpoint(TRUNCATE);", _connection);
                await checkpointCmd.ExecuteNonQueryAsync();
            }
            catch { }

            await _connection.CloseAsync();
            _connection.Dispose();
            _connection = null;

            if (_isRemoteStorage && !string.IsNullOrEmpty(_remoteDbPath) && !string.IsNullOrEmpty(_activeDbPath) && File.Exists(_activeDbPath))
            {
                try
                {
                    File.Copy(_activeDbPath, _remoteDbPath, true);
                }
                catch { }
            }
        }
    }
}
