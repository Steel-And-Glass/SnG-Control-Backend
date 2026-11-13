using Microsoft.EntityFrameworkCore;
using SnG.Control.Domain.Entities.Production;
namespace SnG.Control.Infrastructure.Persistence.Configurations;
internal static class ProductionReviewConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<ProductionReview>(b=>{
            b.ToTable("ProductionReviews",t=>t.HasCheckConstraint("CK_Review_State","\"State\" IN (1,2,3) AND \"Revision\" >= 0 AND \"DataRevision\" >= 0"));
            b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();b.HasAlternateKey(x=>new{x.Id,x.PreparationId});
            b.HasOne<ProductionPreparation>().WithMany().HasForeignKey(x=>x.PreparationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<DocumentExtractionJob>().WithMany().HasForeignKey(x=>new{x.ExtractionJobId,x.PreparationId}).HasPrincipalKey(x=>new{x.Id,x.PreparationId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ProductionComponentRevision>().WithMany().HasForeignKey(x=>new{x.SourceRevisionId,x.PreparationId}).HasPrincipalKey(x=>new{x.Id,x.PreparationId}).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.ExtractionJobId).IsUnique();b.HasIndex(x=>new{x.PreparationId,x.Number}).IsUnique();
            b.Property(x=>x.Revision).IsConcurrencyToken();b.Property(x=>x.CreatedBySubject).HasMaxLength(200);
        });
        model.Entity<ProductionReviewRow>(b=>{
            b.ToTable("ProductionReviewRows");b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
            b.HasOne<ProductionReview>().WithMany().HasForeignKey(x=>new{x.ReviewId,x.PreparationId}).HasPrincipalKey(x=>new{x.Id,x.PreparationId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ProductionCandidateRow>().WithMany().HasForeignKey(x=>new{x.CandidateId,x.PreparationId}).HasPrincipalKey(x=>new{x.Id,x.PreparationId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PreparationSourceMember>().WithMany().HasForeignKey(x=>new{x.PreparationId,x.StructureId,x.Op}).HasPrincipalKey(x=>new{x.PreparationId,x.StructureId,x.Op}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PreparationSourceDocument>().WithMany().HasForeignKey(x=>new{x.PreparationId,x.DocumentId,x.DocumentSha256}).HasPrincipalKey(x=>new{x.PreparationId,x.DocumentId,x.Sha256}).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>new{x.ReviewId,x.CandidateId}).IsUnique();b.Property(x=>x.Op).HasMaxLength(80);b.Property(x=>x.DocumentSha256).HasMaxLength(64);
            b.Property(x=>x.OriginalJson).HasColumnType("jsonb");b.Property(x=>x.ValuesJson).HasColumnType("jsonb");
        });
        model.Entity<ProductionReviewIssue>(b=>{
            b.ToTable("ProductionReviewIssues");b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
            b.HasOne<ProductionReview>().WithMany().HasForeignKey(x=>x.ReviewId).OnDelete(DeleteBehavior.Restrict);
            b.Property(x=>x.Code).HasMaxLength(100);b.Property(x=>x.Category).HasMaxLength(40);b.Property(x=>x.EvidenceJson).HasColumnType("jsonb");
        });
        model.Entity<ProductionReviewEvent>(b=>{
            b.ToTable("ProductionReviewEvents");b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
            b.HasOne<ProductionReview>().WithMany().HasForeignKey(x=>new{x.ReviewId,x.PreparationId}).HasPrincipalKey(x=>new{x.Id,x.PreparationId}).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>new{x.PreparationId,x.RequestKey}).IsUnique();b.Property(x=>x.Actor).HasMaxLength(200);b.Property(x=>x.Action).HasMaxLength(40);b.Property(x=>x.Reason).HasMaxLength(2000);b.Property(x=>x.Fingerprint).HasMaxLength(64);
            b.Property(x=>x.DetailJson).HasColumnType("jsonb");b.Property(x=>x.ResultJson).HasColumnType("jsonb");
        });
        model.Entity<ProductionApproval>(b=>{
            b.ToTable("ProductionApprovals");b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
            b.HasOne<ProductionReview>().WithMany().HasForeignKey(x=>new{x.ReviewId,x.PreparationId}).HasPrincipalKey(x=>new{x.Id,x.PreparationId}).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.ReviewId).IsUnique();b.HasIndex(x=>x.PreparationId).IsUnique();b.Property(x=>x.Actor).HasMaxLength(200);b.Property(x=>x.Sha256).HasMaxLength(64);
        });
        model.Entity<ProductionReviewAttachment>(b=>{
            b.ToTable("ProductionReviewAttachments",t=>t.HasCheckConstraint("CK_ReviewAttachment_Size","\"SizeBytes\" > 0 AND \"SizeBytes\" <= 26214400"));b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
            b.HasOne<ProductionPreparation>().WithMany().HasForeignKey(x=>x.PreparationId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>new{x.PreparationId,x.RequestKey}).IsUnique();b.Property(x=>x.Name).HasMaxLength(255);b.Property(x=>x.ContentType).HasMaxLength(255);b.Property(x=>x.Actor).HasMaxLength(200);b.Property(x=>x.Sha256).HasMaxLength(64);
        });
    }
}
