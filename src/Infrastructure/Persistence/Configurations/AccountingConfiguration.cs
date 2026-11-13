using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.ValueObjects;
namespace SnG.Control.Infrastructure.Persistence.Configurations;

public sealed class AccountingHandoffConfiguration : IEntityTypeConfiguration<AccountingHandoff>
{
    internal const string DecisionIndex = "UX_AccountingHandoffs_Decision";
    internal const string QuotationIndex = "UX_AccountingHandoffs_Quotation";
    internal const string TaskIndex = "UX_AccountingHandoffs_Task";
    public void Configure(EntityTypeBuilder<AccountingHandoff> b)
    {
        b.ToTable("AccountingHandoffs", t => { t.HasCheckConstraint("CK_AccountingHandoffs_Round", "\"QuotationRound\" > 0"); t.HasCheckConstraint("CK_AccountingHandoffs_Actor", "length(btrim(\"SentByGoogleSubject\")) > 0"); });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.SentAt).HasColumnType("timestamp with time zone");
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ClientQuotationDecision>().WithMany().HasForeignKey(x => x.SourceClientQuotationDecisionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CommercialQuotation>().WithMany().HasForeignKey(x => x.CommercialQuotationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProjectTask>().WithMany().HasForeignKey(x => x.RegisterInitialDepositTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.SourceClientQuotationDecisionId).IsUnique().HasDatabaseName(DecisionIndex);
        b.HasIndex(x => x.CommercialQuotationId).IsUnique().HasDatabaseName(QuotationIndex);
        b.HasIndex(x => x.RegisterInitialDepositTaskId).IsUnique().HasDatabaseName(TaskIndex);
    }
}
public sealed class InitialDepositConfiguration : IEntityTypeConfiguration<InitialDeposit>
{
    internal const string HandoffIndex = "UX_InitialDeposits_Handoff";
    internal const string TaskIndex = "UX_InitialDeposits_Task";
    internal const string QuotationIndex = "UX_InitialDeposits_Quotation";
    public void Configure(EntityTypeBuilder<InitialDeposit> b)
    {
        b.ToTable("InitialDeposits", t => { t.HasCheckConstraint("CK_InitialDeposits_Amount", "\"Amount\" > 0"); t.HasCheckConstraint("CK_InitialDeposits_Receipt", "\"ReceivedAt\" <= \"RecordedAt\""); t.HasCheckConstraint("CK_InitialDeposits_Actor", "length(btrim(\"RecordedByGoogleSubject\")) > 0"); });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Ignore(x => x.CurrencyCode);
        b.Property(x => x.Amount).HasPrecision(Money.Precision, Money.Scale);
        b.Property(x => x.ReceivedAt).HasColumnType("timestamp with time zone"); b.Property(x => x.RecordedAt).HasColumnType("timestamp with time zone");
        b.Property(x => x.Reference).HasMaxLength(200); b.Property(x => x.Note).HasMaxLength(2000);
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AccountingHandoff>().WithMany().HasForeignKey(x => x.AccountingHandoffId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CommercialQuotation>().WithMany().HasForeignKey(x => x.CommercialQuotationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProjectTask>().WithMany().HasForeignKey(x => x.RegisterInitialDepositTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.AccountingHandoffId).IsUnique().HasDatabaseName(HandoffIndex);
        b.HasIndex(x => x.RegisterInitialDepositTaskId).IsUnique().HasDatabaseName(TaskIndex);
        b.HasIndex(x => x.CommercialQuotationId).IsUnique().HasDatabaseName(QuotationIndex);
    }
}
