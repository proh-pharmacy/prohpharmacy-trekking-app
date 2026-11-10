using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using prohpharmacy_trekking_app.Features.Trekking.Entities;

namespace prohpharmacy_trekking_app.Database;

public sealed class InvoicePaymentConfiguration : IEntityTypeConfiguration<InvoicePayment>
{
    public void Configure(EntityTypeBuilder<InvoicePayment> entity)
    {
        entity.HasKey(p => p.Id);
        entity.Property(p => p.Amount).HasPrecision(14, 2);
        entity.Property(p => p.InvoiceTotalAtPayment).HasPrecision(14, 2);
        entity.Property(p => p.BalanceAfterPayment).HasPrecision(14, 2);
        entity.Property(p => p.PaymentMethod).HasConversion<string>().HasMaxLength(30);
        entity.Property(p => p.ReceiptNumber).HasMaxLength(40);
        entity.Property(p => p.Reference).HasMaxLength(200);
        entity.HasIndex(p => p.ReceiptNumber).IsUnique().HasFilter("\"ReceiptNumber\" IS NOT NULL");
        entity.HasIndex(p => p.ClientGeneratedId).IsUnique();
        entity.HasOne(p => p.Invoice).WithMany(i => i.Payments).HasForeignKey(p => p.SaleInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Features.Staff.Entities.StaffMember>().WithMany().HasForeignKey(p => p.RecordedByStaffId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(t => t.HasCheckConstraint("CK_InvoicePayments_Amount", "\"Amount\" > 0"));
    }
}
