using DataLayer.Models;
using Microsoft.EntityFrameworkCore;

namespace DataLayer
{
    public class InvoiceDbContext(DbContextOptions<InvoiceDbContext> options) : DbContext(options)
    {
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<InvoiceLine> InvoiceLines { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Existing databases get it from demo/index.sql; EnsureCreated does not alter existing tables.
            modelBuilder.Entity<Invoice>().HasIndex(i => new { i.Supplier, i.DueDate });
        }
    }
}