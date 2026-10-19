using System.Security.Cryptography;
using System.Text;

namespace CifraToken.Core;

/// <summary>
/// Cifra com frase-senha no formato do "openssl enc" (cabeçalho "Salted__" + sal de 8 bytes),
/// com AES-256-CBC e chave derivada por PBKDF2-HMAC-SHA512 com 1.000.000 de iterações.
/// São os parâmetros do arquivo ".enc" do Quimera (Manual do Quimera v1.0, Apêndice C), então os
/// dois programas e o openssl abrem os arquivos uns dos outros:
///   openssl enc -aes-256-cbc -d -in ARQ.enc -out ARQ -pbkdf2 -iter 1000000 -md sha512
/// </summary>
public static class SenhaOpenSsl
{
    public const int Iteracoes = 1_000_000;
    static readonly byte[] Cabecalho = Encoding.ASCII.GetBytes("Salted__");

    public static byte[] Cifrar(byte[] conteudo, string senha)
    {
        var sal = RandomNumberGenerator.GetBytes(8);
        using var aes = Criar(senha, sal);
        var cifrado = aes.EncryptCbc(conteudo, aes.IV, PaddingMode.PKCS7);
        return Cabecalho.Concat(sal).Concat(cifrado).ToArray();
    }

    public static byte[] Decifrar(byte[] enc, string senha)
    {
        if (!EhFormatoOpenSsl(enc))
            throw new CryptographicException("O arquivo não está no formato do openssl enc (falta o cabeçalho \"Salted__\").");
        using var aes = Criar(senha, enc[8..16]);
        try
        {
            return aes.DecryptCbc(enc.AsSpan(16), aes.IV, PaddingMode.PKCS7);
        }
        catch (CryptographicException)
        {
            throw new CryptographicException("Senha incorreta ou arquivo corrompido.");
        }
    }

    public static bool EhFormatoOpenSsl(byte[] dados) =>
        dados.Length >= 32 && dados.AsSpan(0, 8).SequenceEqual(Cabecalho);

    static Aes Criar(string senha, byte[] sal)
    {
        var material = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(senha), sal, Iteracoes, HashAlgorithmName.SHA512, 48);
        var aes = Aes.Create();
        aes.Key = material[..32];
        aes.IV = material[32..];
        return aes;
    }

    /// <summary>Avaliação simples da força da frase-senha, de 0 a 4.</summary>
    public static int Forca(string senha)
    {
        if (senha.Length < 8) return 0;
        var classes = new[] { senha.Any(char.IsLower), senha.Any(char.IsUpper), senha.Any(char.IsDigit),
            senha.Any(c => !char.IsLetterOrDigit(c)) }.Count(b => b);
        var nota = (senha.Length >= 14 ? 2 : senha.Length >= 10 ? 1 : 0) + (classes >= 3 ? 2 : classes >= 2 ? 1 : 0);
        return Math.Min(nota, 4);
    }
}
