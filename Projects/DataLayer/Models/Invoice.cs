using System.ComponentModel.DataAnnotations;

namespace DataLayer.Models
{
    public class Invoice
    {
        public int Id { get; set; }
        [MaxLength(200)] public string Description { get; init; } = string.Empty;
        public DateTime DueDate { get; init; }
        [MaxLength(100)] public string Supplier { get; init; } = string.Empty;
        public List<InvoiceLine> InvoiceLines { get; init; } = [];
    }

    public class InvoiceLine
    {
        public int Id { get; init; }
        [MaxLength(200)] public string Description { get; init; } = string.Empty;
        public double Price { get; init; }
        public int Quantity { get; init; }
    }
}