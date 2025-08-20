using System.IO;
using System.Threading.Tasks;

namespace SBSaaS.Application.Interfaces;

public interface IAntivirusScanner
{
    Task<bool> ScanAsync(Stream stream); // Returns true if clean, false if infected
}
