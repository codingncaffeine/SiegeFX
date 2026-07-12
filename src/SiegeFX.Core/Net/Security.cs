using System.Security.Cryptography;
using System.Text;

namespace SiegeFX.Core.Net;

/// <summary>SC-MP-EOS P6 — session gatekeeping. The lobby mints a per-session
/// symmetric key + short-TTL HMAC tickets; the game UDP is wrapped in AEAD
/// with that key so endpoint discovery ≠ inject/hijack. Transport-agnostic:
/// wraps ANY ISessionTransport payload. Pure crypto, unit-testable.</summary>
public static class MpSecurity
{
    /// <summary>Mint a random 32-byte session key (lobby-side, per session).</summary>
    public static byte[] NewSessionKey() => RandomNumberGenerator.GetBytes(32);

    /// <summary>Derive a deterministic 32-byte AEAD key from a shared passphrase
    /// (both peers agree on it out-of-band, like the host address). Used when no
    /// lobby server exists to mint a key: PBKDF2-SHA256, fixed app salt, 200k
    /// iterations. Same passphrase → same key on both sides → frames authenticate.</summary>
    public static byte[] DeriveKey(string passphrase)
    {
        var salt = Encoding.UTF8.GetBytes("SiegeFX-MP-AEAD-v1");
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase ?? ""), salt, 200_000, HashAlgorithmName.SHA256, 32);
    }

    /// <summary>HMAC-SHA256 session ticket: proves the bearer was admitted by
    /// the lobby for this session before a given expiry. Body =
    /// player|sessionId|expiryUnix; tag authenticates it under the session key.</summary>
    public static string MintTicket(byte[] key, int player, string sessionId, DateTimeOffset expiry)
    {
        string body = $"{player}|{sessionId}|{expiry.ToUnixTimeSeconds()}";
        using var h = new HMACSHA256(key);
        var tag = h.ComputeHash(Encoding.UTF8.GetBytes(body));
        return body + "|" + Convert.ToBase64String(tag);
    }

    /// <summary>Verify a ticket against the session key + current time.
    /// Constant-time tag compare; rejects expired or tampered tickets.</summary>
    public static bool VerifyTicket(byte[] key, string ticket, string sessionId, DateTimeOffset now, out int player)
    {
        player = -1;
        var parts = ticket.Split('|');
        if (parts.Length != 4) return false;
        if (!int.TryParse(parts[0], out player)) { player = -1; return false; }
        if (parts[1] != sessionId) return false;
        if (!long.TryParse(parts[2], out var expUnix)) return false;
        if (DateTimeOffset.FromUnixTimeSeconds(expUnix) < now) return false;
        string body = $"{parts[0]}|{parts[1]}|{parts[2]}";
        using var h = new HMACSHA256(key);
        var expect = h.ComputeHash(Encoding.UTF8.GetBytes(body));
        byte[] got;
        try { got = Convert.FromBase64String(parts[3]); } catch { return false; }
        return CryptographicOperations.FixedTimeEquals(expect, got);
    }

    // AEAD frame layout: [nonce 12][ciphertext N][tag 16].
    const int NonceLen = 12, TagLen = 16;

    /// <summary>AES-GCM seal a plaintext payload under the session key.</summary>
    public static byte[] Seal(byte[] key, ReadOnlySpan<byte> plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceLen);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagLen];
        using var gcm = new AesGcm(key, TagLen);
        gcm.Encrypt(nonce, plaintext, cipher, tag);
        var outp = new byte[NonceLen + cipher.Length + TagLen];
        nonce.CopyTo(outp, 0);
        cipher.CopyTo(outp, NonceLen);
        tag.CopyTo(outp, NonceLen + cipher.Length);
        return outp;
    }

    /// <summary>AES-GCM open a sealed frame. Returns null on any tamper /
    /// truncation / bad tag — never throws on attacker input.</summary>
    public static byte[]? Open(byte[] key, ReadOnlySpan<byte> frame)
    {
        if (frame.Length < NonceLen + TagLen) return null;
        var nonce = frame.Slice(0, NonceLen);
        int cipherLen = frame.Length - NonceLen - TagLen;
        var cipher = frame.Slice(NonceLen, cipherLen);
        var tag = frame.Slice(NonceLen + cipherLen, TagLen);
        var plain = new byte[cipherLen];
        try
        {
            using var gcm = new AesGcm(key, TagLen);
            gcm.Decrypt(nonce, cipher, tag, plain);
            return plain;
        }
        catch (CryptographicException) { return null; } // failed auth = drop
    }
}

/// <summary>SC-MP-EOS P6 — an ISessionTransport decorator that AEAD-seals
/// every payload under the session key. Drops frames that fail to open
/// (tampered / not from a session member) silently, logging the drop. The
/// game layer above sees only authenticated plaintext.</summary>
public sealed class SecureTransport : ISessionTransport
{
    readonly ISessionTransport _inner;
    readonly byte[] _key;
    public SecureTransport(ISessionTransport inner, byte[] sessionKey) { _inner = inner; _key = sessionKey; }

    /// <summary>The wrapped transport — so callers can reach provider-specific
    /// methods not on the interface (e.g. the EOS packet Pump).</summary>
    public ISessionTransport Inner => _inner;

    public string ProviderId => _inner.ProviderId + "+aead";
    public IReadOnlyList<int> Peers => _inner.Peers;
    public event Action<int>? PeerConnected { add => _inner.PeerConnected += value; remove => _inner.PeerConnected -= value; }
    public event Action<int>? PeerDisconnected { add => _inner.PeerDisconnected += value; remove => _inner.PeerDisconnected -= value; }

    public Task<bool> ListenAsync(int port, CancellationToken ct) => _inner.ListenAsync(port, ct);
    public Task<bool> ConnectAsync(string hostAddress, CancellationToken ct) => _inner.ConnectAsync(hostAddress, ct);
    public void Send(int peerId, ReadOnlySpan<byte> payload) => _inner.Send(peerId, MpSecurity.Seal(_key, payload));

    public bool TryReceive(out int peerId, out byte[] payload)
    {
        while (_inner.TryReceive(out peerId, out var sealed_))
        {
            var opened = MpSecurity.Open(_key, sealed_);
            if (opened is not null) { payload = opened; return true; }
            NetLog.Warn($"secure: dropped unauthenticated frame from peer {peerId}");
        }
        payload = Array.Empty<byte>();
        return false;
    }

    public void Dispose() => _inner.Dispose();
}
