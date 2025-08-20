using SBSaaS.Application.Interfaces;
using System.IO;
using System.Threading.Tasks;

namespace SBSaaS.Infrastructure.Storage;

public class DummyScanner : IAntivirusScanner
{
    public Task<bool> ScanAsync(Stream stream)
    {
        // This is a dummy implementation.
        // In a real scenario, you would integrate with an actual antivirus engine (e.g., ClamAV).
        // For now, we just "scan" and return true (clean).
        return Task.FromResult(true);
    }
}
