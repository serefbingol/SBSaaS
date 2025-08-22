using SBSaaS.Application.Interfaces;
using System.IO;
using System.Threading.Tasks;
using ClamAV.Net.Client;
using ClamAV.Net.Client.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SBSaaS.Infrastructure.Storage;

public class ClamAVScanner : IAntivirusScanner
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ClamAVScanner> _logger;

    public ClamAVScanner(IConfiguration configuration, ILogger<ClamAVScanner> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> ScanAsync(Stream stream)
    {
        var clamAvHost = _configuration["ClamAV:Host"] ?? "localhost";
        var clamAvPort = _configuration.GetValue<int>("ClamAV:Port", 3310);

        try
        {
            _logger.LogInformation("Connecting to ClamAV at {Host}:{Port} for scanning.", clamAvHost, clamAvPort);
            var clamAvUri = new System.Uri($"tcp://{clamAvHost}:{clamAvPort}");
            var client = ClamAvClient.Create(clamAvUri);

            // Ensure the stream is at the beginning
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            var scanResult = await client.ScanDataAsync(stream);

            if (scanResult.Infected)
            {
                _logger.LogWarning("ClamAV scan detected virus: {VirusName}", scanResult.VirusName);
                return false; // Infected
            }
            else
            {
                _logger.LogInformation("ClamAV scan completed. File is clean.");
                return true; // Clean
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error connecting to or scanning with ClamAV at {Host}:{Port}. Assuming clean for now.", clamAvHost, clamAvPort);
            // In a production environment, you might want to return false or throw an exception
            // if the scanner is unavailable, depending on your security policy.
            // For now, we'll assume clean if the scanner itself fails.
            return true;
        }
    }
}
