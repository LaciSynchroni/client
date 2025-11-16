using LaciSynchroni.Common.Dto.Files;

namespace LaciSynchroni.WebAPI.Files.Models;

public class DownloadFileTransfer : FileTransfer
{
    public DownloadFileTransfer(DownloadFileDto dto, int serverIndex) : base(dto, serverIndex)
    {
    }

    public override bool CanBeTransferred => Dto.FileExists && !Dto.IsForbidden && Dto.Size > 0;
    public Uri DownloadUri => new(string.IsNullOrEmpty(Dto.DirectDownloadUrl) ? (string.IsNullOrEmpty(Dto.CDNDownloadUrl) ? Dto.Url : Dto.CDNDownloadUrl) : Dto.DirectDownloadUrl);
    public bool IsDirectDownload => !string.IsNullOrEmpty(Dto.DirectDownloadUrl) || !string.IsNullOrEmpty(Dto.CDNDownloadUrl);
    public override long Total
    {
        set
        {
            // nothing to set
        }
        get => Dto.Size;
    }

    public long TotalRaw => Dto.RawSize;
    private DownloadFileDto Dto => (DownloadFileDto)TransferDto;
}