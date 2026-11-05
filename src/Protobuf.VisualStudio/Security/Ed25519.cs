using System.Numerics;
using System.Security.Cryptography;

namespace Protobuf.VisualStudio.Security;

/// <summary>
/// Ed25519 signature verification, a direct port of the reference code in RFC 8032 section 6 (extended
/// coordinates, BigInteger arithmetic). Verification only: no secret keys are ever handled, so constant-time
/// behaviour is not a concern. .NET has no built-in Ed25519.
/// </summary>
internal static class Ed25519
{
    private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;

    /// <summary>Group order (q in RFC 8032, often called L).</summary>
    private static readonly BigInteger Q = BigInteger.Pow(2, 252) + BigInteger.Parse("27742317777372353535851937790883648493", System.Globalization.CultureInfo.InvariantCulture);

    private static readonly BigInteger D = Mod(-121665 * Inverse(121666));

    private static readonly BigInteger SqrtMinusOne = BigInteger.ModPow(2, (P - 1) / 4, P);

    private static readonly Point G = BasePoint();

    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        if (publicKey.Length != 32 || signature.Length != 64)
        {
            return false;
        }

        if (Decompress(publicKey) is not { } a)
        {
            return false;
        }

        var rBytes = signature[..32];
        if (Decompress(rBytes) is not { } r)
        {
            return false;
        }

        var s = FromLittleEndian(signature[32..]);
        if (s >= Q)
        {
            return false;
        }

        var hashInput = new byte[32 + 32 + message.Length];
        rBytes.CopyTo(hashInput);
        publicKey.CopyTo(hashInput.AsSpan(32));
        message.CopyTo(hashInput.AsSpan(64));
        var h = FromLittleEndian(SHA512.HashData(hashInput)) % Q;

        var sB = Multiply(s, G);
        var hA = Multiply(h, a);
        return PointEqual(sB, Add(r, hA));
    }

    private static Point BasePoint()
    {
        var y = Mod(4 * Inverse(5));
        var x = RecoverX(y, 0) ?? throw new InvalidOperationException("Invalid Ed25519 base point.");
        return new Point(x, y, 1, Mod(x * y));
    }

    private static Point? Decompress(ReadOnlySpan<byte> encoded)
    {
        var y = FromLittleEndian(encoded);
        var sign = (int)((y >> 255) & 1);
        y &= (BigInteger.One << 255) - 1;
        return RecoverX(y, sign) is { } x ? new Point(x, y, 1, Mod(x * y)) : null;
    }

    private static BigInteger? RecoverX(BigInteger y, int sign)
    {
        if (y >= P)
        {
            return null;
        }

        var x2 = Mod((y * y - 1) * Inverse(D * y * y + 1));
        if (x2.IsZero)
        {
            return sign == 0 ? BigInteger.Zero : null;
        }

        var x = BigInteger.ModPow(x2, (P + 3) / 8, P);
        if (!Mod(x * x - x2).IsZero)
        {
            x = Mod(x * SqrtMinusOne);
        }

        if (!Mod(x * x - x2).IsZero)
        {
            return null;
        }

        if ((int)(x & 1) != sign)
        {
            x = P - x;
        }

        return x;
    }

    private static Point Add(Point p1, Point p2)
    {
        var a = Mod((p1.Y - p1.X) * (p2.Y - p2.X));
        var b = Mod((p1.Y + p1.X) * (p2.Y + p2.X));
        var c = Mod(2 * p1.T * p2.T * D);
        var d = Mod(2 * p1.Z * p2.Z);
        var e = b - a;
        var f = d - c;
        var g = d + c;
        var h = b + a;
        return new Point(Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
    }

    private static Point Multiply(BigInteger scalar, Point point)
    {
        var result = new Point(0, 1, 1, 0);
        while (scalar > 0)
        {
            if (!scalar.IsEven)
            {
                result = Add(result, point);
            }

            point = Add(point, point);
            scalar >>= 1;
        }

        return result;
    }

    private static bool PointEqual(Point p1, Point p2) =>
        Mod(p1.X * p2.Z - p2.X * p1.Z).IsZero && Mod(p1.Y * p2.Z - p2.Y * p1.Z).IsZero;

    private static BigInteger Mod(BigInteger value)
    {
        var r = value % P;
        return r.Sign < 0 ? r + P : r;
    }

    private static BigInteger Inverse(BigInteger value) => BigInteger.ModPow(Mod(value), P - 2, P);

    private static BigInteger FromLittleEndian(ReadOnlySpan<byte> bytes) => new(bytes, isUnsigned: true, isBigEndian: false);

    private readonly record struct Point(BigInteger X, BigInteger Y, BigInteger Z, BigInteger T);
}
