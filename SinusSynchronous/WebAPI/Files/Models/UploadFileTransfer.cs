using SinusSynchronous.API.Dto.Files;

namespace SinusSynchronous.WebAPI.Files.Models;

public class UploadFileTransfer : FileTransfer
{
    public UploadFileTransfer(UploadFileDto dto) : base(dto)
    {
    }

    public string LocalFile { get; set; } = string.Empty;
    public override long Total { get; set; }
}