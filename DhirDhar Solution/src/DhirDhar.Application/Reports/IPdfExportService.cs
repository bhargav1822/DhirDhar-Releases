using System.Threading;
using System.Threading.Tasks;

namespace DhirDhar.Application.Reports;

public interface IPdfExportService
{
    Task<string> ExportReportToPdfAsync(object report, string reportType, CancellationToken cancellationToken = default);
}
