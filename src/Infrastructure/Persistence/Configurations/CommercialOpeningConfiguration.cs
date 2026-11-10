using Microsoft.EntityFrameworkCore;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Entities.Incremental;
namespace SnG.Control.Infrastructure.Persistence.Configurations;
internal static class CommercialOpeningConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<CommercialOpeningRound>(b=>{
            b.ToTable("CommercialOpeningRounds",t=>{
                t.HasCheckConstraint("CK_CommercialOpeningRound","\"Number\">0 AND \"Revision\">=0 AND \"LastActivityAt\">=\"ReceivedAt\" AND (\"PresentedAt\" IS NULL OR \"PresentedAt\">=\"ReceivedAt\") AND (\"FirstResponseAt\" IS NULL OR (\"PresentedAt\" IS NOT NULL AND \"FirstResponseAt\">=\"PresentedAt\")) AND (\"ResolvedAt\" IS NULL OR (\"FirstResponseAt\" IS NOT NULL AND \"ResolvedAt\">=\"FirstResponseAt\"))");
            });
            b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();b.HasAlternateKey(x=>new{x.Id,x.OpeningRecordId,x.ProjectId});
            b.HasIndex(x=>new{x.OpeningRecordId,x.Number}).IsUnique();b.HasIndex(x=>x.OpeningRecordId).IsUnique().HasFilter("\"SupersededAt\" IS NULL");b.HasIndex(x=>x.TaskId).IsUnique();
            b.Property(x=>x.Revision).IsConcurrencyToken();b.Property(x=>x.AdvisorSubject).HasMaxLength(200);
            b.HasOne<OpeningRoundLink>().WithMany().HasForeignKey(x=>new{x.Id,x.OpeningRecordId,x.ProjectId}).HasPrincipalKey(x=>new{x.RoundId,x.OpeningRecordId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ProjectTask>().WithMany().HasForeignKey(x=>new{x.TaskId,x.ProjectId}).HasPrincipalKey(x=>new{x.Id,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OpeningDocumentScope>().WithMany().HasForeignKey(x=>new{x.OpeningRecordId,x.DocumentId,x.ProjectId}).HasPrincipalKey(x=>new{x.OpeningRecordId,x.DocumentId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CommercialOpeningRow>(b=>{
            b.ToTable("CommercialOpeningRows",t=>t.HasCheckConstraint("CK_CommercialOpeningRow","\"EvidenceVersion\">0 AND \"Revision\">=0 AND \"Approval\" IN ('AwaitingApproval','Confirmed','UnderEngineeringReview') AND ((\"Approval\"='Confirmed' AND \"AcceptanceId\" IS NOT NULL) OR (\"Approval\"<>'Confirmed' AND \"AcceptanceId\" IS NULL))"));
            b.HasKey(x=>new{x.RoundId,x.StructureId});b.HasAlternateKey(x=>new{x.RoundId,x.StructureId,x.OpeningRecordId,x.ProjectId});b.Property(x=>x.Revision).IsConcurrencyToken();b.Property(x=>x.Approval).HasMaxLength(40);
            b.HasOne<CommercialOpeningRound>().WithMany().HasForeignKey(x=>new{x.RoundId,x.OpeningRecordId,x.ProjectId}).HasPrincipalKey(x=>new{x.Id,x.OpeningRecordId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OpeningRecordMember>().WithMany().HasForeignKey(x=>new{x.OpeningRecordId,x.StructureId,x.ProjectId}).HasPrincipalKey(x=>new{x.OpeningRecordId,x.StructureId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OpeningDocumentScope>().WithMany().HasForeignKey(x=>new{x.OpeningRecordId,x.EvidenceDocumentId,x.ProjectId}).HasPrincipalKey(x=>new{x.OpeningRecordId,x.DocumentId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OpeningAcceptance>().WithMany().HasForeignKey(x=>new{x.AcceptanceId,x.OpeningRecordId,x.ProjectId}).HasPrincipalKey(x=>new{x.Id,x.OpeningRecordId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CommercialOpeningReview>(b=>{
            b.ToTable("CommercialOpeningReviews",t=>t.HasCheckConstraint("CK_CommercialOpeningReview","\"Revision\">=0 AND (\"CompletedAt\" IS NULL OR (\"CompletedAt\">=\"CreatedAt\" AND \"Declared\" AND \"DocumentId\" IS NOT NULL))"));
            b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();b.HasAlternateKey(x=>new{x.Id,x.RoundId,x.OpeningRecordId,x.ProjectId});b.Property(x=>x.Subject).HasMaxLength(200);b.Property(x=>x.Revision).IsConcurrencyToken();
            b.HasIndex(x=>x.OpeningRecordId).IsUnique().HasFilter("\"CompletedAt\" IS NULL");b.HasIndex(x=>x.TaskId).IsUnique();
            b.HasOne<CommercialOpeningRound>().WithMany().HasForeignKey(x=>new{x.RoundId,x.OpeningRecordId,x.ProjectId}).HasPrincipalKey(x=>new{x.Id,x.OpeningRecordId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ProjectTask>().WithMany().HasForeignKey(x=>new{x.TaskId,x.ProjectId}).HasPrincipalKey(x=>new{x.Id,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OpeningDocumentScope>().WithMany().HasForeignKey(x=>new{x.OpeningRecordId,x.DocumentId,x.ProjectId}).HasPrincipalKey(x=>new{x.OpeningRecordId,x.DocumentId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CommercialOpeningReviewMember>(b=>{
            b.ToTable("CommercialOpeningReviewMembers",t=>t.HasCheckConstraint("CK_CommercialOpeningReviewMember","\"Revision\">=0 AND \"EvidenceVersion\">0 AND (NOT \"Completed\" OR (\"ChangedAt\" IS NOT NULL AND \"ChangedBySubject\" IS NOT NULL))"));
            b.HasKey(x=>new{x.ReviewId,x.StructureId});b.Property(x=>x.Revision).IsConcurrencyToken();b.Property(x=>x.ChangedBySubject).HasMaxLength(200);
            b.HasOne<CommercialOpeningReview>().WithMany().HasForeignKey(x=>new{x.ReviewId,x.RoundId,x.OpeningRecordId,x.ProjectId}).HasPrincipalKey(x=>new{x.Id,x.RoundId,x.OpeningRecordId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CommercialOpeningRow>().WithMany().HasForeignKey(x=>new{x.RoundId,x.StructureId,x.OpeningRecordId,x.ProjectId}).HasPrincipalKey(x=>new{x.RoundId,x.StructureId,x.OpeningRecordId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CommercialOpeningDocument>(b=>{
            b.ToTable("CommercialOpeningDocuments",t=>t.HasCheckConstraint("CK_CommercialOpeningDocument","\"Kind\" IN ('SIGNED','REVISED') AND ((\"Kind\"='REVISED' AND \"ReviewId\" IS NOT NULL) OR (\"Kind\"='SIGNED' AND \"ReviewId\" IS NULL)) AND \"ScopeHash\" ~ '^[A-F0-9]{64}$'"));
            b.HasKey(x=>x.DocumentId);b.Property(x=>x.DocumentId).ValueGeneratedNever();b.HasAlternateKey(x=>new{x.DocumentId,x.RoundId,x.OpeningRecordId,x.ProjectId});b.Property(x=>x.Kind).HasMaxLength(20);b.Property(x=>x.ScopeHash).HasMaxLength(64);
            b.HasOne<CommercialOpeningRound>().WithMany().HasForeignKey(x=>new{x.RoundId,x.OpeningRecordId,x.ProjectId}).HasPrincipalKey(x=>new{x.Id,x.OpeningRecordId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OpeningDocumentScope>().WithMany().HasForeignKey(x=>new{x.OpeningRecordId,x.DocumentId,x.ProjectId}).HasPrincipalKey(x=>new{x.OpeningRecordId,x.DocumentId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CommercialOpeningReview>().WithMany().HasForeignKey(x=>new{x.ReviewId,x.RoundId,x.OpeningRecordId,x.ProjectId}).HasPrincipalKey(x=>new{x.Id,x.RoundId,x.OpeningRecordId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CommercialOpeningDocumentMember>(b=>{
            b.ToTable("CommercialOpeningDocumentMembers",t=>t.HasCheckConstraint("CK_CommercialOpeningDocumentMember","\"EvidenceVersion\">0"));
            b.HasKey(x=>new{x.DocumentId,x.StructureId});b.HasAlternateKey(x=>new{x.DocumentId,x.StructureId,x.RoundId,x.OpeningRecordId,x.ProjectId,x.EvidenceVersion,x.EvidenceDocumentId});
            b.HasOne<CommercialOpeningDocument>().WithMany().HasForeignKey(x=>new{x.DocumentId,x.RoundId,x.OpeningRecordId,x.ProjectId}).HasPrincipalKey(x=>new{x.DocumentId,x.RoundId,x.OpeningRecordId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CommercialOpeningRow>().WithMany().HasForeignKey(x=>new{x.RoundId,x.StructureId,x.OpeningRecordId,x.ProjectId}).HasPrincipalKey(x=>new{x.RoundId,x.StructureId,x.OpeningRecordId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OpeningDocumentScope>().WithMany().HasForeignKey(x=>new{x.OpeningRecordId,x.EvidenceDocumentId,x.ProjectId}).HasPrincipalKey(x=>new{x.OpeningRecordId,x.DocumentId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CommercialOpeningEvent>(b=>{
            b.ToTable("CommercialOpeningEvents");b.HasKey(x=>new{x.ProjectId,x.RequestKey});b.Property(x=>x.Fingerprint).HasMaxLength(64);b.Property(x=>x.Action).HasMaxLength(40);b.Property(x=>x.ActorSubject).HasMaxLength(200);
            b.HasOne<CommercialOpeningRound>().WithMany().HasForeignKey(x=>new{x.RoundId,x.OpeningRecordId,x.ProjectId}).HasPrincipalKey(x=>new{x.Id,x.OpeningRecordId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CommercialOpeningAcceptanceEvidence>(b=>{
            b.ToTable("CommercialOpeningAcceptanceEvidence",t=>t.HasCheckConstraint("CK_CommercialOpeningAcceptanceEvidence","\"Attested\" AND \"EvidenceVersion\">0"));
            b.HasKey(x=>new{x.AcceptanceId,x.StructureId});
            b.HasOne<OpeningAcceptanceMember>().WithMany().HasForeignKey(x=>new{x.AcceptanceId,x.StructureId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OpeningAcceptance>().WithMany().HasForeignKey(x=>new{x.AcceptanceId,x.OpeningRecordId,x.ProjectId}).HasPrincipalKey(x=>new{x.Id,x.OpeningRecordId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CommercialOpeningDocumentMember>().WithMany().HasForeignKey(x=>new{x.SignedDocumentId,x.StructureId,x.RoundId,x.OpeningRecordId,x.ProjectId,x.EvidenceVersion,x.EvidenceDocumentId}).HasPrincipalKey(x=>new{x.DocumentId,x.StructureId,x.RoundId,x.OpeningRecordId,x.ProjectId,x.EvidenceVersion,x.EvidenceDocumentId}).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
