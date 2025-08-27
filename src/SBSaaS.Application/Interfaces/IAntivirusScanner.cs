using SBSaaS.Application.Models;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SBSaaS.Application.Interfaces
{
    public interface IAntivirusScanner
    {
        Task<ScanResult> ScanFileAsync(Stream fileStream, CancellationToken cancellationToken);
    }
}