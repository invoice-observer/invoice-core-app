namespace DataLayer.Models
{
    public record InvoiceSummaryDto(int Id, string Supplier, DateTime DueDate, string Description, double Total);
}
