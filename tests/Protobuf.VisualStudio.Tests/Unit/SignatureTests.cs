using System.Text;
using Protobuf.VisualStudio.Security;

namespace Protobuf.VisualStudio.Tests.Unit;

public class Blake2bTests
{
    // Expected values from Python's hashlib.blake2b (an independent implementation).
    [Theory]
    [InlineData(0, "786a02f742015903c6c6fd852552d272912f4740e15847618a86e217f71f5419d25e1031afee585313896444934eb04b903a685b1448b755d56f701afe9be2ce")]
    [InlineData(1, "4fe4da61bcc756071b226843361d74944c72245d23e8245ea678c13fdcd7fe2ae529cf999ad99cc24f7a73416a18ba53e76c0afef83b16a568b12fbfc1a2674d")]
    [InlineData(3, "76ce7e90c577d436a65b691e51f0a1ad536130d70a18df3bf5368551327258b6f706f4a47c45f2d0d8147cfbb5623077e798247972e4f9338603a13abc05a4db")]
    [InlineData(127, "71546bbf9110ad184cc60f2eb120fcfd9b4dbbca7a7f1270045b8a23a6a4f4330f65c1f030dd2f5fabc6c57617242c37cf427bd90407fac5b9deffd3ae888c39")]
    [InlineData(128, "2d9e329f42afa3601d646692b81c13e87fcaff5bf15972e9813d7373cb6d181f9599f4d513d4af4fd6ebd37497aceb29aba5ee23ed764d8510b552bd088814fb")]
    [InlineData(129, "47889df9eb4d717afc5019df5c6a83df00a0b8677395e078cd5778ace0f338a618e68b7d9afb065d9e6a01ccd31d109447e7fae771c3ee3e105709194122ba2b")]
    [InlineData(256, "91019c558584980249ca43eceed27e19f1c3c24161b93eed1eee2a6a774f60bf8a81b43750870bee1698feac9c5336ae4d5c842e7ead159bf3916387e8ded9ae")]
    [InlineData(1000, "4bdd2c9cf31d797a81d245c989ffb7515143ca345c66f73087dd5c58bf642bf083ba16894eab79e3b08d5126404d833e7510271b50be36a7b7cbbb46f5c89fac")]
    public void Matches_an_independent_implementation_across_block_boundaries(int length, string expected)
    {
        var data = Enumerable.Range(0, length).Select(i => (byte)((i * 7) + 3)).ToArray();
        Assert.Equal(expected, Convert.ToHexStringLower(Blake2b.Hash512(data)));
    }

    [Fact]
    public void Matches_the_RFC_7693_abc_vector()
    {
        Assert.Equal(
            "ba80a53f981c4d0d6a2797b69f12f6e94c212f14685ac4b74b12bb6fdbffa2d17d87c5392aab792dc252d5de4533cc9518d38aa8dbf1925ab92386edd4009923",
            Convert.ToHexStringLower(Blake2b.Hash512("abc"u8)));
    }
}

public class Ed25519Tests
{
    // RFC 8032 section 7.1, tests 1 to 3.
    [Theory]
    [InlineData(
        "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a",
        "",
        "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b")]
    [InlineData(
        "3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c",
        "72",
        "92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00")]
    [InlineData(
        "fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025",
        "af82",
        "6291d657deec24024827e69c3abe01a30ce548a284743a445e3680d7db5ac3ac18ff9b538d16f290ae67f760984dc6594a7c15e9716ed28dc027beceea1ec40a")]
    public void Accepts_the_RFC_8032_test_vectors(string publicKey, string message, string signature)
    {
        Assert.True(Ed25519.Verify(Convert.FromHexString(publicKey), Convert.FromHexString(message), Convert.FromHexString(signature)));
    }

    [Fact]
    public void Rejects_a_changed_message()
    {
        Assert.False(Ed25519.Verify(
            Convert.FromHexString("3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c"),
            Convert.FromHexString("73"),
            Convert.FromHexString("92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00")));
    }

    [Fact]
    public void Rejects_a_changed_signature_and_the_wrong_key()
    {
        var key = Convert.FromHexString("3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c");
        var signature = Convert.FromHexString("92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00");
        var tampered = (byte[])signature.Clone();
        tampered[40] ^= 0x01;
        var otherKey = Convert.FromHexString("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");

        Assert.False(Ed25519.Verify(key, [0x72], tampered));
        Assert.False(Ed25519.Verify(otherKey, [0x72], signature));
    }

    [Fact]
    public void Rejects_a_non_canonical_s_and_bad_lengths()
    {
        var key = Convert.FromHexString("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        var signature = Convert.FromHexString("e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b");
        var highS = (byte[])signature.Clone();
        highS[63] |= 0xF0; // s >= group order

        Assert.False(Ed25519.Verify(key, [], highS));
        Assert.False(Ed25519.Verify(key.AsSpan(1), [], signature));
        Assert.False(Ed25519.Verify(key, [], signature.AsSpan(1)));
    }
}

public class MinisignTests
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "TestData", "buf-v1.75.0");

    private static byte[] Checksums => File.ReadAllBytes(Path.Combine(Dir, "sha256.txt"));

    private static string Signature => File.ReadAllText(Path.Combine(Dir, "sha256.txt.minisig"));

    [Fact]
    public void Verifies_the_real_buf_v1_75_0_checksum_file()
    {
        var trusted = Minisign.Verify(Minisign.BufPublicKey, Signature, Checksums);
        Assert.Contains("filename:sha256.txt", trusted, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_checksum_file_with_one_changed_byte()
    {
        var tampered = Checksums;
        tampered[10] ^= 0x01;

        var error = Assert.Throws<MinisignException>(() => Minisign.Verify(Minisign.BufPublicKey, Signature, tampered));
        Assert.Contains("does not match", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_checksum_file_with_converted_line_endings()
    {
        var crlf = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Checksums).Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.Throws<MinisignException>(() => Minisign.Verify(Minisign.BufPublicKey, Signature, crlf));
    }

    [Fact]
    public void Rejects_an_edited_trusted_comment()
    {
        var edited = Signature.Replace("filename:sha256.txt", "filename:evil.txt", StringComparison.Ordinal);

        var error = Assert.Throws<MinisignException>(() => Minisign.Verify(Minisign.BufPublicKey, edited, Checksums));
        Assert.Contains("trusted comment", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_different_signing_key()
    {
        // Same format, different key id and key bytes.
        var other = Convert.ToBase64String([(byte)'E', (byte)'d', .. new byte[8], .. Convert.FromHexString("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a")]);

        var error = Assert.Throws<MinisignException>(() => Minisign.Verify(other, Signature, Checksums));
        Assert.Contains("different key", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not a signature")]
    [InlineData("untrusted comment: x\n!!!\ntrusted comment: y\nzzz")]
    public void Rejects_malformed_signature_files(string content)
    {
        Assert.Throws<MinisignException>(() => Minisign.Verify(Minisign.BufPublicKey, content, Checksums));
    }
}
