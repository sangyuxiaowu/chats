﻿using Chats.BE.Controllers.Chats.Messages.Dtos;
using Chats.BE.DB;
using Chats.BE.DB.Enums;
using Chats.BE.Services.UrlEncryption;
using Microsoft.EntityFrameworkCore;
using OpenAI.Chat;

namespace Chats.BE.Services.FileServices;

public class FileUrlProvider(ChatsDB db, FileServiceFactory fileServiceFactory, IUrlEncryptionService urlEncryptionService)
{
    public async Task<ChatMessageContentPart> CreateOpenAIPart(StepContentFile? mcFile, CancellationToken cancellationToken)
    {
        if (mcFile == null)
        {
            if (mcFile?.File?.FileService == null || mcFile.File.FileContentType == null)
            {
                throw new ArgumentNullException(nameof(mcFile));
            }
            throw new ArgumentNullException(nameof(mcFile));
        }

        DB.File file = mcFile.File;

        IFileService fs = fileServiceFactory.Create(file.FileService);
        if (file.FileService.FileServiceTypeId == (byte)DBFileServiceType.Local)
        {
            MemoryStream ms = new();
            using Stream stream = await fs.Download(file.StorageKey, cancellationToken);
            await stream.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;

            BinaryData binaryData = BinaryData.FromStream(ms);
            return ChatMessageContentPart.CreateImagePart(binaryData, file.FileContentType.ContentType);
        }
        else
        {
            Uri url = fs.CreateDownloadUrl(CreateDownloadUrlRequest.FromFile(file));
            return ChatMessageContentPart.CreateImagePart(url);
        }
    }

    public FileDto CreateFileDto(DB.File file, bool tryWithUrl = true)
    {
        if (file.FileContentType == null)
        {
            throw new InvalidOperationException("Unable to convert file to DTO: FileContentType is null.");
        }

        string? downloadUrl = null;
        if (tryWithUrl && file.FileService != null)
        {
            IFileService fs = fileServiceFactory.Create(file.FileService);
            downloadUrl = fs.CreateDownloadUrl(CreateDownloadUrlRequest.FromFile(file)).ToString();
        }

        return new FileDto()
        {
            Id = urlEncryptionService.EncryptFileId(file.Id),
            FileName = file.FileName,
            ContentType = file.FileContentType.ContentType,
            Url = downloadUrl,
        };
    }

    public async Task<StepContent> CreateFileContent(string encryptedFileId, CancellationToken cancellationToken)
    {
        int fileId = urlEncryptionService.DecryptFileId(encryptedFileId);
        DB.File file = await db.Files
            .Include(x => x.FileContentType)
            .Include(x => x.FileService)
            .Include(x => x.FileImageInfo)
            .FirstAsync(x => x.Id == fileId, cancellationToken);
        return StepContent.FromFile(file);
    }
}
