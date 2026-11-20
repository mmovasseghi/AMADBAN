namespace Amadban.Data;

public sealed record DocumentUpload(string Name, string Type, byte[] Content)
{
    public static async Task<DocumentUpload?> ReadAsync(IFormFile? file)
    {
        if (file is null || file.Length is <= 0 or > 10 * 1024 * 1024) return null;
        using var output = new MemoryStream((int)file.Length);
        await file.CopyToAsync(output);
        var bytes = output.ToArray();
        var type = bytes.AsSpan().StartsWith("%PDF-"u8) ? "application/pdf" :
            bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff }) ? "image/jpeg" :
            bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }) ? "image/png" : null;
        if (type is null) return null;
        var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        if (name.Length > 180) name = name[..180];
        if (string.IsNullOrWhiteSpace(name)) name = "attachment";
        return new(name, type, bytes);
    }
}
