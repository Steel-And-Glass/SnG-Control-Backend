using Microsoft.EntityFrameworkCore;
using SnG.Control.Domain.Entities;
using SnG.Control.Infrastructure.Persistence.Configurations;

namespace SnG.Control.Infrastructure.Persistence;

public class SnGControlDbContext(DbContextOptions<SnGControlDbContext> options) : DbContext(options)
{

    public DbSet<OpeningRecordRound> OpeningRecordRounds => Set<OpeningRecordRound>();
    public DbSet<OpeningRecordStructure> OpeningRecordStructures => Set<OpeningRecordStructure>();
    public DbSet<OpeningRecordDocument> OpeningRecordDocuments => Set<OpeningRecordDocument>();
    public DbSet<OpeningRecordEvent> OpeningRecordEvents => Set<OpeningRecordEvent>();
    public DbSet<EngineeringChecklist> EngineeringChecklists => Set<EngineeringChecklist>();
    public DbSet<EngineeringStructure> EngineeringStructures => Set<EngineeringStructure>();
    public DbSet<EngineeringEvent> EngineeringEvents => Set<EngineeringEvent>();
    public DbSet<EngineeringActa> EngineeringActas => Set<EngineeringActa>();
    public DbSet<MeasurementReport> MeasurementReports => Set<MeasurementReport>();
    public DbSet<MeasurementSketchVersion> MeasurementSketchVersions => Set<MeasurementSketchVersion>();
    public DbSet<MeasurementChecklist> MeasurementChecklists => Set<MeasurementChecklist>();
    public DbSet<MeasurementStructure> MeasurementStructures => Set<MeasurementStructure>();
    public DbSet<MeasurementEvent> MeasurementEvents => Set<MeasurementEvent>();
    public DbSet<MeasurementSubmissionDocument> MeasurementSubmissionDocuments => Set<MeasurementSubmissionDocument>();
    public DbSet<DepositVerification> DepositVerifications => Set<DepositVerification>();
    public DbSet<DepositVerificationDocument> DepositVerificationDocuments => Set<DepositVerificationDocument>();
    public DbSet<AccountingHandoff> AccountingHandoffs => Set<AccountingHandoff>();
    public DbSet<InitialDeposit> InitialDeposits => Set<InitialDeposit>();
    public DbSet<RequotationRequest> RequotationRequests => Set<RequotationRequest>();
    public DbSet<ClientQuotationDecision> ClientQuotationDecisions => Set<ClientQuotationDecision>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectTask> ProjectTasks => Set<ProjectTask>();
    public DbSet<BudgetQuotationVersion> BudgetQuotationVersions => Set<BudgetQuotationVersion>();
    public DbSet<CommercialCatalog> CommercialCatalogs => Set<CommercialCatalog>();
    public DbSet<CommercialCatalogItem> CommercialCatalogItems => Set<CommercialCatalogItem>();
    public DbSet<CommercialCatalogItemExtraction> CommercialCatalogItemExtractions => Set<CommercialCatalogItemExtraction>();
    public DbSet<CommercialQuotation> CommercialQuotations => Set<CommercialQuotation>();
    public DbSet<CommercialQuotationItem> CommercialQuotationItems => Set<CommercialQuotationItem>();

    public DbSet<ProjectNotification> ProjectNotifications => Set<ProjectNotification>();
    public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();
    public DbSet<ProjectDocumentHandoff> ProjectDocumentHandoffs => Set<ProjectDocumentHandoff>();
    public DbSet<ProjectDocumentHandoffItem> ProjectDocumentHandoffItems => Set<ProjectDocumentHandoffItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        IncrementalFlowConfiguration.Configure(modelBuilder);
        modelBuilder.ApplyConfiguration(new OpeningRecordRoundConfiguration());
        modelBuilder.ApplyConfiguration(new OpeningRecordStructureConfiguration());
        modelBuilder.ApplyConfiguration(new OpeningRecordDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new OpeningRecordEventConfiguration());
        modelBuilder.ApplyConfiguration(new EngineeringChecklistConfiguration());
        modelBuilder.ApplyConfiguration(new EngineeringStructureConfiguration());
        modelBuilder.ApplyConfiguration(new EngineeringEventConfiguration());
        modelBuilder.ApplyConfiguration(new EngineeringActaConfiguration());
        modelBuilder.ApplyConfiguration(new MeasurementReportConfiguration());
        modelBuilder.ApplyConfiguration(new MeasurementSketchVersionConfiguration());
        modelBuilder.ApplyConfiguration(new MeasurementChecklistConfiguration());
        modelBuilder.ApplyConfiguration(new MeasurementStructureConfiguration());
        modelBuilder.ApplyConfiguration(new MeasurementEventConfiguration());
        modelBuilder.ApplyConfiguration(new MeasurementSubmissionDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new DepositVerificationConfiguration());
        modelBuilder.ApplyConfiguration(new DepositVerificationDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new AccountingHandoffConfiguration());
        modelBuilder.ApplyConfiguration(new InitialDepositConfiguration());
        modelBuilder.ApplyConfiguration(new RequotationRequestConfiguration());
        modelBuilder.ApplyConfiguration(new ClientQuotationDecisionConfiguration());
        modelBuilder.ApplyConfiguration(new ClientConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectTaskConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectNotificationConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectDocumentHandoffConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectDocumentHandoffItemConfiguration());
        modelBuilder.ApplyConfiguration(new BudgetQuotationVersionConfiguration());
        modelBuilder.ApplyConfiguration(new CommercialCatalogConfiguration());
        modelBuilder.ApplyConfiguration(new CommercialCatalogItemConfiguration());
        modelBuilder.ApplyConfiguration(new CommercialCatalogItemExtractionConfiguration());
        modelBuilder.ApplyConfiguration(new CommercialQuotationConfiguration());
        modelBuilder.ApplyConfiguration(new CommercialQuotationItemConfiguration());
    }
}
