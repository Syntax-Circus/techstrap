namespace TechStrap.Admin.Components.Ui;

/// <summary>Who a timeline entry is from. The tint code is fixed to these three kinds and is never reused for anything else (docs/BRAND.md section 12).</summary>
public enum EntryKind
{
    Customer,
    PublicReply,
    InternalNote,
}
