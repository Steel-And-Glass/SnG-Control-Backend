using SnG.Control.Application.Features.Requotations;
using SnG.Control.Application.Features.ClientQuotationDecisions;
using SnG.Control.Application.Features.Notifications;
using SnG.Control.Application.Features.ProjectDocuments;
using SnG.Control.Application.Features.Management;
using SnG.Control.Application.Scheduling;
using SnG.Control.Api.Workers;
using System.Text.Json.Serialization;
using SnG.Control.Api.Authentication;
using SnG.Control.Application.Features.CommercialQuotations;
using SnG.Control.Api.Authorization;
using SnG.Control.Application.Features.Budgets.CommercialHandoff;
using Microsoft.Extensions.Options;
using SnG.Control.Application.Features.Budgets.ConfirmQuotation;
using SnG.Control.Application.Files;
using SnG.Control.Infrastructure.Files;
using SnG.Control.Api.Contracts;
using SnG.Control.Api.Errors;
using SnG.Control.Application.Features.Budgets;
using SnG.Control.Application.Features.Budgets.AssignSg;
using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using SnG.Control.Infrastructure.Quotations;
using SnG.Control.Application.Features.Clients;
using SnG.Control.Application.Features.Clients.Create;
using SnG.Control.Application.Features.Clients.GetById;
using SnG.Control.Application.Features.Clients.List;
using SnG.Control.Application.Features.Projects;
using SnG.Control.Application.Features.Projects.Create;
using SnG.Control.Application.Features.Projects.GetById;
using SnG.Control.Application.Features.Projects.List;
using SnG.Control.Infrastructure.Persistence.Stores;
using SnG.Control.Application.Features.ProjectTasks;
using SnG.Control.Application.Features.ProjectTasks.SendToBudgets;
using SnG.Control.Application.Features.ProjectTasks.ListByArea;
using SnG.Control.Application.Features.ProjectTasks.ListByProject;
using SnG.Control.Domain.Enums;

namespace SnG.Control.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddScoped<SnG.Control.Application.Features.QuotationViews.IQuotationReadStore, EfQuotationReadStore>();
        services.AddScoped<SnG.Control.Application.Features.QuotationViews.QuotationReadService>();
        services.AddScoped<IClientQuotationDecisionStore, EfClientQuotationDecisionStore>();
        services.AddScoped<ClientQuotationDecisionService>();
        services.AddScoped<IRequotationStore, EfRequotationStore>();
        services.AddScoped<RequotationService>();
        services.AddActiveRoles();
        services.AddScoped<SnG.Control.Application.Access.ProjectReadAccess>();
        services.AddManagementAuthorization();
        services.AddScoped<IManagementReadStore, EfManagementReadStore>();
        services.AddScoped<ListManagementTasksUseCase>();
        services.AddScoped<GetManagementOverviewUseCase>();
        services.AddScoped<GetManagementQuotationPortfolioUseCase>();
        services.AddScoped<ProjectDocumentAccess>();
        services.AddScoped<ProjectDocumentService>();
        services.AddScoped<IProjectDocumentStore, EfProjectDocumentStore>();
        services.AddScoped<IReceiptDocumentStore, EfProjectDocumentStore>();
        services.AddScoped<SnG.Control.Application.Features.Measurements.IMeasurementDocumentStore, EfProjectDocumentStore>();
        services.AddScoped<SnG.Control.Application.Features.Measurements.IMeasurementStore, EfMeasurementStore>();
        services.AddScoped<SnG.Control.Application.Features.Measurements.MeasurementService>();
        services.AddSingleton(sp => {
            var raw = sp.GetRequiredService<IConfiguration>()["Measurements:IncrementalNewProjectsFromUtc"];
            if (string.IsNullOrWhiteSpace(raw)) return new SnG.Control.Application.Features.Measurements.IncrementalMeasurementActivation();
            if (!raw.EndsWith("Z", StringComparison.Ordinal) || !DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var cutoff))
                throw new InvalidOperationException("Measurements:IncrementalNewProjectsFromUtc must be an explicit UTC timestamp.");
            return new SnG.Control.Application.Features.Measurements.IncrementalMeasurementActivation(cutoff);
        });
        services.AddScoped<IStreamingFileStorage, LocalFileStorage>();
        services.AddScoped<IProjectDocumentArchive, ProjectDocumentArchive>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ColombiaBusinessCalendar>();
        services.AddSingleton<TaskTiming>();
        services.AddScoped<ProjectNotificationPlanner>();
        services.AddScoped<EfProjectNotificationWriter>();
        services.AddScoped<IProjectNotificationStore, EfProjectNotificationStore>();
        services.AddScoped<IProjectNotificationSweepStore, EfProjectNotificationSweepStore>();
        services.AddScoped<ProjectNotificationSweepService>();
        services.AddScoped<ListPersonalNotificationsUseCase>();
        services.AddScoped<ListProjectNotificationsUseCase>();
        services.AddHostedService<ProjectNotificationBackgroundService>();
        services.AddBudgetsAssigneeAuthorization();
        services.AddAccountingAssigneeAuthorization();
        services.AddOptions<EngineeringAssigneeOptions>().BindConfiguration("Authentication");
        services.AddSingleton<SnG.Control.Application.Identity.IEngineeringAssigneeIdentity, ConfiguredEngineeringAssigneeIdentity>();
        services.AddScoped<SnG.Control.Application.Features.Engineering.EngineeringService>();
        services.AddScoped<SnG.Control.Application.Features.Engineering.IEngineeringStore, EfEngineeringStore>();
        services.AddScoped<SnG.Control.Application.Features.Engineering.IOpeningRecordStore, EfOpeningRecordStore>();
        services.AddScoped<SnG.Control.Application.Features.Engineering.OpeningRecordService>();
        services.AddOptions<InstallationAssigneeOptions>().BindConfiguration("Authentication");
        services.AddSingleton<SnG.Control.Application.Identity.IInstallationAssigneeIdentity, ConfiguredInstallationAssigneeIdentity>();
        services.AddScoped<SnG.Control.Application.Features.Accounting.IDepositVerificationStore, EfDepositVerificationStore>();
        services.AddScoped<SnG.Control.Application.Features.Accounting.DepositVerificationService>();
        services.AddScoped<SnG.Control.Application.Features.Accounting.IAccountingStore, EfAccountingStore>();
        services.AddScoped<SnG.Control.Application.Features.Accounting.AccountingService>();
        services.AddGoogleAuthentication();
        services.AddCommercialHeadAuthorization();
        services.AddScoped<ICommercialQuotationParser, XlsxCommercialQuotationParser>();
        services.AddScoped<ICommercialQuotationStore, EfCommercialQuotationStore>();
        services.AddScoped<PreviewCommercialQuotationUseCase>();
        services.AddScoped<ConfirmCommercialQuotationUseCase>();
        services.AddScoped<DownloadCommercialQuotationFileUseCase>();
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddProblemDetails();
        services.AddSingleton<IValidateOptions<FileStorageOptions>, FileStorageOptionsValidator>();
        services.AddOptions<FileStorageOptions>().BindConfiguration(FileStorageOptions.SectionName).ValidateOnStart();
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<SnG.Control.Application.Features.Budgets.Status.IBudgetQuotationStatusStore, EfBudgetQuotationStatusStore>();
        services.AddScoped<IBudgetQuotationCommercialHandoffStore, EfBudgetQuotationCommercialHandoffStore>();
        services.AddScoped<IBudgetQuotationWorkbookGenerator, XlsxBudgetQuotationWorkbookGenerator>();
        services.AddScoped<SendBudgetQuotationToCommercialUseCase>();
        services.AddScoped<DownloadCommercialQuotationUseCase>();
        services.AddScoped<IBudgetQuotationConfirmationStore, EfBudgetQuotationConfirmationStore>();
        services.AddScoped<ConfirmBudgetQuotationUseCase>();
        services.AddOptions<BudgetQuotationParserOptions>().BindConfiguration(BudgetQuotationParserOptions.SectionName)
            .Validate(options => options.MaxUploadBytes > 0 && options.MaxUncompressedBytes > 0 && options.MaxZipEntries > 0,
                "Quotation preview technical limits must be positive.").ValidateOnStart();
        services.AddControllers(options => options.Filters.Add<ActiveRoleFilter>()).AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<ClientQuotationDecisionType>(allowIntegerValues: false));
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<NotificationKind>(allowIntegerValues: false));
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<NotificationState>(allowIntegerValues: false));
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<LeadTimeStatus>(allowIntegerValues: false));
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<SystemRole>(allowIntegerValues: false));
            options.JsonSerializerOptions.Converters.Add(new ProductLineJsonConverter());
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<OperationalArea>(allowIntegerValues: false));
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<ProjectTaskType>(allowIntegerValues: false));
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<ProjectTaskStatus>(allowIntegerValues: false));
            options.JsonSerializerOptions.Converters.Add(new CommercialItemSectionJsonConverter());
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<TechnicalField>(allowIntegerValues: false));
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<CatalogItemImageStatus>(allowIntegerValues: false));
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<BudgetQuotationWarningCode>(allowIntegerValues: false));
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<SnG.Control.Application.Features.Budgets.Status.BudgetQuotationStatus>(allowIntegerValues: false));
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<SnG.Control.Application.Features.Budgets.Status.BudgetQuotationSendBlockReason>(allowIntegerValues: false));
            options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        });

        services.AddScoped<IClientStore, EfClientStore>();
        services.AddScoped<IProjectStore, EfProjectStore>();
        services.AddScoped<IProjectTaskStore, EfProjectTaskStore>();
        services.AddScoped<IBudgetsWorkflowStore, EfBudgetsWorkflowStore>();
        services.AddScoped<AssignProjectSgCodeUseCase>();
        services.AddScoped<IBudgetQuotationParser, XlsxBudgetQuotationParser>();
        services.AddScoped<IBudgetQuotationEconomicsReader, XlsxBudgetQuotationEconomicsReader>();
        services.AddScoped<PreviewBudgetQuotationUseCase>();
        services.AddScoped<SendProjectToBudgetsUseCase>();
        services.AddScoped<ListProjectTasksByAreaUseCase>();
        services.AddScoped<ListProjectTasksByProjectUseCase>();
        services.AddScoped<CreateClientUseCase>();
        services.AddScoped<GetClientByIdUseCase>();
        services.AddScoped<ListClientsUseCase>();
        services.AddScoped<CreateProjectUseCase>();
        services.AddScoped<GetProjectByIdUseCase>();
        services.AddScoped<ListProjectsUseCase>();
        return services;
    }
}
