using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Playground.ImageCrypto;

/// <summary>
/// 文件格式：Magic(6) + Salt(16) + Nonce(12) + Tag(16) + Ciphertext
/// 明文 = ExtLen(u16 LE) + Ext(UTF8) + 原图字节
/// 密钥 = PBKDF2-SHA256(password, salt, 200_000) → 32 bytes，AES-GCM
/// </summary>
internal static class ImageCryptoService
{
    private static readonly byte[] Magic = "PMIMG1"u8.ToArray();
    private const int SaltLen = 16;
    private const int NonceLen = 12;
    private const int TagLen = 16;
    private const int KeyLen = 32;
    private const int Iterations = 200_000;

    public static byte[] Encrypt(byte[] imageBytes, string extension, string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("密码不能为空。");
        extension = NormalizeExt(extension);
        var extBytes = Encoding.UTF8.GetBytes(extension);
        if (extBytes.Length > ushort.MaxValue)
            throw new ArgumentException("扩展名过长。");

        var plain = new byte[2 + extBytes.Length + imageBytes.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(plain.AsSpan(0, 2), (ushort)extBytes.Length);
        extBytes.CopyTo(plain, 2);
        imageBytes.CopyTo(plain, 2 + extBytes.Length);

        var salt = RandomNumberGenerator.GetBytes(SaltLen);
        var nonce = RandomNumberGenerator.GetBytes(NonceLen);
        var key = DeriveKey(password, salt);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagLen];
        using (var aes = new AesGcm(key, TagLen))
            aes.Encrypt(nonce, plain, cipher, tag);

        var output = new byte[Magic.Length + SaltLen + NonceLen + TagLen + cipher.Length];
        var o = 0;
        Magic.CopyTo(output, o); o += Magic.Length;
        salt.CopyTo(output, o); o += SaltLen;
        nonce.CopyTo(output, o); o += NonceLen;
        tag.CopyTo(output, o); o += TagLen;
        cipher.CopyTo(output, o);
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(plain);
        return output;
    }

    public static (byte[] ImageBytes, string Extension) Decrypt(byte[] blob, string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("密码不能为空。");
        var header = Magic.Length + SaltLen + NonceLen + TagLen;
        if (blob.Length < header + 1)
            throw new InvalidOperationException("文件太短，不是有效的加密图。");
        if (!blob.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidOperationException("不是本程序的加密格式（Magic 不匹配）。");

        var o = Magic.Length;
        var salt = blob.AsSpan(o, SaltLen).ToArray(); o += SaltLen;
        var nonce = blob.AsSpan(o, NonceLen).ToArray(); o += NonceLen;
        var tag = blob.AsSpan(o, TagLen).ToArray(); o += TagLen;
        var cipher = blob.AsSpan(o).ToArray();

        var key = DeriveKey(password, salt);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, TagLen);
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new InvalidOperationException("解密失败：密码错误或文件已被篡改。");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        if (plain.Length < 2)
            throw new InvalidOperationException("明文损坏。");
        var extLen = BinaryPrimitives.ReadUInt16LittleEndian(plain.AsSpan(0, 2));
        if (2 + extLen > plain.Length)
            throw new InvalidOperationException("明文损坏（扩展名长度）。");
        var ext = Encoding.UTF8.GetString(plain, 2, extLen);
        var image = plain.AsSpan(2 + extLen).ToArray();
        CryptographicOperations.ZeroMemory(plain);
        return (image, NormalizeExt(ext));
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeyLen);
    }

    private static string NormalizeExt(string extension)
    {
        extension = (extension ?? "").Trim();
        if (extension.Length == 0) return ".bin";
        if (!extension.StartsWith('.')) extension = "." + extension;
        return extension.ToLowerInvariant();
    }
}
