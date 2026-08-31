using DhirDhar.Application.Reports.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Views.Reports;

public class ReportTemplateSelector : DataTemplateSelector
{
    public DataTemplate? BorrowerStatementTemplate { get; set; }
    public DataTemplate? TransactionReportTemplate { get; set; }
    public DataTemplate? InterestReportTemplate { get; set; }
    public DataTemplate? OutstandingReportTemplate { get; set; }
    public DataTemplate? BorrowerSummaryTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
    {
        return SelectTemplateCore(item);
    }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        return item switch
        {
            BorrowerStatementReport => BorrowerStatementTemplate,
            TransactionReport => TransactionReportTemplate,
            InterestReport => InterestReportTemplate,
            OutstandingReport => OutstandingReportTemplate,
            BorrowerSummaryReport => BorrowerSummaryTemplate,
            _ => base.SelectTemplateCore(item)
        };
    }
}
