using System;
using System.Threading;
using System.Threading.Tasks;

namespace SBSaaS.Application.Interfaces;

public interface IObjectSigner
{
    Task<string> PresignPutAsync(string bucket, string objectName, TimeSpan expiry, string? contentType, CancellationToken ct);
    Task<string> PresignGetAsync(string bucket, string objectName, TimeSpan expiry, CancellationToken ct);
}
