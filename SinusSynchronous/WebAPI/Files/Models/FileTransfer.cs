using SinusSynchronous.API.Dto.Files;

namespace SinusSynchronous.WebAPI.Files.Models;

public abstract class FileTransfer
{
    protected readonly ITransferFileDto TransferDto;
    public readonly int ServerIndex;

    protected FileTransfer(ITransferFileDto transferDto, int serverIndex)
    {
        TransferDto = transferDto;
        ServerIndex = serverIndex;
    }

    public virtual bool CanBeTransferred => !TransferDto.IsForbidden && (TransferDto is not DownloadFileDto dto || dto.FileExists);
    public string ForbiddenBy => TransferDto.ForbiddenBy;
    public string Hash => TransferDto.Hash;
    public bool IsForbidden => TransferDto.IsForbidden;
    public bool IsInTransfer => Transferred != Total && Transferred > 0;
    public bool IsTransferred => Transferred == Total;
    public abstract long Total { get; set; }
    public long Transferred { get; set; } = 0;

    public override string ToString()
    {
        return Hash;
    }
}