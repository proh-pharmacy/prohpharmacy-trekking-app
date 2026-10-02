namespace prohpharmacy_trekking_app.Features.Trekking.Entities;

public class InvoiceNumberTracker
{
    public Guid ScopeId { get; set; }
    public string ScopeType { get; set; } = string.Empty;
    public int LastSequence { get; set; }
}
