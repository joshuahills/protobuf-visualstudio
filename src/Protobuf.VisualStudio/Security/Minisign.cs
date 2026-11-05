using System.Text;

namespace Protobuf.VisualStudio.Security;

/// <summary>
/// Verifies minisign signatures (https://jedisct1.github.io/minisign/): an Ed25519 signature over the file (or, for
/// the "ED" algorithm, over its BLAKE2b-512 hash), plus a global signature binding the trusted comment.
/// </summary>
internal static class Minisign
{
    /// <summary>Buf's release signing key, from https://buf.build/docs/cli/installation/.</summary>
    public const string BufPublicKey = "RWQ/i9xseZwBVE7pEniCNjlNOeeyp4BQgdZDLQcAohxEAH5Uj5DEKjv6";

    /// <summary>Verifies <paramref name="message"/> and returns the trusted comment; throws with the reason otherwise.</summary>
    public static string Verify(string publicKeyBase64, string signatureFile, ReadOnlySpan<byte> message)
    {
        var key = Decode(publicKeyBase64.Trim(), "public key");
        if (key.Length != 42 || key[0] != (byte)'E' || key[1] != (byte)'d')
        {
            throw new MinisignException("The public key is not a minisign Ed25519 key.");
        }

        var lines = signatureFile.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        if (lines.Length < 4 || !lines[2].StartsWith("trusted comment: ", StringComparison.Ordinal))
        {
            throw new MinisignException("The signature file is not in minisign format.");
        }

        var signature = Decode(lines[1].Trim(), "signature");
        if (signature.Length != 74)
        {
            throw new MinisignException("The signature has the wrong length.");
        }

        var algorithm = Encoding.ASCII.GetString(signature, 0, 2);
        if (algorithm is not ("Ed" or "ED"))
        {
            throw new MinisignException($"Unsupported signature algorithm '{algorithm}'.");
        }

        if (!signature.AsSpan(2, 8).SequenceEqual(key.AsSpan(2, 8)))
        {
            throw new MinisignException("The file was signed with a different key.");
        }

        var publicKey = key.AsSpan(10, 32);
        var signed = algorithm == "ED" ? Blake2b.Hash512(message) : message.ToArray();
        if (!Ed25519.Verify(publicKey, signed, signature.AsSpan(10, 64)))
        {
            throw new MinisignException("The signature does not match the file.");
        }

        var trustedComment = lines[2]["trusted comment: ".Length..];
        var globalSignature = Decode(lines[3].Trim(), "global signature");
        var globalMessage = new byte[64 + Encoding.UTF8.GetByteCount(trustedComment)];
        signature.AsSpan(10, 64).CopyTo(globalMessage);
        Encoding.UTF8.GetBytes(trustedComment, globalMessage.AsSpan(64));
        if (globalSignature.Length != 64 || !Ed25519.Verify(publicKey, globalMessage, globalSignature))
        {
            throw new MinisignException("The trusted comment signature is invalid.");
        }

        return trustedComment;
    }

    private static byte[] Decode(string base64, string what)
    {
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new MinisignException($"The {what} is not valid base64.");
        }
    }
}

internal sealed class MinisignException(string message) : Exception(message);
