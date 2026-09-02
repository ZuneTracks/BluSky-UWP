using System;
using System.Buffers;
using System.Text.Json.Serialization;
using FishyFlip.Models;

namespace UniSky.Models;

/// <summary>
/// Rents a buffer from the shared pool and guarantees it is returned exactly once.
/// </summary>
internal ref struct PooledBuffer
{
    private byte[] _array;

    public PooledBuffer(int minimumLength)
    {
        _array = ArrayPool<byte>.Shared.Rent(minimumLength);
        Length = minimumLength;
    }

    public int Length { get; }

    public Span<byte> Span
        => _array is null
            ? throw new ObjectDisposedException(nameof(PooledBuffer))
            : _array.AsSpan(0, Length);

    public void Dispose()
    {
        // Null out first so a second Dispose is a harmless no-op.
        var array = _array;
        _array = null;

        if (array is not null)
            ArrayPool<byte>.Shared.Return(array, clearArray: true);
    }
}

public record class SessionModel
{
    public bool IsActive { get; init; }
    public string Service { get; init; }
    public string DID { get; init; }
    public string RefreshJwt { get; init; }
    public string AccessJwt { get; init; }
    public string Handle { get; init; }
    public string? EmailAddress { get; init; }
    public string? ProofKey { get; init; }
    public DidDoc? DidDoc { get; init; }
    public DateTime? ExpiresAt { get; set; }

    [JsonConstructor]
    public SessionModel(bool isActive,
                        string service,
                        string did,
                        DidDoc? didDoc,
                        string refreshJwt,
                        string accessJwt,
                        string handle,
                        string? emailAddress,
                        string? proofKey,
                        DateTime? expiresAt = null)
    {
        IsActive = isActive;
        Service = service;
        DID = did;
        RefreshJwt = refreshJwt;
        AccessJwt = accessJwt;
        Handle = handle;
        EmailAddress = emailAddress;
        ProofKey = proofKey;
        DidDoc = didDoc;
        ExpiresAt = expiresAt;
    }

    public SessionModel(bool isActive,
                        string service,
                        Session session,
                        AuthSession? authSession = null)
        : this(isActive,
               service,
               session.Did.Handler,
               session.DidDoc,
               session.RefreshJwt,
               session.AccessJwt,
               session.Handle.Handle,
               session.Email,
               authSession?.ProofKey,
               session.ExpiresIn)
    { }

    [JsonIgnore]
    public AuthSession Session
        => new AuthSession(new Session(new ATDid(DID),
                                       DidDoc,
                                       new ATHandle(Handle),
                                       EmailAddress,
                                       AccessJwt,
                                       RefreshJwt,
                                       ExpiresAt ?? DateTime.MinValue),
                           this.ProofKey ?? "");
}
