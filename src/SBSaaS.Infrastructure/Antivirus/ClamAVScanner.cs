using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using nClam;
using SBSaaS.Application.Interfaces;
using SBSaaS.Application.Models;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SBSaaS.Infrastructure.Antivirus
{
    public class ClamAVScanner : IAntivirusScanner
    {
        private readonly ILogger<ClamAVScanner> _logger;
        private readonly ClamAVOptions _options;

        public ClamAVScanner(ILogger<ClamAVScanner> logger, IOptions<ClamAVOptions> options)
        {
            _logger = logger;
            _options = options.Value;
        }

        public async Task<ScanResult> ScanFileAsync(Stream fileStream, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Connecting to ClamAV on {Host}:{Port}", _options.Host, _options.Port);

                var clam = new ClamClient(_options.Host, _options.Port);
                var scanResult = await clam.SendAndScanFileAsync(fileStream, cancellationToken);

                switch (scanResult.Result)
                {
                    case ClamScanResults.Clean:
                        _logger.LogInformation("File is clean.");
                        return new ScanResult { IsInfected = false };

                    case ClamScanResults.VirusDetected:
                        var virusName = scanResult.InfectedFiles?.FirstOrDefault()?.VirusName;
                        _logger.LogWarning("File is infected! Virus found: {VirusName}", virusName);
                        return new ScanResult { IsInfected = true, VirusName = virusName };

                    case ClamScanResults.Error:
                        _logger.LogError("An error occurred while scanning the file: {ErrorMessage}", scanResult.RawResult);
                        throw new System.Exception($"ClamAV scan error: {scanResult.RawResult}");

                    default:
                        _logger.LogError("Unknown scan result: {RawResult}", scanResult.RawResult);
                        throw new System.Exception($"ClamAV unknown scan result: {scanResult.RawResult}");
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to or scan with ClamAV.");
                throw;
            }
        }
    }
}
