using Microsoft.EntityFrameworkCore;
using SnG.Control.Domain.Entities;
using SnG.Control.Domain.Entities.Incremental;
namespace SnG.Control.Infrastructure.Persistence.Configurations;
internal static class EngineeringOperationsConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<EngineeringWorkspace>().Property(x=>x.Revision).IsConcurrencyToken();
        model.Entity<EngineeringPackage>(b=>{
            b.ToTable("EngineeringPackages",t=>t.HasCheckConstraint("CK_EngineeringPackage_State", "\"Revision\">=0 AND \"Status\" IN ('Open','Transferred','Completed') AND ((\"Status\"='Open' AND \"CompletedAt\" IS NULL) OR (\"Status\"<>'Open' AND \"CompletedAt\">=\"CreatedAt\"))"));
            b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever(); b.HasAlternateKey(x=>new{x.Id,x.ProjectId});
            b.Property(x=>x.CreatedBySubject).HasMaxLength(200);b.Property(x=>x.Status).HasMaxLength(20);b.Property(x=>x.Revision).IsConcurrencyToken();
            b.HasOne<EngineeringWorkspace>().WithMany().HasForeignKey(x=>x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<WorkPackageAssignment>().WithMany().HasForeignKey(x=>new{x.CurrentAssignmentId,x.ProjectId}).HasPrincipalKey(x=>new{x.Id,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ProjectTask>().WithMany().HasForeignKey(x=>new{x.TaskId,x.ProjectId}).HasPrincipalKey(x=>new{x.Id,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.TaskId).IsUnique();
        });
        model.Entity<EngineeringAssignedStructure>(b=>{
            b.ToTable("EngineeringAssignedStructures");b.HasKey(x=>new{x.ProjectId,x.StructureId});
            b.HasOne<WorkPackageMember>().WithMany().HasForeignKey(x=>new{x.AssignmentId,x.StructureId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<WorkPackageAssignment>().WithMany().HasForeignKey(x=>new{x.AssignmentId,x.ProjectId}).HasPrincipalKey(x=>new{x.Id,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EngineeringEvidenceCheck>().WithMany().HasForeignKey(x=>new{x.ProjectId,x.SubmissionId,x.StructureId,x.EvidenceVersion}).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EngineeringOpeningState>(b=>{
            b.ToTable("EngineeringOpeningStates",t=>t.HasCheckConstraint("CK_EngineeringOpening_Confirmed", "\"Revision\">=0 AND ((\"ConfirmedAt\" IS NULL AND \"ConfirmedBySubject\" IS NULL AND \"ConfirmationCanonical\" IS NULL AND \"ConfirmationSha256\" IS NULL) OR (\"ConfirmedAt\" IS NOT NULL AND \"ConfirmedBySubject\" IS NOT NULL AND \"CurrentDocumentId\" IS NOT NULL AND \"ConfirmationCanonical\" IS NOT NULL AND \"ConfirmationSha256\" ~ '^[A-F0-9]{64}$'))"));
            b.HasKey(x=>x.OpeningRecordId);b.Property(x=>x.OpeningRecordId).ValueGeneratedNever();b.HasAlternateKey(x=>new{x.OpeningRecordId,x.ProjectId});
            b.Property(x=>x.ResponsibleSubject).HasMaxLength(200);b.Property(x=>x.CreatedBySubject).HasMaxLength(200);b.Property(x=>x.ConfirmedBySubject).HasMaxLength(200);b.Property(x=>x.ConfirmationSha256).HasMaxLength(64);b.Property(x=>x.Revision).IsConcurrencyToken();
            b.HasOne<OpeningRecord>().WithMany().HasForeignKey(x=>new{x.OpeningRecordId,x.ProjectId}).HasPrincipalKey(x=>new{x.Id,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OpeningDocumentScope>().WithMany().HasForeignKey(x=>new{x.OpeningRecordId,x.CurrentDocumentId,x.ProjectId}).HasPrincipalKey(x=>new{x.OpeningRecordId,x.DocumentId,x.ProjectId}).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EngineeringWorkspaceEvent>(b=>{
            b.ToTable("EngineeringWorkspaceEvents",t=>t.HasCheckConstraint("CK_EngineeringWorkspaceEvent", "\"Revision\">=0 AND length(btrim(\"ActorSubject\"))>0 AND \"Fingerprint\" ~ '^[A-F0-9]{64}$'"));
            b.HasKey(x=>new{x.ProjectId,x.RequestKey});b.Property(x=>x.Fingerprint).HasMaxLength(64);b.Property(x=>x.Action).HasMaxLength(40);b.Property(x=>x.ActorSubject).HasMaxLength(200);
            b.HasOne<EngineeringWorkspace>().WithMany().HasForeignKey(x=>x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
