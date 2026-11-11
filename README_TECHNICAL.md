# SnG Control Backend — Guía técnica

## Estado y stack

La solución `SnG.Control.sln` utiliza .NET 10 (`net10.0`), C#, ASP.NET Core Web API con Controllers tradicionales y Clean Architecture. El núcleo de negocio contiene `Client`, `Project`, `ProjectTask`, la versión de cotización de Presupuestos, el catálogo base histórico y la cotización comercial confirmada con sus ítems; sus reglas se describen en [README.md](README.md).

La persistencia utiliza Entity Framework Core 10 y el proveedor Npgsql para PostgreSQL. El modelo y las once migraciones, desde `InitialCreate` hasta `FormalizeCommercialQuotationMoney`, están en Infrastructure. La base esperada es **`sng_control`**, creada previamente por el administrador. Su host, puerto y credenciales deben suministrarse explícitamente.

Application implementa veintiún casos de uso: crear, obtener por Id y listar clientes y proyectos; enviar un proyecto a Presupuestos; listar tareas por área/estado y por proyecto; asignar o corregir SG; obtener un preview técnico de cotización XLSX y confirmar sus valores revisados; enviar la cotización a Comercial y descargar su Excel temporal; obtener preview comercial, confirmar su revisión y descargar el XLSX comercial final; listar notificaciones personales y el historial por proyecto; obtener el resumen gerencial y listar la operación completa. ProjectDocumentService coordina las operaciones documentales y QuotationReadService las vistas persistentes de cotizaciones. ProjectNotificationSweepService coordina adicionalmente una iteración recuperable. Los Controllers delegan en estos casos de uso, que acceden a PostgreSQL mediante interfaces específicas implementadas en Infrastructure. El preview solo consulta datos y utiliza un parser de Infrastructure, sin persistir catálogo ni documentos. La autenticación Google protege las operaciones y `/api/auth/me` consulta la identidad mediante ICurrentUser e IRoleResolver, sin persistencia ni otro caso de uso de negocio. No hay repositorios genéricos, Unit of Work propio ni Docker. La API exige configuración de autenticación, conexión y almacenamiento, y no aplica migraciones al arrancar. El worker inicia una consulta de notificaciones en segundo plano; una falla de DB se registra por tipo y se reintenta sin detener la API.

## Requisitos y paquetes

- SDK de .NET 10. No hay `global.json` que fije una versión concreta del SDK.
- Acceso a NuGet para restaurar paquetes y herramientas, o una caché local disponible.
- PostgreSQL accesible y la base existente `sng_control` para aplicar migraciones o persistir datos. Los tests y la generación de migraciones no requieren servidor ni contraseña.
- Certificado de desarrollo confiable para ejecutar el perfil HTTPS.

| Proyecto | PackageReference | Versión | Uso |
| --- | --- | --- | --- |
| Infrastructure | Microsoft.EntityFrameworkCore.Relational | 10.0.12 | Fluent API relacional y migraciones; fija también EF Core en 10.0.12 |
| Infrastructure | Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | Proveedor PostgreSQL |
| Infrastructure | DocumentFormat.OpenXml | 3.5.1 | Lectura XLSX, cachés de fórmulas e imágenes con anchors; licencia MIT |
| Api | Microsoft.EntityFrameworkCore.Design | 10.0.12 | Tooling del startup project; `PrivateAssets=all` |
| Api | Microsoft.AspNetCore.OpenApi | 10.0.12 | Documento OpenAPI en desarrollo |
| Api | Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.12 | Validación estándar de ID tokens Google mediante discovery OIDC; alineado con ASP.NET Core |
| Tests | Microsoft.NET.Test.Sdk | 17.14.1 | Ejecución de tests |
| Tests | xunit | 2.9.3 | Tests unitarios |
| Tests | xunit.runner.visualstudio | 3.1.4 | Descubrimiento y ejecución de xUnit |
| Tests | coverlet.collector | 6.0.4 | Cobertura opcional |

`Microsoft.EntityFrameworkCore` 10.0.12 y `Npgsql` 10.0.3 se resuelven transitivamente. No se duplican sus referencias en Api ni Tests. Domain y Application no dependen de EF ni de paquetes externos. No se utilizan proveedores InMemory o SQLite.

[Open XML SDK 3.5.1](https://www.nuget.org/packages/DocumentFormat.OpenXml/3.5.1), mantenido en [su repositorio oficial bajo MIT](https://github.com/dotnet/Open-XML-SDK), permite leer valores almacenados, formatos numéricos y posiciones/bytes originales de dibujos sin Office, COM ni procesos externos. Es el único paquete XLSX directo; sus dependencias transitivas las resuelve NuGet. No contiene un motor de cálculo: se usan exclusivamente los valores cacheados del archivo. Application y Domain no referencian la librería.

`dotnet-tools.json` en la raíz registra **dotnet-ef 10.0.12** como herramienta local reproducible. No se necesita una instalación global. Las versiones son estables; la dependencia relacional explícita mantiene alineados los componentes de EF Core.

## Estructura y responsabilidades

```text
dotnet-tools.json
SnG.Control.sln
src/
  Api/
    Authentication/GoogleAuthenticationOptions.cs
    Authentication/GoogleAuthenticationRegistration.cs
    Authentication/HttpContextCurrentUser.cs
    Authentication/BearerSecurityDocumentTransformer.cs
    Authorization/ManagementOptions.cs
    Authorization/ManagementRequirement.cs
    Authorization/ManagementRegistration.cs
    Authorization/BudgetsAssigneeOptions.cs
    Workers/ProjectNotificationBackgroundService.cs
    Authorization/CommercialHeadOptions.cs
    Authorization/ConfiguredCommercialHeadIdentity.cs
    Authorization/CommercialHeadRequirement.cs
    Authorization/CommercialHeadRegistration.cs
    Program.cs
    DependencyInjection.cs
    Contracts/CreateClientRequest.cs
    Contracts/CreateProjectRequest.cs
    Contracts/AssignSgCodeRequest.cs
    Contracts/ProductLineJsonConverter.cs
    Contracts/CommercialItemSectionJsonConverter.cs
    Contracts/QuotationMultipartReader.cs
    Controllers/ClientsController.cs
    Controllers/ProjectsController.cs
    Controllers/NotificationsController.cs
    Controllers/ProjectTasksController.cs
    Controllers/ProjectDocumentsController.cs
    Contracts/ProjectDocumentMultipartReader.cs
    Controllers/BudgetQuotationController.cs
    Controllers/AuthController.cs
    Controllers/ManagementController.cs
    Controllers/CommercialHandoffController.cs
    Controllers/CommercialQuotationController.cs
    Filters/DisableFormValueModelBindingAttribute.cs
    Errors/ApiExceptionHandler.cs
    Persistence/SnGControlDbContextFactory.cs
    appsettings.json
    Properties/launchSettings.json
  Application/
    Scheduling/ColombiaBusinessCalendar.cs
    Scheduling/TaskTiming.cs
    Identity/IBudgetsAssigneeIdentity.cs
    Identity/ICurrentUser.cs
    Identity/IManagementIdentity.cs
    Identity/ICommercialHeadIdentity.cs
    Files/IFileStorage.cs
    Files/IStreamingFileStorage.cs
    Features/
      Management/ManagementContracts.cs
      Management/GetManagementOverviewUseCase.cs
      Management/ListManagementTasksUseCase.cs
      Notifications/NotificationContracts.cs
      Notifications/NotificationUseCases.cs
      Notifications/ProjectNotificationPlanner.cs
      CommercialQuotations/
        CommercialQuotationContracts.cs
        ICommercialQuotationStore.cs
        PreviewCommercialQuotationUseCase.cs
        ConfirmCommercialQuotationUseCase.cs
        DownloadCommercialQuotationFileUseCase.cs
      Budgets/
        IBudgetsWorkflowStore.cs
        BudgetQuotationWorkflow.cs
        CommercialHandoff/IBudgetQuotationCommercialHandoffStore.cs
        CommercialHandoff/IBudgetQuotationWorkbookGenerator.cs
        CommercialHandoff/SendBudgetQuotationToCommercialUseCase.cs
        CommercialHandoff/DownloadCommercialQuotationUseCase.cs
        ConfirmQuotation/BudgetQuotationConfirmation.cs
        ConfirmQuotation/IBudgetQuotationConfirmationStore.cs
        ConfirmQuotation/ConfirmBudgetQuotationUseCase.cs
        AssignSg/AssignProjectSgCodeUseCase.cs
        AssignSg/AssignProjectSgCodeResult.cs
        AssignSg/AssignProjectSgCodeResponse.cs
        PreviewQuotation/IBudgetQuotationParser.cs
        PreviewQuotation/BudgetQuotationParseResult.cs
        PreviewQuotation/BudgetQuotationPreviewResponse.cs
        PreviewQuotation/PreviewBudgetQuotationResult.cs
        PreviewQuotation/PreviewBudgetQuotationUseCase.cs
      Clients/
        IClientStore.cs
        ClientResponse.cs
        Create/CreateClientUseCase.cs
        GetById/GetClientByIdUseCase.cs
        List/ListClientsUseCase.cs
      Projects/
        IProjectStore.cs
        ProjectResponse.cs
        Create/CreateProjectUseCase.cs
        GetById/GetProjectByIdUseCase.cs
        List/ListProjectsUseCase.cs
      ProjectDocuments/
        DocumentContracts.cs
        ProjectDocumentAccess.cs
        ProjectDocumentService.cs
        DocumentSizeStream.cs
      ProjectTasks/
        IProjectTaskStore.cs
        ProjectTaskResponse.cs
        SendToBudgets/SendProjectToBudgetsUseCase.cs
        SendToBudgets/SendProjectToBudgetsResult.cs
        ListByArea/ListProjectTasksByAreaUseCase.cs
        ListByProject/ListProjectTasksByProjectUseCase.cs
  Domain/
    Entities/Client.cs
    Entities/Project.cs
    Entities/ProjectNotification.cs
    Enums/NotificationKind.cs
    Enums/NotificationState.cs
    Entities/ProjectTask.cs
    Entities/BudgetQuotationVersion.cs
    Entities/CommercialCatalog.cs
    Entities/CommercialCatalogItem.cs
    Entities/CommercialCatalogItemExtraction.cs
    Entities/CommercialQuotation.cs
    Entities/CommercialQuotationItem.cs
    ValueObjects/CatalogItemValues.cs
    Enums/CommercialItemSection.cs
    Enums/CatalogItemImageStatus.cs
    Enums/ProductLine.cs
    Enums/OperationalArea.cs
    Enums/ProjectTaskType.cs
    Enums/ProjectTaskStatus.cs
  Infrastructure/
    Files/FileStorageOptions.cs
    Files/FileStorageOptionsValidator.cs
    Files/LocalFileStorage.cs
    Quotations/
      XlsxBudgetQuotationParser.cs
      XlsxCommercialQuotationParser.cs
      XlsxBudgetQuotationWorkbookGenerator.cs
      QuotationWorksheetLayout.cs
      QuotationCellReader.cs
      BudgetQuotationParserOptions.cs
      UnsupportedQuotationException.cs
    Persistence/
      SnGControlDbContext.cs
      Stores/EfClientStore.cs
      Stores/EfProjectStore.cs
      Stores/EfProjectNotificationStore.cs
      Stores/EfProjectNotificationSweepStore.cs
      Stores/EfProjectNotificationWriter.cs
      Configurations/ProjectNotificationConfiguration.cs
      Stores/EfProjectTaskStore.cs
      Stores/EfManagementReadStore.cs
      Stores/EfBudgetsWorkflowStore.cs
      Stores/EfBudgetQuotationConfirmationStore.cs
      Stores/EfBudgetQuotationCommercialHandoffStore.cs
      Stores/EfCommercialQuotationStore.cs
      Configurations/ClientConfiguration.cs
      Configurations/ProjectConfiguration.cs
      Configurations/ProjectTaskConfiguration.cs
      Configurations/BudgetQuotationVersionConfiguration.cs
      Configurations/CommercialCatalogConfiguration.cs
      Configurations/CommercialCatalogItemConfiguration.cs
      Configurations/CommercialCatalogItemExtractionConfiguration.cs
      Configurations/CommercialQuotationConfiguration.cs
      Configurations/CommercialQuotationItemConfiguration.cs
      Migrations/
        20261013143318_InitialCreate.cs
        20261013143318_InitialCreate.Designer.cs
        20261013190849_AddProjectTasks.cs
        20261013190849_AddProjectTasks.Designer.cs
        20261013192753_EnforceUniquePrepareQuotationTask.cs
        20261013192753_EnforceUniquePrepareQuotationTask.Designer.cs
        20261013203446_EnforceSgCodeFormat.cs
        20261013203446_EnforceSgCodeFormat.Designer.cs
        20261013220201_AddBudgetQuotationCatalog.cs
        20261013220201_AddBudgetQuotationCatalog.Designer.cs
        20261014141907_AddCommercialQuotationHandoff.cs
        20261014141907_AddCommercialQuotationHandoff.Designer.cs
        20261014192650_AddCommercialIdentityAssignments.cs
        20261014192650_AddCommercialIdentityAssignments.Designer.cs
        20261014201256_AddCommercialQuotationConfirmation.cs
        20261014201256_AddCommercialQuotationConfirmation.Designer.cs
        20261015141208_AddLeakTimesAndNotifications.cs
        20261015141208_AddLeakTimesAndNotifications.Designer.cs
        20261016051339_AddProjectDocumentsAndHandoff.cs
        20261016051339_AddProjectDocumentsAndHandoff.Designer.cs
        SnGControlDbContextModelSnapshot.cs
tests/
  SnG.Control.Tests/
    Domain/
    Application/
    Api/
    TestDoubles/
    Fixtures/QuotationWorkbook.cs
    Fixtures/ConfirmationScenario.cs
    Fixtures/CommercialHandoffScenario.cs
    Fixtures/CommercialQuotationScenario.cs
    Fixtures/TemporaryStorage.cs
    Storage/LocalFileStorageTests.cs
    Quotations/XlsxBudgetQuotationParserTests.cs
    Quotations/CommercialWorkbookGeneratorTests.cs
    Quotations/CommercialEconomicsTests.cs
    Persistence/SnGControlDbContextTests.cs
    Persistence/EfProjectTaskStoreTests.cs
    Persistence/EfBudgetsWorkflowStoreTests.cs
    Persistence/QuotationCatalogModelTests.cs
    Persistence/QuotationConfirmationCompensationTests.cs
    Persistence/CommercialHandoffCompensationTests.cs
    Persistence/CommercialQuotationCompensationTests.cs
    Persistence/CommercialQuotationModelTests.cs
```

| Proyecto | Responsabilidad actual |
| --- | --- |
| Domain | Entidades e invariantes, sin dependencias de persistencia o HTTP. |
| Application | Diecinueve casos de uso, respuestas e interfaces específicas de parsing, archivos y persistencia. |
| Infrastructure | Contexto EF, stores PostgreSQL, configuraciones Fluent API, migraciones, parser/generador XLSX y almacenamiento local. |
| Api | Contratos HTTP, Controllers, manejo de errores y composition root. Incluye la fábrica del tooling del startup project. |
| Tests | Tests de dominio, casos de uso, HTTP y metadata/SQL del proveedor real, sin conexión PostgreSQL. |

Referencias entre proyectos:

```text
Domain         → ninguna otra capa
Application    → Domain
Infrastructure → Application, Domain
Api            → Application, Infrastructure
Tests          → Domain, Application, Infrastructure, Api
```

## Preparación del entorno

Clonar usando la URL del repositorio facilitada por el equipo y entrar en su raíz:

```shell
git clone <URL_DEL_REPOSITORIO> SnG-Control-Backend
cd SnG-Control-Backend
dotnet tool restore
dotnet restore
```

Los comandos siguientes se ejecutan desde esa raíz.

### Conexión y secretos

La clave de configuración es `ConnectionStrings:SnGControl`. La API tiene `UserSecretsId` inicializado y carga User Secrets en `Development`. El identificador del archivo de proyecto no es una credencial.

Configurar localmente el secreto, reemplazando todos los placeholders por datos confirmados:

```shell
dotnet user-secrets set "ConnectionStrings:SnGControl" "Host=<HOST>;Port=<PUERTO>;Database=sng_control;Username=<USUARIO>;Password=<CONTRASEÑA>" --project src/Api
```

No guardar la contraseña ni una conexión real en código, migraciones, archivos versionados o documentación. User Secrets se almacena fuera del repositorio, en el perfil del usuario; no es un almacén cifrado ni una solución para producción. No hay archivos `.env` ni `appsettings.Local.json`.

Como alternativa, suministrar la variable estándar **`ConnectionStrings__SnGControl`** mediante el entorno de ejecución o su gestor de secretos. Las variables de entorno tienen prioridad sobre User Secrets. Ejemplo PowerShell con placeholders:

```powershell
$env:ConnectionStrings__SnGControl = 'Host=<HOST>;Port=<PUERTO>;Database=sng_control;Username=<USUARIO>;Password=<CONTRASEÑA>'
```

La API rechaza una conexión ausente, vacía o solo whitespace al arrancar, con un mensaje que identifica la clave requerida, sin imprimir su valor. No se presume host, puerto, usuario o contraseña. Tener configuración no demuestra que PostgreSQL esté accesible: la conexión efectiva ocurre al ejecutar una operación de base de datos.

### Autenticación Google

La API recibe `Authorization: Bearer <GOOGLE_ID_TOKEN>` y usa Microsoft.AspNetCore.Authentication.JwtBearer 10.0.12 con discovery OpenID Connect. Google es la autoridad de identidad; SnG Control aplica CommercialHead a la descarga temporal y al preview/confirmación comerciales; la descarga final autoriza al Jefe o al creador por Subject. No se usa Google.Apis.Auth ni un validador JWT propio. La librería estándar gestiona discovery, claves públicas y su actualización. Referencias: [validación de ID tokens de Google](https://developers.google.com/identity/sign-in/web/backend-auth) y [JwtBearer en ASP.NET Core](https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0).

La opción de autenticación Google es `Authentication:Google:ClientId`, vinculada a GoogleAuthenticationOptions y validada al startup: no puede ser null, vacía ni whitespace. Configurar mediante User Secrets en Development o la variable `Authentication__Google__ClientId`. Los valores siguientes son placeholders; no versionar el valor real:

```shell
dotnet user-secrets set "Authentication:Google:ClientId" "<GOOGLE_OAUTH_CLIENT_ID>" --project src/Api
```

```powershell
$env:Authentication__Google__ClientId = '<GOOGLE_OAUTH_CLIENT_ID>'
```

Este flujo no utiliza Client Secret ni dominio permitido. Google autentica; las membresías configuradas y el área activa autorizan las operaciones. Crear clientes/proyectos requiere CommercialAdvisor activo; enviar a Presupuestos exige además ownership. No hay usuarios/roles persistidos, passwords, cookies de sesión, refresh tokens ni tokens propios de SnG Control.

Authority es `https://accounts.google.com`; Audience es exclusivamente el ClientId configurado. Se aceptan los issuers oficiales `https://accounts.google.com` y `accounts.google.com`. Permanecen activados ValidateIssuer, ValidateAudience, ValidateLifetime y ValidateIssuerSigningKey; se exige firma RS256 y expiración, con tolerancia de reloj de 60 segundos. La audience no admite una barra final adicional. Discovery exige HTTPS y necesita conectividad a Google cuando obtiene o renueva sus claves; los tests no hacen esas solicitudes.

`MapInboundClaims=false` conserva `sub`, `email`, `name` y `email_verified` originales. OnTokenValidated rechaza un `sub` ausente, vacío o whitespace como fallo de autenticación: produce 401 antes de llegar al Controller. No utiliza email ni NameIdentifier como fallback. Email y name son opcionales; email_verified permanece en ClaimsPrincipal si llegó en el token, sin imponer todavía reglas de dominio o verificación del email.

Application declara `ICurrentUser` (Subject, Email, Name), `IRoleResolver` e `IActiveRoleContext` (solo Role), sin dependencias HTTP/configuración. Api implementa HttpContextCurrentUser scoped, ConfiguredRoleResolver central y HttpActiveRoleContext scoped. El resolver compara Subjects exactamente con StringComparison.Ordinal. `/api/auth/me` devuelve subject, email, name, roles (nombres de SystemRole, nunca enteros) e isManagement derivado de roles.Contains(Management). No devuelve credenciales ni el resto del JWT.

La FallbackPolicy exige autenticación. El filtro global ActiveRoleFilter exige un área validada en todos los Controllers operacionales y exceptúa únicamente AuthController para descubrir membresías. HttpActiveRoleContext acepta un solo valor exacto de X-SnG-Active-Role, rechaza números, listas, case diferente y nombres desconocidos, y comprueba pertenencia contra el Subject autenticado. No conserva estado entre requests. Las políticas de área usan ese contexto ya validado antes del Controller. Program mantiene UseAuthentication seguido de UseAuthorization; 401 sin identidad válida y 403 para área ausente, inválida, ajena o incorrecta. SaveToken=false e IncludeErrorDetails=false; nunca se registran tokens ni Authorization.

**Prueba manual:** con la API iniciada, una petición sin cabecera a `/api/auth/me` debe devolver 401. Cuando se disponga de un ID token Google obtenido legítimamente para el ClientId configurado, ejecutar desde PowerShell; conservar `$googleIdToken` y `$authHeaders` solo durante la sesión local:

```powershell
$googleIdToken = '<GOOGLE_ID_TOKEN>'
$authHeaders = @{ Authorization = "Bearer $googleIdToken" }
Invoke-RestMethod -Headers $authHeaders -Uri 'https://localhost:7265/api/auth/me'
```

Respuesta sintética ilustrativa:

```json
{ "subject": "google-test-subject", "email": "test@example.com", "name": "Test User" }
```

Los ejemplos HTTP de negocio requieren un ID token válido; para SG, preview/confirm de la cotización inicial y send-to-commercial debe pertenecer a Presupuestos; para commercial-file debe pertenecer al Jefe Comercial configurado. No registrar ni compartir tokens en logs o reportes. La validación con Google real requiere un ID token obtenido en el navegador; los tests locales usan exclusivamente identidades sintéticas. La descarga comercial final acepta también al creador original por Subject, aunque su email actual sea diferente.

Las descargas XLSX conservan el nombre persistido de la versión pertinente, saneado mediante QuotationDownloadName solo al responder (componentes de ruta eliminados, caracteres incompatibles sustituidos, nombres reservados protegidos y extensión conservada). El archivo operativo de Presupuestos es el derivado ya guardado con sus correcciones técnicas; el archivo final es el subido y confirmado por Comercial. No se regeneran bytes, no se modifica metadata histórica y se conservan los filtros de proyecto/versión y autorización.

### Identidad y autorización del Jefe Comercial

`Authentication:CommercialHeadSubject` es obligatorio al arrancar. `CommercialHeadOptions`, separado de GoogleAuthenticationOptions, se vincula a la sección Authentication y usa ValidateOnStart: null, vacío y whitespace producen `Configure Authentication:CommercialHeadSubject.`, sin imprimir el valor. No requiere email, nombre ni Client Secret.

```shell
dotnet user-secrets set "Authentication:CommercialHeadSubject" "<GOOGLE_SUBJECT>" --project src/Api/Api.csproj
```

Alternativa de entorno: `Authentication__CommercialHeadSubject=<GOOGLE_SUBJECT>`. El Subject es un string opaco: no se parsea, recorta ni cambia de case. No versionar valores reales. Cambiar OAuth Client, Workspace o el Subject configurado no reescribe identidades históricas de proyectos o tareas.

Application declara `ICommercialHeadIdentity` con una única propiedad `string Subject`. Api registra `ConfiguredCommercialHeadIdentity`, que lee IOptions<CommercialHeadOptions> validado. Application no conoce IConfiguration, IOptions ni HttpContext.

`CommercialHeadRequirement` y `CommercialHeadAuthorizationHandler` exigen identidad autenticada y ActiveRole CommercialHead validado por el resolver. Protegen GET `/api/projects/{id}/budget-quotation/commercial-file` y POST `/api/projects/{id}/commercial-quotation/preview` y `/confirm`, antes de acceder al archivo. El rol disponible por sí solo no autoriza cuando otra área está activa. Email y nombre no autorizan; el handler scoped no consulta DB ni Google.

POST send-to-commercial exige Budgets activo y tarea PrepareQuotation asignada al usuario; conserva la asignación de ReviewQuotation al Jefe configurado. Solo CommercialAdvisor activo puede crear Project; continúa siendo su creador y asesor responsable. No hay reasignación, tablas Users/UserRoles ni campos ResponsibleCommercial duplicados.

Para smoke de arranque, si falta la configuración local, puede suministrarse `Authentication__CommercialHeadSubject=google-test-subject` solo al proceso temporal. No persistirlo como identidad real. Comprobar OpenAPI público en Development, 401 en negocio sin token y fallo claro sin la nueva clave. El 403 real requiere un token Google válido de otra identidad; no copiarlo al reporte.

### Autorización de Presupuestos

Presupuestos posee el serial SG. BudgetsAssigneeRequirement y su handler scoped exigen autenticación y ActiveRole Budgets validado por IRoleResolver. AddBudgetsAssigneeAuthorization conserva las opciones validadas y ConfiguredBudgetsAssigneeIdentity para asignaciones; la resolución de membresías se concentra en ConfiguredRoleResolver. SG, preview/confirm y send-to-commercial comprueban también AssignedToGoogleSubject exacto. Una tarea legacy sin asignación se rechaza con conflicto, sin inventar responsable ni alterar fechas.

La política protege PUT `/api/projects/{id}/sg-code`, POST `/api/projects/{id}/budget-quotation/preview`, POST `/api/projects/{id}/budget-quotation/confirm` y POST `/api/projects/{id}/send-to-commercial`. Responde 401 sin autenticación y 403 para otro Subject antes del binding y del caso de uso, sin cambiar SG, tarea, StartedAt, DeadlineAt, WasOverdue ni notificaciones. La identidad configurada conserva los resultados funcionales existentes.

CommercialHead y Budgets permanecen separados incluso si sus claves tienen el mismo Subject: solo autoriza la política del área activa. Las consultas generales requieren un rol activo poseído. send-to-budgets requiere CommercialAdvisor activo y creador del proyecto; los permisos documentales del Jefe no habilitan ese handoff.

### Roles y área activa V1

El enum cerrado SystemRole reserva CommercialAdvisor, Budgets, CommercialHead, Accounting,
Engineering, Production, Logistics, Installation y Management. La configuraci?n asigna Advisor, Budgets, Head, Accounting, Installation, Engineering y Management;
Installation dispone de checklist persistido y radicación documental de medidas.

`Authentication:CommercialAdvisorSubjects` admite una lista vacía (ningún asesor), pero
rechaza entradas vacías/whitespace, espacios exteriores y duplicados ordinales. Nunca
recorta ni normaliza Subjects. Las opciones validan al arrancar con mensajes que nombran
la clave, sin imprimir su valor. Ejemplo local con placeholders:

```powershell
dotnet user-secrets set "Authentication:CommercialAdvisorSubjects:0" "<GOOGLE_SUBJECT>" --project src/Api
dotnet user-secrets set "Authentication:BudgetsAssigneeSubject" "<GOOGLE_SUBJECT>" --project src/Api
dotnet user-secrets set "Authentication:CommercialHeadSubject" "<GOOGLE_SUBJECT>" --project src/Api
dotnet user-secrets set "Authentication:ManagementSubject" "<GOOGLE_SUBJECT>" --project src/Api
```

En entorno: `Authentication__CommercialAdvisorSubjects__0` y las claves equivalentes
para los otros roles. Usar el mismo Subject otorga cuatro membresías, no cuatro áreas
activas simultáneas. El header de una llamada operacional es, por ejemplo,
`X-SnG-Active-Role: Budgets`, junto al Bearer existente. BFF valida el nombre y Backend
vuelve a resolver membresía en cada request. Auth/me funciona sin ese header. Intelligence
lo fija a Management en lecturas y descargas; mantiene compatibilidad temporal con
isManagement derivado. Los clientes anteriores deben enviar el nuevo header.

Identidad, membresía, área activa, ownership y asignación son conceptos distintos.
Cambiar área no escribe proyectos/tareas ni reinicia Lead Time. No introduce migración,
reassignment, tabla de usuarios ni caché de permisos entre requests.

### Almacenamiento local

La API exige `FileStorage:RootPath` al arrancar. Debe ser una ruta absoluta, no vacía, que exista o pueda crearse. `FileStorageOptionsValidator` valida esa configuración sin revelar el valor en sus mensajes. El usuario del proceso necesita permisos de escritura, rename y eliminación; un fallo posterior de I/O es un error de servidor.

Configurar exclusivamente en User Secrets o entorno, con una carpeta confirmada para esa instalación:

```shell
dotnet user-secrets set "FileStorage:RootPath" "<DIRECTORIO_ABSOLUTO_DE_ARCHIVOS>" --project src/Api/Api.csproj
```

Alternativa: `FileStorage__RootPath=<DIRECTORIO_ABSOLUTO_DE_ARCHIVOS>`. Las variables de entorno tienen prioridad sobre User Secrets. No versionar rutas reales ni añadir RootPath a PostgreSQL o appsettings.json. El tooling EF puede inspeccionar el modelo sin esta configuración; el arranque HTTP normal la exige, incluido cuando se utiliza solo preview.

`Application/Files/IFileStorage` conserva SaveAsync, ReadAsync y DeleteAsync para cotizaciones. IStreamingFileStorage amplía esa capacidad con SaveFromStreamAsync y OpenReadAsync; documentos usan únicamente streams. Ambas abstracciones emplean claves relativas y CancellationToken. No expone RootPath ni rutas físicas. `Infrastructure/Files/LocalFileStorage` resuelve las claves dentro de la raíz configurada, rechaza rutas absolutas, traversal, segmentos vacíos/punto, backslashes, colon y caracteres inválidos. También rechaza symlinks/junctions existentes dentro de la raíz. La carpeta debe pertenecer al servicio: no se pretende proteger frente a otro proceso privilegiado que cambie enlaces mientras se escribe.

La escritura crea directorios necesarios, escribe un temporal único con CreateNew en el directorio destino, hace flush a disco y rename al nombre final con overwrite desactivado. Si falla, intenta eliminar su temporal. DeleteAsync elimina exclusivamente la clave solicitada; no realiza limpieza global ni borra directorios ajenos. Los directorios vacíos de una operación fallida pueden permanecer.

```text
<ROOT>/projects/<projectId>/budget-quotations/<quotationVersionId>/
  original.xlsx
  confirmed.xlsx  (temporal entre envío a Comercial y confirmación comercial)
  images/<catalogItemId>.<extension>
<ROOT>/projects/<projectId>/commercial-quotations/<commercialQuotationId>/
  commercial.xlsx  (bytes exactos del archivo confirmado)
```

Las claves se generan internamente con Guid del dominio, nunca con SG, nombres de personas/proyectos o ItemNumber. El XLSX original conserva exactamente los bytes recibidos; no se vuelve a guardar con OpenXML. El generador edita únicamente la copia operativa. Su contentType es `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`. Las imágenes conservan bytes y contentType originales: PNG→`.png`, JPEG→`.jpg`, GIF→`.gif`, BMP→`.bmp`, TIFF→`.tiff`; otros tipos usan `.bin` sin conversión. El Excel operativo se descarga por HTTP después del envío a Comercial. No hay servicio HTTP de imágenes ni soporte S3/Azure.

### Compilación y tests

```shell
dotnet build
dotnet test
```

Los tests normales funcionan sin conexión configurada, sin contraseña y sin PostgreSQL levantado. Los tests del modelo instancian el contexto con `UseNpgsql()` sin cadena y consultan metadata o compilan consultas sin ejecutarlas. Los tests de Application usan dobles de `IClientStore`, `IProjectStore`, `IProjectTaskStore` e `IBudgetsWorkflowStore`, ubicados exclusivamente en Tests.

Los tests HTTP arrancan Kestrel en un puerto loopback dinámico, utilizan el registro real `AddApiServices`, los Controllers y el middleware de errores, y reemplazan los stores por dobles. Comprueban rutas, binding, validación, serialización, `Location`, códigos HTTP y ausencia de detalles internos. No necesitan paquetes de hosting de tests adicionales, Docker ni proveedores ficticios de EF. Requieren permiso del entorno para abrir un puerto local. No prueban PostgreSQL, el perfil HTTPS ni la carga de secretos de Program; esa parte se verifica por smoke test controlado con la base local.

La suite incluye pruebas habituales y smokes PostgreSQL opt-in. Cubre invariantes, fechas UTC, nombres repetidos, propagación de CancellationToken, rechazo de proyectos con cliente inexistente antes de escribir, contratos numéricos de enums, PK/FK e índices, tipos PostgreSQL, nullability y consistencia de las migraciones. Para tareas prueba creación Pending, rechazo de inserciones duplicadas de cualquier estado y área, filtros, historial vacío/inexistente, 201/404/409, parámetros requeridos y rechazo de números o nombres desconocidos. También verifica que crear Project no crea tareas y que no existe POST genérico de tareas.

ApiFixture autentica por defecto mediante el esquema TestIdentity, exclusivo de Tests: sub=google-test-subject, email=test@example.com y name=Test User. Aplica a la identidad sintética la misma regla OnTokenValidated de presencia de sub. Los tests negativos omiten la cabecera o seleccionan una identidad inválida sin cambiar los defaults de otros tests. BudgetsHttp usa el modo sintético budgets y BudgetsAssigneeSubject=budgets-test-subject, separado del cliente Http general. CommercialHeadSubject se configura como google-test-subject únicamente en Tests; other-google-subject prueba el 403 con el mismo email, y el mismo Subject con otro email conserva acceso. No se modifica la configuración productiva para permitir accesos de prueba. Las pruebas del handler JwtBearer real sustituyen discovery por configuración estática únicamente en Tests y verifican 401 para entradas ausentes o inválidas, sin Internet. También se comprueban startup de las opciones, opciones JWT, política global, CommercialHead y BudgetsAssignee, claims originales, ausencia de email como fallback, snapshots del creador, rechazo de identidad en el body, CreatedBy legacy null mediante materialización real de EF, asignación personal, DTOs y reintentos concurrentes HTTP con doble de persistencia. Los tests de compensación verifican la asignación en el ChangeTracker y un único SaveChanges; los índices y bloqueos PostgreSQL conservan la garantía de concurrencia real.

Los 66 casos de preview usan XLSX anónimos generados en memoria: bloques reales/vacíos, Main/Additional, números repetidos y formatos con ceros, orden documental, etiquetas, fórmulas cacheadas sin cálculo, M2, imágenes por anchor y sus warnings, SG, archivos inválidos y límites técnicos. Application prueba todos los requisitos del workflow con stores cuyas operaciones de escritura fallan si se invocan. Los tests multipart utilizan el parser real y comprueban el contrato completo y sus errores, sin datos reales de clientes.

Los tests de SG cubren normalización y dígitos ASCII, ceros iniciales, actualización de fechas, Start, primera asignación, corrección e idempotencia, conflictos de workflow y unicidad, request HTTP estricto, CHECK y traducción específica de errores. `EfBudgetsWorkflowStoreTests` comprueba que ambos cambios llegan a una sola llamada SaveChanges, la precisión de timestamps y el detach de ambas entidades sin limpiar entidades ajenas. La transacción y los bloqueos se validan con PostgreSQL real. El doble de workflow usa copias de entidades para no confundir cambios fallidos con datos guardados; la reflexión de copiado se limita a Tests. Los escenarios InProgress usan Start; Complete permite finalizar PrepareQuotation mediante el envío a Comercial.

`EfProjectTaskStoreTests` usa el ChangeTracker real y un contexto de test que sustituye únicamente SaveChangesAsync, sin conexión ni paquetes adicionales. Verifica éxito y token, traducción exclusiva de SQLSTATE 23505 con el índice esperado, propagación de otros constraints/códigos/errores, detach de la entidad rechazada y conservación de otras entidades tracked. Estos tests comprueban la traducción y limpieza; la garantía de concurrencia se valida además contra PostgreSQL real mediante el smoke descrito más abajo.

Cobertura opcional:

```shell
dotnet test --collect:"XPlat Code Coverage"
```

Los artefactos de compilación y `TestResults` están excluidos por `.gitignore`.

## Contexto y materialización

`SnGControlDbContext` está en `SnG.Control.Infrastructure.Persistence`. Recibe `DbContextOptions<SnGControlDbContext>`, expone trece DbSets: `Clients`, `Projects`, `ProjectTasks`, `BudgetQuotationVersions`, `CommercialCatalogs`, `CommercialCatalogItems`, `CommercialCatalogItemExtractions`, `CommercialQuotations`, `CommercialQuotationItems`, `ProjectNotifications`, `ProjectDocuments`, `ProjectDocumentHandoffs` y `ProjectDocumentHandoffItems`. Aplica explícitamente las trece configuraciones IEntityTypeConfiguration correspondientes.

Las propiedades tienen setters privados. EF enlaza constructores y restaura los valores persistidos. Project y las seis entidades de cotización/catálogo tienen constructor privado sin parámetros reservado para materialización; ProjectTask y ProjectNotification también tienen constructor privado para EF; Client usa constructor enlazado. El constructor público de Project exige CreatedByGoogleSubject no vacío, mientras EF puede materializar proyectos históricos con los tres campos de creador null. Los tests compilan consultas de los DbSets y prueban esa materialización con IStructuralTypeMaterializerSource, sin conexión.

Los identificadores nacen de `Guid.NewGuid()` en el dominio y se mapean con `ValueGeneratedNever()`: no hay generación de UUID en PostgreSQL. Las fechas son `DateTime` UTC; `CreatedAt` y `UpdatedAt` coinciden al crear clientes o proyectos. Los timestamps e identificadores almacenados se conservan al materializar entidades; no hay mecanismo automático de actualización de fechas.

`ProjectTask(projectId, area, type, assignedToGoogleSubject = null, assignedAtUtc = null)` exige un ProjectId no vacío y enums definidos. Genera Id; los flujos pasan AssignedAt UTC desde TimeProvider. El constructor conserva la fecha actual como compatibilidad si no se proporciona. Fija Status=Pending y StartedAt/CompletedAt/DeadlineAt/WasOverdue=null. Start(startedAtUtc, deadlineAtUtc) exige UTC, deadline posterior y ausencia de inicio, fin o seguimiento previos; cambia a InProgress, fija ambas fechas y WasOverdue=false; rechaza InProgress y Completed sin reiniciar fechas. Complete(completedAtUtc) exige UTC, InProgress, StartedAt presente, fin no anterior al inicio y CompletedAt null; cambia a Completed, conserva AssignedAt/StartedAt/DeadlineAt y actualiza WasOverdue sin revertir true. No hay Return/Cancel ni endpoint genérico de finalización. Sus once propiedades tienen setters privados. AssignedToGoogleSubject admite null; un valor no null debe ser no vacío y se conserva exactamente. PrepareQuotation se crea asignada a BudgetsAssigneeSubject y el envío a Comercial asigna las nuevas ReviewQuotation al Subject configurado. `Project` no tiene estado global ni colección Tasks.

`Project.SetSgCode` rechaza null/vacío/whitespace, aplica Trim y ToUpperInvariant y exige SG seguido de al menos un dígito ASCII. La validación recorre los caracteres, equivalente a `^SG[0-9]+$` sobre la entrada normalizada completa; no acepta dígitos Unicode ni caracteres adicionales. Conserva ceros iniciales. Si el valor es distinto, modifica SgCode y UpdatedAt UTC; si es igual no modifica nada. CreatedAt permanece intacto y no existe generación de consecutivos.

Los proyectos y carpetas bajo `src` se llaman `Api`, `Application`, `Domain` e `Infrastructure`; cada archivo de proyecto tiene el mismo nombre que su carpeta. `RootNamespace` mantiene el prefijo `SnG.Control` en los namespaces del código y de las migraciones. Se utilizan nullable reference types e implicit usings. Las validaciones siguen utilizando `ArgumentNullException`, `ArgumentException` y `ArgumentOutOfRangeException`. Domain no contiene DataAnnotations de persistencia, referencias EF, colecciones de navegación ni dependencias hacia Infrastructure.

## Tablas y restricciones

No hay tablas Users/Employees ni roles persistidos. La identidad necesaria para el negocio se guarda en Project, ProjectTask y los snapshots de auditoría de CommercialQuotation; el modelo tiene diez migraciones.

Se usan nombres explícitos `Clients`, `Projects` y `ProjectTasks`, con columnas PascalCase; el SQL PostgreSQL generado utiliza identificadores entre comillas. No se introduce un esquema personalizado.

| Tabla | Columnas y tipos | Nullability |
| --- | --- | --- |
| Clients | `Id`: uuid; `Name`: text; `CreatedAt`, `UpdatedAt`: timestamp with time zone | Todas NOT NULL |
| Projects | `Id`, `ClientId`: uuid; `Name`, `City`, `SgCode`, `CreatedByGoogleSubject`, `CreatedByEmail`, `CreatedByName`: text; `ProductLine`: integer; `CreatedAt`, `UpdatedAt`: timestamp with time zone | `SgCode` y los tres campos CreatedBy admiten NULL para compatibilidad histórica |
| ProjectTasks | `Id`, `ProjectId`: uuid; `AssignedToGoogleSubject`: text; `Area`, `Type`, `Status`: integer; `AssignedAt`, `StartedAt`, `CompletedAt`, `DeadlineAt`: timestamp with time zone; `WasOverdue`: boolean | `StartedAt`, `CompletedAt`, `DeadlineAt`, `WasOverdue` y `AssignedToGoogleSubject` admiten NULL |

- `PK_Clients` sobre `Clients.Id` y `PK_Projects` sobre `Projects.Id`.
- `FK_Projects_Clients_ClientId`: `Projects.ClientId` referencia `Clients.Id`, relación 1:N sin navegaciones, con `ON DELETE RESTRICT`. No se eliminan proyectos en cascada.
- `IX_Projects_ClientId`: índice no único de la FK.
- `IX_Projects_SgCode`: índice UNIQUE estándar, sin filtro. PostgreSQL permite múltiples NULL y rechaza valores no nulos repetidos.
- `CK_Projects_SgCode_Format`: CHECK `"SgCode" IS NULL OR "SgCode" ~ '^SG[0-9]+$'`. Rechaza códigos externos sin normalizar o con formato inválido. SgCode conserva su tipo text y nullability; el CHECK no normaliza datos ni usa triggers.
- No hay índices únicos sobre nombres, longitudes máximas de texto, defaults de identidad/fechas ni datos seed.
- `ProductLine` se convierte a integer: **Classic=1, Essential=2, Bioconfort=3, Signature=4**. Estos valores forman parte del contrato de datos y no deben renumerarse. El constructor rechaza valores de enum no definidos; no se añade una restricción CHECK en esta fase.

`ProjectTasks` tiene `PK_ProjectTasks` sobre Id y `FK_ProjectTasks_Projects_ProjectId` hacia Projects.Id, relación 1:N requerida sin navegaciones y con `ON DELETE RESTRICT`. No hay borrado en cascada. Conserva los índices no únicos `IX_ProjectTasks_ProjectId`, para consultar todas las tareas del proyecto independientemente de su tipo, e `IX_ProjectTasks_Area_Status`.

El índice **`UX_ProjectTasks_ProjectId_PrepareQuotation`** es único sobre ProjectId con filtro exacto **`"Type" = 1`**. Impide dos tareas PrepareQuotation para el mismo proyecto, independientemente de área o estado, incluso ante inserciones concurrentes. No se usa UNIQUE(ProjectId, Type): cada filtro limita la regla a su tipo y no impone unicidad a otros tipos futuros. Fluent API utiliza un índice con nombre propio en el modelo para conservar también el índice no único sobre ProjectId. El nombre se comparte entre configuración y detección de conflictos mediante una constante interna de Infrastructure.

`UX_ProjectTasks_ProjectId_ReviewQuotation` aplica la misma garantía a ReviewQuotation con filtro `"Type" = 2`, independientemente del área o estado. Su nombre explícito también se comparte con el detector de conflictos del store de envío a Comercial. `UX_ProjectTasks_ProjectId_PresentQuotationToClient` protege la presentación con filtro `"Type" = 3` y comparte su nombre con el store de confirmación comercial.

Los enums de tarea se convierten explícitamente a integer y conservan estos contratos estables:

| Enum | Valores |
| --- | --- |
| OperationalArea | Commercial=1, Budgets=2 |
| ProjectTaskType | PrepareQuotation=1, ReviewQuotation=2, PresentQuotationToClient=3 |
| ProjectTaskStatus | Pending=1, InProgress=2, Completed=3 |

Domain valida los valores; no hay restricciones CHECK ni valores predeterminados de base para estos campos.

### Cotización y catálogo comercial persistidos

El snapshot histórico de Presupuestos ocupa cuatro tablas, sin JSON/JSONB, bytea ni columnas económicas:

| Tabla/entidad | Campos principales y relación |
| --- | --- |
| BudgetQuotationVersions | Id, ProjectId, VersionNumber, OriginalFileName, OriginalFileStorageKey, ConfirmedFileStorageKey nullable, OriginalFileContentType, DocumentClientName, DocumentProjectName, DocumentLocation, ClientLine, ProductionLine, PreparedBy, DocumentSgCode, ConfirmedAt. FK requerida hacia Project. |
| CommercialCatalogs | Id, BudgetQuotationVersionId y CreatedAt. FK única hacia la versión: relación 1:1. |
| CommercialCatalogItems | Id, CommercialCatalogId, Section, SourceRow y los once valores confirmados. Relación N:1 al catálogo. |
| CommercialCatalogItemExtractions | CatalogItemId como PK/FK 1:1 al ítem; los once valores extraídos, ImageStatus, ImageStorageKey e ImageContentType. |

Los once valores técnicos son ItemNumber, Location, System, Glass, AluminumFinish, LockType, Observations, Width, Height, SquareMeters y Quantity. Se guardan como columnas explícitas en ambas tablas. Textos y claves son `text`, medidas son `numeric` sin escala artificial, cantidad/fila/versión/enums son `integer`, identificadores son `uuid` y fechas son `timestamp with time zone`. ConfirmedFileStorageKey, ImageStorageKey e ImageContentType admiten null en estas tablas. No se almacena RootPath ni el contenido binario de archivos.

Domain genera los Guid, conserva setters privados y fechas UTC a precisión de microsegundos. `CatalogItemValues` concentra la validación común sin ser una entidad EF: ItemNumber no vacío y recortado; textos opcionales null se normalizan a cadena vacía, con Trim exterior y contenido interno intacto; medidas/cantidad positivas, sin recalcular m². Las entidades contienen propiedades escalares; sus constructores privados sin parámetros se reservan para materialización EF. SourceRow debe ser positivo y Section definido. El orden se recupera mediante SourceRow; ItemNumber puede repetirse y cambiarse en la confirmación.

`CommercialItemSection`: Main=1, Additional=2. `CatalogItemImageStatus`: Stored=1, Missing=2, Ambiguous=3. Una imagen Stored exige clave relativa y contentType; Missing/Ambiguous exigen ambos null. El estado pertenece al snapshot de extracción. No se persisten mensajes libres de warnings. DocumentSgCode es el SG normalizado extraído durante la confirmación y no cambia cuando se corrige posteriormente Project.SgCode.

Todas las FK nuevas utilizan Restrict. Los índices son:

- `UX_BudgetQuotationVersions_ProjectId_VersionNumber`: UNIQUE(ProjectId, VersionNumber), autoridad de concurrencia.
- `IX_BudgetQuotationVersions_OriginalFileStorageKey`: UNIQUE sobre la clave del XLSX.
- `IX_BudgetQuotationVersions_ConfirmedFileStorageKey`: UNIQUE nullable sobre el archivo operativo; permite múltiples null.
- `IX_CommercialCatalogs_BudgetQuotationVersionId`: UNIQUE, un catálogo por versión.
- `IX_CommercialCatalogItems_CommercialCatalogId`: consulta de ítems del catálogo.
- `IX_CommercialCatalogItems_CommercialCatalogId_SourceRow`: UNIQUE, una fila fuente por catálogo.
- `IX_CommercialCatalogItemExtractions_ImageStorageKey`: UNIQUE nullable; permite varios null.

La extracción utiliza PK/FK CatalogItemId y no necesita otra identidad. No hay índices únicos sobre ItemNumber. CHECKs protegen versión positiva, fila positiva, Section definido, medidas/cantidades positivas, ItemNumber no vacío y consistencia del estado de imagen. Domain admite VersionNumber positivo, pero Application crea exclusivamente **1**; no existe operación para versión 2, reemplazo o edición del catálogo.

## Migraciones

`20261016051339_AddProjectDocumentsAndHandoff` crea ProjectDocuments, ProjectDocumentHandoffs y ProjectDocumentHandoffItems, con PK/FK Restrict, unicidad de StorageKey y de handoff por proyecto, índices de vigentes/histórico, constraints de tamaño/textos/estado y relaciones compuestas que impiden referencias entre proyectos. Amplía NotificationKind a 5; la restricción de estado existente ya exige Informational para todo tipo distinto de Overdue. No modifica migraciones anteriores, no hace backfill y no se aplica al arrancar. Debe revisarse y aplicarse explícitamente antes de operar esta funcionalidad; la validación aislada no actualiza la base normal.

`20261015141208_AddLeakTimesAndNotifications` agrega DeadlineAt (`timestamp with time zone`) y WasOverdue (`boolean`) nullable a ProjectTasks, sin defaults ni backfill. Crea ProjectNotifications, sus restricciones y cuatro índices. Las ocho migraciones anteriores permanecen intactas. Down elimina exclusivamente la tabla nueva y ambas columnas.

`20261014201256_AddCommercialQuotationConfirmation` crea CommercialQuotations y CommercialQuotationItems y el índice parcial único PresentQuotationToClient (`Type = 3`). Conserva las siete migraciones anteriores, sin backfill ni modificaciones a tablas históricas salvo el índice de tareas. Las nuevas FK usan Restrict. Down elimina solo ambas tablas nuevas y ese índice.

`20261014192650_AddCommercialIdentityAssignments` agrega exclusivamente cuatro columnas text nullable: Projects.CreatedByGoogleSubject, CreatedByEmail y CreatedByName, y ProjectTasks.AssignedToGoogleSubject. No tiene defaults, backfill, datos reales ni índices nuevos; mantiene constraints y las seis migraciones anteriores. Down elimina solo esas cuatro columnas. Los proyectos y ReviewQuotation históricos pueden mantener identidad null. Toda creación nueva de Project desde Domain/Application exige Subject; las nuevas ReviewQuotation del envío reciben el Subject configurado. No se inventa identidad histórica.

`20261014141907_AddCommercialQuotationHandoff` agrega únicamente ConfirmedFileStorageKey nullable (`text`), su índice único y `UX_ProjectTasks_ProjectId_ReviewQuotation`, UNIQUE(ProjectId) con filtro `"Type" = 2`. Mantiene el índice de PrepareQuotation y las cinco migraciones anteriores intactas. Su Down revierte exclusivamente estas tres incorporaciones.

`20261013220201_AddBudgetQuotationCatalog` crea únicamente BudgetQuotationVersions, CommercialCatalogs, CommercialCatalogItems y CommercialCatalogItemExtractions, con sus FK, índices y CHECKs. Las cuatro migraciones anteriores no se modifican. El snapshot incorpora el nuevo modelo; RootPath no forma parte del esquema. Su Down elimina las cuatro tablas nuevas en orden de dependencias.

`20261013143318_InitialCreate` crea Clients y Projects, sus PK, FK e índices. `20261013190849_AddProjectTasks` agrega ProjectTasks, su PK, FK e índices de consulta. `20261013192753_EnforceUniquePrepareQuotationTask` agrega el índice único parcial. Las tres permanecen intactas, incluidos sus Designers y modelos históricos. `20261013203446_EnforceSgCodeFormat`, generada mediante EF tooling, agrega únicamente el CHECK de SG; no cambia columnas, relaciones ni ProjectTasks. El snapshot vigente incluye estas restricciones. Los métodos `Down` contienen la reversión generada por EF; no forman parte del procedimiento normal de instalación. EF administra además su tabla técnica `__EFMigrationsHistory` al aplicar migraciones.

No se utilizan `EnsureCreated()` ni migraciones automáticas en startup. No crear tablas manualmente, borrar o recrear la base. Antes de aplicar, confirmar que la conexión apunta a la base **existente** `sng_control` y que el usuario tiene permisos sobre ella. El tooling de EF puede intentar crear una base inexistente; no ejecutarlo contra un destino sin verificar.

La fábrica `SnG.Control.Api.Persistence.SnGControlDbContextFactory` implementa `IDesignTimeDbContextFactory<SnGControlDbContext>` y utiliza la configuración estándar de ASP.NET Core. Permite configurar únicamente el proveedor cuando no existe conexión, para generar/inspeccionar migraciones offline sin credenciales ficticias. Esa excepción se limita al tooling; el arranque normal sigue exigiendo la cadena. Los comandos incluyen `-- --environment Development` para cargar los User Secrets de la API de forma explícita.

### Inspeccionar sin conectarse

```shell
dotnet ef migrations list --no-connect --project src/Infrastructure --startup-project src/Api -- --environment Development
dotnet ef migrations has-pending-model-changes --project src/Infrastructure --startup-project src/Api -- --environment Development
dotnet ef migrations script 0 AddProjectDocumentsAndHandoff --project src/Infrastructure --startup-project src/Api -- --environment Development
dotnet ef migrations script AddLeakTimesAndNotifications AddProjectDocumentsAndHandoff --project src/Infrastructure --startup-project src/Api -- --environment Development
```

### Aplicar a la base existente

Después de configurar y verificar la conexión, si la base ya tiene ProjectTasks, comprobar duplicados antes de aplicar el índice:

```sql
SELECT "ProjectId", COUNT(*)
FROM "ProjectTasks"
WHERE "Type" = 1
GROUP BY "ProjectId"
HAVING COUNT(*) > 1;
```

Si devuelve filas, detener la aplicación de la migración, reportar los ProjectId afectados y esperar una decisión sobre esos datos. No borrar ni escoger registros automáticamente. La migración no sanea datos y el índice rechazará duplicados que aparezcan entre la comprobación y su creación.

El índice de PrepareQuotation se define así:

```sql
CREATE UNIQUE INDEX "UX_ProjectTasks_ProjectId_PrepareQuotation"
ON "ProjectTasks" ("ProjectId") WHERE "Type" = 1;
```

Antes de aplicar EnforceSgCodeFormat, comprobar también los SG existentes:

```sql
SELECT "Id", "SgCode" FROM "Projects"
WHERE "SgCode" IS NOT NULL AND "SgCode" !~ '^SG[0-9]+$';
```

Si aparecen filas, detenerse, reportar los proyectos afectados y esperar decisión. No normalizar, corregir ni borrar automáticamente. El SQL de esta migración agrega exclusivamente:

```sql
ALTER TABLE "Projects" ADD CONSTRAINT "CK_Projects_SgCode_Format"
CHECK ("SgCode" IS NULL OR "SgCode" ~ '^SG[0-9]+$');
```

Con las comprobaciones sin datos incompatibles:

```shell
dotnet ef database update --project src/Infrastructure --startup-project src/Api -- --environment Development
```

Comprobar que el comando termina correctamente y verificar el estado aplicado:

```shell
dotnet ef migrations list --project src/Infrastructure --startup-project src/Api -- --environment Development
```

El modelo tiene exactamente diez migraciones, en orden:

1. `20261013143318_InitialCreate`
2. `20261013190849_AddProjectTasks`
3. `20261013192753_EnforceUniquePrepareQuotationTask`
4. `20261013203446_EnforceSgCodeFormat`
5. `20261013220201_AddBudgetQuotationCatalog`
6. `20261014141907_AddCommercialQuotationHandoff`
7. `20261014192650_AddCommercialIdentityAssignments`
8. `20261014201256_AddCommercialQuotationConfirmation`
9. `20261015141208_AddLeakTimesAndNotifications`
10. `20261016051339_AddProjectDocumentsAndHandoff`
11. `20261027211911_FormalizeCommercialQuotationMoney`

Cada instalación debe comprobar su historial en `__EFMigrationsHistory` antes de operar. Los comandos de inspección offline no verifican si una base concreta tiene migraciones aplicadas.

### Crear migraciones futuras

Solo después de aprobar el nuevo alcance y ajustar el modelo:

```shell
dotnet ef migrations add <NombreDescriptivo> --project src/Infrastructure --startup-project src/Api --output-dir Persistence/Migrations -- --environment Development
```

Revisar la migración y el snapshot: columnas, tipos, nullability, claves, índices y comportamiento de borrado. Si una migración todavía no aplicada es incorrecta, corregir la configuración y regenerarla mediante tooling. No editar como primera opción el SQL o el snapshot a mano ni alterar migraciones aplicadas. Compilar y ejecutar tests antes de aplicar.

## Ejecutar la API

Con ClientId Google, CommercialHeadSubject, BudgetsAssigneeSubject, ManagementSubject, conexión y almacenamiento configurados y, para persistir datos, las migraciones aplicadas:

```shell
dotnet dev-certs https --trust
dotnet run --project src/Api --launch-profile https
```

El perfil `https` usa `https://localhost:7265` y `http://localhost:5051`, en `Development`, con redirección HTTP a HTTPS. Detener con `Ctrl+C`. También existe el perfil `http` en 5051, sin puerto HTTPS propio; la redirección puede advertir que no encuentra ese puerto. Se recomienda el perfil `https`.

En desarrollo, `GET /openapi/v1.json` publica OpenAPI sin autenticación mediante AllowAnonymous explícito: la política fallback también alcanzaría ese endpoint. El documento describe los endpoints vigentes de negocio y `/api/auth/me`, con esquema HTTP Bearer y requisito global de seguridad. BearerSecurityDocumentTransformer usa el paquete OpenAPI existente, sin librerías adicionales. No hay Swagger UI ni Minimal APIs de negocio. Fuera de desarrollo no se publica OpenAPI. Los cuerpos multipart del preview y confirmación se leen manualmente; sus contratos de carga se describen más abajo y no se infiere de un parámetro IFormFile.

- `Program.cs`: valida la presencia de conexión, registra `SnGControlDbContext` scoped con Npgsql, llama `AddApiServices` y registra OpenAPI; configura manejo de excepciones, HTTPS, autenticación, autorización y Controllers.
- `DependencyInjection.cs`: `AddApiServices` registra autenticación Google, política fallback, ICurrentUser, opciones y providers del Jefe Comercial, Presupuestos y Gerencia con sus políticas específicas, además de los veintiún casos de uso, los doce stores, el writer de notificaciones, IFileStorage, ambos parsers y el generador con lifetime scoped, Controllers, opciones JSON, límites técnicos validados al arrancar y manejo de errores. Application no necesita depender de una librería de DI.
- `appsettings.json`: logging (`Information` general y `Warning` para ASP.NET Core) y `AllowedHosts=*`; no contiene conexiones.
- `Properties/launchSettings.json`: perfiles/puertos locales y `ASPNETCORE_ENVIRONMENT=Development`.
- El registro del contexto no aplica migraciones. El worker registrado ejecuta su primer sweep en segundo plano al arrancar.

## Casos de uso y persistencia

El flujo de datos persistidos es `HTTP → Controller → UseCase → Store EF → PostgreSQL`; el preview añade una llamada al parser XLSX después de consultar el workflow. Application está organizada verticalmente en `Features/Clients` y `Features/Projects`, cada una con `Create`, `GetById` y `List`; `Features/ProjectTasks`, con `SendToBudgets`, `ListByArea` y `ListByProject`; `Features/CommercialQuotations`, con preview, confirmación y descarga final; y `Features/Budgets`, con `AssignSg`, `PreviewQuotation`, `ConfirmQuotation` y `CommercialHandoff`. Los casos de uso son clases explícitas con `ExecuteAsync`, sin MediatR ni interfaces por caso de uso.

`IClientStore` declara `AddAsync`, `GetByIdAsync`, `ListAsync` y `ExistsAsync`; `IProjectStore` declara los tres primeros. Las interfaces viven junto a su feature en Application y trabajan con entidades del dominio, sin exponer EF, `IQueryable` ni detalles de PostgreSQL.

`EfClientStore` y `EfProjectStore` implementan esas operaciones con el contexto existente. `AddAsync` agrega la entidad y ejecuta `SaveChangesAsync` antes de completar; no se requiere un método separado de commit. Todas las lecturas usan `AsNoTracking` y operaciones asíncronas. No se define orden de listado. Controllers, casos de uso y stores propagan `CancellationToken`.

Crear cliente construye la entidad validada, la guarda y mapea `ClientResponse`. CreateProjectUseCase obtiene Subject, Email y Name exclusivamente de ICurrentUser y construye la entidad validada; verifica explícitamente la existencia del cliente con `ExistsAsync`, guarda y mapea `ProjectResponse`. Devuelve `null` si el cliente no existe, sin intentar guardar ni esperar un error de FK. El Subject se conserva exactamente; Email y Name son snapshots informativos nullable, con empty/whitespace normalizados a null, sin actualizaciones posteriores. El creador es el responsable comercial actual, sin campos duplicados ni reasignación. Get/List de clientes y proyectos reciben ProjectReadAccess y ejecutan GetReadableByIdAsync/ListReadableAsync. ProjectReadScope construye un predicado reutilizado por EF y por los estados documentales/cotizaciones: Advisor propietario, Budgets con PrepareQuotation asignada, Head con ReviewQuotation asignada y Management transversal. No filtra por estado de tarea. Clientes se restringen mediante EXISTS de proyecto accesible para Budgets/Head; Advisor conserva el registro compartido y Management lee todos. Consultas SQL AsNoTracking, sin migración ni nuevas columnas. Obtener por Id devuelve `null`/404 si no existe o queda fuera del ámbito; listar devuelve una colección vacía cuando no hay registros visibles.

ListByArea aplica TaskInboxScope por rol/tipo/área/Subject, además de Status y el ámbito de lectura del proyecto. ListByProject y el historial de notificaciones comprueban acceso al proyecto antes de consultar. Documentos y vistas persistentes mantienen su 403 de acceso denegado; snapshots inexistentes mantienen 404. El estado de Presupuestos y las descargas XLSX también filtran antes de devolver metadata o leer storage. Las escrituras conservan políticas de área, assignment y workflow; pertenecer a varias áreas nunca combina permisos en una petición.

Las respuestas de Application se mapean explícitamente con `FromEntity`, compartido por los casos de uso de cada feature. Los Controllers reciben requests propios de Api y devuelven esos DTO de respuesta, nunca entidades del dominio. No hay AutoMapper, repositorios genéricos ni Unit of Work adicional.

### Envío y consultas de tareas

`Features/ProjectTasks` contiene `IProjectTaskStore`, `ProjectTaskResponse` y los casos de uso agrupados en `SendToBudgets`, `ListByArea` y `ListByProject`. La interfaz declara únicamente `TryAddPrepareQuotationAsync`, `ListByAreaAsync` y `ListByProjectAsync`. La operación de inserción devuelve true si la tarea se guardó y false si ya existe PrepareQuotation para el proyecto. `EfProjectTaskStore` utiliza el mismo DbContext scoped; guarda con SaveChangesAsync, lee con AsNoTracking y propaga CancellationToken en todas las operaciones.

`SendProjectToBudgetsUseCase` delega el envío formal en `IProjectDocumentStore.SendToBudgetsAsync`. El store bloquea Project y revalida: inexistente → ProjectNotFound; actor sin CommercialAdvisor activo o distinto del creador → Forbidden; PrepareQuotation o snapshot existentes → AlreadySent; ningún documento vigente → DocumentsRequired. El éxito crea snapshot exacto, tarea Pending asignada a Presupuestos y TaskAssigned en una transacción/SaveChanges. No inicia StartedAt ni DeadlineAt. Crear Project no invoca este caso de uso.

`ListProjectTasksByAreaUseCase` valida los enums y construye TaskInboxScope con Subject y rol activo validados. La regla cerrada permite Advisor → Commercial/PresentQuotationToClient, Budgets → Budgets/PrepareQuotation y Head → Commercial/ReviewQuotation. Management y roles no implementados carecen de bandeja personal. Un área incompatible retorna null y el Controller responde 403 antes de consultar el store; nombres de enums inválidos mantienen 400. EF filtra Type, Area, AssignedToGoogleSubject exacto y Status, junto con el ProjectReadScope existente. La regla no depende de email ni del resto de membresías. Completed conserva consulta histórica propia. `ListProjectTasksByProjectUseCase` verifica primero el proyecto: devuelve null si no existe, colección vacía si no tiene tareas o todas sus tareas en otro caso. No hay paginación ni orden garantizado en ninguna consulta.

PostgreSQL conserva el índice único parcial de PrepareQuotation y la unicidad de ProjectDocumentHandoffs.ProjectId. Las operaciones documentales y el envío se serializan con SELECT FOR UPDATE sobre Project antes de consultar documentos/tarea; así un segundo envío ve el primero confirmado y responde AlreadySent. El método de inserción aislada de EfProjectTaskStore conserva su detector específico de 23505, pero ya no es la ruta del handoff HTTP.

## Documentos del proyecto y handoff inicial

`Features/ProjectDocuments` contiene los contratos y ProjectDocumentService/ProjectDocumentAccess; Api implementa ProjectDocumentsController y ProjectDocumentMultipartReader. EfProjectDocumentStore coordina la persistencia y LocalFileStorage implementa streaming. Los parsers de cotizaciones permanecen independientes.

ProjectDocumentAccess reutiliza ProjectReadScope: CommercialAdvisor modifica/consulta solo proyectos propios; CommercialHead consulta con ReviewQuotation asignada; Budgets consulta con PrepareQuotation asignada, incluso legacy o Completed; Management consulta transversalmente antes/después del handoff y siempre sin escritura. El Jefe no carga, reemplaza ni quita documentación general. CanSendToBudgets solo corresponde al Asesor activo propietario y mantiene las condiciones documentales del workflow. Upload verifica acceso antes de leer el body y revalida bajo el lock existente.

| Operación | Contrato / resultado |
| --- | --- |
| POST `/api/projects/{id}/documents/batch` | multipart; 201 DocumentBatchResponse |
| GET `/api/projects/{id}/documents` | 200 DocumentListResponse |
| DELETE `/api/projects/{id}/documents/{documentId}` | 204; solo vigente antes del handoff |
| GET `/api/projects/{id}/documents/{documentId}/file` | 200 attachment, application/octet-stream |
| GET `/api/projects/{id}/documents/current.zip` | 200 ZIP de vigentes |
| GET `/api/projects/{id}/documents/initial-handoff.zip` | 200 ZIP del snapshot; 409 InitialHandoffUnavailable en legacy |

Todos exigen autenticación (401 si falta). Los DTO documentales omiten StorageKey y Subjects. DocumentListResponse contiene `documents`, `initialHandoff`, `canModify`, `canRemove` y `canSendToBudgets`; cada documento contiene id, originalFileName, contentType, sizeBytes, uploadedAt, uploadedBy (email/name), uploadBatchId, isCurrent, replacedAt, replacedByDocumentId e inInitialHandoff. El handoff expone id, sentAt, sentBy (email/name) y documentIds. Batch devuelve uploadBatchId y documentos creados. Las capacidades son informativas; el servidor siempre vuelve a autorizar.

### Multipart y límites

La primera parte debe ser `manifest`, Content-Type application/json UTF-8, máximo **16 384 bytes**, profundidad JSON máxima 8, sin propiedades desconocidas. Contiene entre 1 y 15 archivos, con partName de 1–64 letras/dígitos ASCII o guiones, único y distinto de manifest. Las partes de archivo siguientes pueden llegar en cualquier orden y deben corresponder exactamente al manifest. No se permiten partes extra, faltantes, duplicadas, replacements repetidos, Guid vacío ni archivos vacíos. El manifest puede enviarse como Blob con filename; ese nombre se ignora.

```json
{
  "files": [
    { "partName": "file-0", "replacesDocumentId": null },
    { "partName": "file-1", "replacesDocumentId": "<DOCUMENT_ID_VIGENTE>" }
  ]
}
```

```shell
curl.exe -H "Authorization: Bearer <TOKEN_CREADOR_O_JEFE>" -F "manifest=@manifest.json;type=application/json" -F "file-0=@<ARCHIVO_NUEVO>" -F "file-1=@<ARCHIVO_REEMPLAZO>" "https://localhost:7265/api/projects/<PROJECT_ID>/documents/batch"
```

ProjectDocument declara MaxFileBytes=100_000_000, MaxBatchBytes=300_000_000 y MaxCurrentDocuments=15. DocumentSizeStream cuenta bytes realmente leídos; los límites exactos son válidos y excederlos produce 413 FileTooLarge/BatchTooLarge. El límite HTTP es 301_000_000 para permitir hasta 1 MB de overhead multipart; el contenido de archivos sigue limitado a 300 MB. Headers por parte: máximo 16 y 8192 bytes; boundary máximo 128. No se utiliza IFormFile ni se acumulan los archivos en memoria. El buffer de manifest es pequeño; la copia usa buffers de 81 920 bytes.

Se admite cualquier extensión/formato sin inspección de contenido. OriginalFileName es metadata Unicode no vacía de hasta 255 caracteres; se rechazan componentes de path, slash/backslash, colon, controles, caracteres inseguros para nombres y terminación en punto/espacio. ContentType es nullable, acotado y sin controles; nunca decide cómo ejecutar/renderizar el archivo. Cada documento tiene Guid y clave opaca `projects/{projectId}/documents/{documentId}/content`, independiente del nombre. Nombres repetidos no implican reemplazo.

### Atomicidad, concurrencia e historia

Cada upload adquiere FOR UPDATE sobre Project antes de leer documentos/tarea y mantiene el lock hasta completar el streaming y la transacción. Revalida autorización, vigentes y replacements dentro de esa sección. El proveedor escribe mediante temporal y rename sin sobrescritura; preserva rechazo de traversal/symlinks/junctions y cancelación. SaveChanges guarda el batch y su eventual aviso juntos; commit ocurre después de que todos sus archivos están disponibles. Ante error se revierte y se limpian exclusivamente las claves nuevas; si COMMIT no fue confirmado y rollback no puede establecer fallo, se conservan los archivos para inspección. No se limpian archivos ajenos.

Upload, reemplazo, baja y send-to-budgets comparten el lock de Project; no toman locks documentales en orden inverso. Las consultas documentales leen metadata/snapshot en una transacción RepeatableRead para evitar mezclar versiones entre consultas. Reemplazar crea una fila nueva, actualiza solo ReplacedAt/ReplacedByDocumentId del anterior y conserva bytes/metadata originales. Los reemplazados no vuelven a vigentes. El límite cuenta solo ReplacedAt=null y RemovedAt=null.

La baja previa al envío es **lógica** mediante RemovedAt: se conservan la fila y sus bytes, y todas las consultas/descargas públicas excluyen esa fila. No hay purga física automática. Esto evita filas sin archivo o archivos sin referencia por una falla entre DB y filesystem. No se pueden quitar históricos; con PrepareQuotation o snapshot presente responde 409 DocumentRemovalLocked.

El envío formal exige Asesor activo propietario y al menos un vigente. Guarda snapshot, items, PrepareQuotation Pending y TaskAssigned en una sola transacción. Una carga confirmada antes entra al snapshot; una posterior queda fuera y notifica. Una baja que gana al envío queda fuera; si gana el envío, la baja se rechaza. Las FK compuestas mantienen cada replacement/item dentro de su proyecto; las FK Restrict conservan historia. No se duplican físicamente los archivos del snapshot.

Los proyectos con PrepareQuotation preexistente siguen enviados aunque initialHandoff sea null. Presupuestos puede leer sus documentos posteriores; no se reconstruye un snapshot ficticio. DeadlineAt/StartedAt/WasOverdue no cambian al cargar o reemplazar documentos.

### Descargas, ZIP y notificaciones

La descarga individual abre FileStream, siempre como attachment y application/octet-stream, con nosniff. También permite históricos, pero rechaza documentos de otro proyecto o dados de baja. ZIP vigente e inicial seleccionan sus documentos por estado o por los IDs inmutables del handoff. ProjectDocumentArchive copia en streaming a un archivo temporal aleatorio DeleteOnClose, que se elimina al cerrar la respuesta o ante fallo; no reserva hasta 1.5 GB en RAM. Los entries quedan en la raíz, ordenados por UploadedAt/Id; los duplicados se desambiguan de forma determinista (`planos.pdf`, `planos (2).pdf`), sin modificar nombres persistidos.

NotificationKind.ProjectDocumentsUpdated=5 es Informational. Solo lo crea un batch documental exitoso posterior al envío, una vez por operación, incluso si PrepareQuotation está Completed. El destinatario es AssignedToGoogleSubject de PrepareQuotation; para legacy null se utiliza BudgetsAssigneeSubject configurado, sin reescribir la tarea. El título es “Documentación del proyecto actualizada”; el mensaje cuenta additions/replacements con singular/plural. ScheduledAt/CreatedAt corresponden al evento; DeadlineAt se proyecta de la tarea.

Su EventKey usa TaskId, kind 5, SHA-256 del destinatario y UploadBatchId. La unicidad existente evita duplicar ese evento. Los otros cuatro kinds conservan su clave por ciclo de asignación; el worker nunca genera ProjectDocumentsUpdated.

Los errores usan ProblemDetails con code: 400 InvalidDocumentBatch; 403 DocumentAccessDenied; 404 ProjectNotFound/DocumentNotFound; 409 DocumentsRequired, DocumentLimitExceeded, InvalidReplacement, DocumentRemovalLocked o InitialHandoffUnavailable; 413 FileTooLarge/BatchTooLarge; 500 DocumentUnavailable para bytes referenciados ausentes. Fallos inesperados se sanitizan con el handler global.

Los tests sintéticos cubren autorización antes de consumir body, límites exactos con streams generados, atomicidad/compensación, replacements, históricos, notificaciones, archivos/ZIP y modelo EF. El smoke PostgreSQL se ejecuta en un esquema temporal separado, aplica las diez migraciones allí y comprueba carreras reales; elimina únicamente su esquema/archivos y verifica que datos e historial de migraciones de public permanezcan iguales. No requiere ni modifica el corpus XLSX.

## Consultas gerenciales

`Authentication:ManagementSubject` es obligatoria al startup mediante ManagementOptions, BindConfiguration("Authentication") y ValidateOnStart. Null, vacío o whitespace producen únicamente `Configure Authentication:ManagementSubject.`. ConfiguredManagementIdentity implementa IManagementIdentity en Api; Application solo conoce Subject. La policy `Management` exige autenticación y Management disponible y activo. El resolver compara el claim `sub` exactamente con StringComparison.Ordinal; no recorta, normaliza ni usa email/nombre como fallback.

```powershell
dotnet user-secrets set "Authentication:ManagementSubject" "<GOOGLE_SUBJECT_GERENCIA>" --project src/Api
```

La alternativa de entorno es `Authentication__ManagementSubject`. No versionar Subjects reales. `/api/auth/me` incluye roles e isManagement derivado. ManagementController exige Management disponible y activo: 401 sin autenticación y 403 con otra área, aunque el Subject posea Management. Email no participa.

`GET /api/management/overview` devuelve:

- `totalProjects`: todos los proyectos persistidos, incluso sin tareas; no hay ProjectStatus, ActiveProjects, CompletedProjects ni CriticalProjects.
- `tasks`: pending, inProgress, completed.
- `leadTime`: notStarted, onTrack, dueSoon, dueToday, overdue, completedOnTime, completedLate, untrackedLegacy.
- `areas`: únicamente áreas representadas en tareas; cada fila contiene area, pending, inProgress, completed, overdue, dueToday, dueSoon. Enums como texto canónico, sin labels traducidos.
- `attention`: total completo y hasta `AttentionItemLimit = 10` items. Incluye exclusivamente Overdue → DueToday → DueSoon, ordenados dentro de la prioridad por DeadlineAt ascendente, ProjectName ordinal y TaskId. Cada item contiene taskId, projectId, projectName, clientName, sgCode nullable, area, taskType, taskStatus, leadTimeStatus, deadlineAt y startedAt.

`GET /api/management/tasks` devuelve todas las tareas actuales e históricas, sin filtros ni paginación. Cada fila contiene taskId, projectId, projectName, clientId, clientName, sgCode nullable, city, productLine, area, type, status, assignedAt, startedAt, completedAt, deadlineAt, wasOverdue nullable y leadTimeStatus. Ninguno de los DTO gerenciales expone Subjects ni claves de almacenamiento.

La lista ordena primero las no completadas: Overdue → DueToday → DueSoon → OnTrack → NotStarted → UntrackedLegacy; después DeadlineAt ascendente, null al final. Las completadas quedan detrás: CompletedLate → CompletedOnTime → UntrackedLegacy, con CompletedAt descendente dentro de cada grupo. ProjectName ordinal y TaskId resuelven empates. Las fechas son UTC y los valores legacy desconocidos siguen siendo null.

ListManagementTasksUseCase captura un único instante de TaskTiming.UtcNow para proyectar cada respuesta mediante `TaskTiming.Status(task, now)`. GetManagementOverviewUseCase reutiliza esa proyección. No se recalculan deadlines ni se persiste WasOverdue al consultar. IManagementReadStore solo declara CountProjectsAsync y ListTasksAsync; EfManagementReadStore utiliza un COUNT de Projects y una consulta con joins ProjectTasks → Projects → Clients, todas AsNoTracking, sin consultas por tarea. La lista requiere una consulta y el resumen dos. Los casos de uso no guardan entidades, archivos ni notificaciones.

La lectura documental transversal vive en ProjectDocumentAccess. Con Management activo no se puede subir, reemplazar, quitar, enviar a Presupuestos, asignar SG ni confirmar cotizaciones, aunque el Subject posea otros roles. Gerencia reutiliza GET /api/projects, /api/projects/{id}, /api/projects/{id}/tasks y /api/projects/{id}/notifications; no hay /management/projects. `/api/notifications` sigue limitado al destinatario actual, sin filtrar por rol ni duplicar avisos.

Esta funcionalidad no cambia el modelo EF ni crea migraciones. Las pruebas usan identidades sintéticas y reloj controlado; verifican autorización, contratos HTTP/OpenAPI, conteos, orden, joins sin N+1, lectura documental antes/después del handoff y ausencia de mutaciones. No incluye frontend ni SnG Intelligence.

## Contrato HTTP actual

### Asignación y corrección de SG

`AssignProjectSgCodeUseCase` usa `IBudgetsWorkflowStore.LoadAsync` para obtener Project y su PrepareQuotation y `TrySaveAsync` para persistir ambos juntos. Es una frontera específica de Presupuestos; no combina los SaveChanges de los stores de creación. La respuesta reutiliza ProjectResponse y ProjectTaskResponse.

| Estado previo | Resultado |
| --- | --- |
| Project inexistente | ProjectNotFound |
| Project sin PrepareQuotation | ProjectNotSentToBudgets |
| SG null, tarea Pending, StartedAt/CompletedAt null | SetSgCode + Start + guardado atómico |
| SG existente, tarea InProgress, StartedAt presente y CompletedAt null | SetSgCode + guardado; conserva toda la tarea |
| Cualquier otro estado, incluido Completed o fechas inconsistentes | InvalidWorkflowState, sin reparación |

La validación de workflow precede a la modificación del dominio. Un valor normalizado idéntico produce Success sin cambios de fechas ni nuevas tareas. El guardado devuelve false únicamente para SgCodeAlreadyInUse; el caso de uso nunca devuelve Success después de ese conflicto.

`EfBudgetsWorkflowStore` comienza una transacción al cargar y obtiene Project con SELECT FOR UPDATE parametrizado. Carga la tarea con tracking en el mismo contexto. Esto serializa asignaciones al mismo proyecto: una segunda solicitud observa la primera asignación confirmada y la trata como corrección o repetición, conservando StartedAt. La transacción abarca la lectura, un único SaveChangesAsync para las dos entidades y el commit; no hay commits separados. Si el caso de uso termina antes de guardar, la disposición async del store scoped revierte/libera la transacción al cerrar el request.

El índice existente `IX_Projects_SgCode` arbitra solicitudes sobre proyectos distintos. El store traduce solo DbUpdateException con InnerException PostgresException, `PostgresErrorCodes.UniqueViolation` (23505) y ConstraintName=`IX_Projects_SgCode`. Revierte la transacción y desacopla exclusivamente Project y ProjectTask. No vacía el ChangeTracker: otras entidades conservan su seguimiento. Las referencias desacopladas no representan datos guardados; para continuar se deben cargar de nuevo. Otros errores se propagan al manejo global, que responde 500 genérico.

Antes de guardar, Infrastructure ajusta únicamente UpdatedAt y StartedAt modificados a precisión de microsegundos, que es la conservada por PostgreSQL. Esto evita diferencias entre la respuesta inicial y una lectura posterior; un StartedAt existente no se modifica. Los valores siguen siendo UTC. No se añade un framework transaccional ni se realizan preconsultas de unicidad de SG.

### Rutas

| Método y ruta | Request | Éxito | Recurso inexistente |
| --- | --- | --- | --- |
| GET `/api/management/overview` | Policy Management, sin parámetros | 200, ManagementOverviewResponse; 401/403 según identidad | No aplica |
| GET `/api/management/tasks` | Policy Management, sin filtros ni paginación | 200, colección de ManagementTaskResponse; 401/403 según identidad | Colección vacía |
| GET `/api/notifications` | Sin parámetros; identidad autenticada | 200, notificaciones personales recientes primero; 400 si envía query | No aplica |
| GET `/api/projects/{id}/notifications` | Guid en ruta | 200, historial cronológico completo | 404 |
| GET `/api/auth/me` | Bearer ID token | 200, subject/email/name/roles/isManagement; 401 sin identidad válida | No aplica |
| POST `/api/clients` | `name` | 201, ClientResponse y Location | No aplica |
| GET `/api/clients` | Sin body | 200, colección de ClientResponse | Colección vacía |
| GET `/api/clients/{id}` | Guid en ruta | 200, ClientResponse | 404 |
| POST `/api/projects` | `clientId`, `name`, `city`, `productLine` | 201, ProjectResponse y Location | 404 si no existe Client |
| GET `/api/projects` | Sin body | 200, colección de ProjectResponse | Colección vacía |
| GET `/api/projects/{id}` | Guid en ruta | 200, ProjectResponse | 404 |
| POST `/api/projects/{id}/send-to-budgets` | Sin body; creador o Jefe; ≥1 vigente | 201, ProjectTaskResponse; 403 otro actor; 409 AlreadySent/DocumentsRequired | 404 |
| GET `/api/project-tasks?area=Budgets&status=Pending` | area compatible con ActiveRole y status requeridos como nombres | 200, tareas propias del rol; 403 para área incompatible/Management | Colección vacía |
| GET `/api/projects/{id}/tasks` | Guid en ruta, sin filtros | 200, colección de ProjectTaskResponse | 404 |
| PUT `/api/projects/{id}/sg-code` | `sgCode`; BudgetsAssignee | 200, AssignProjectSgCodeResponse; 409 por workflow o SG ocupado | 404 |
| POST `/api/projects/{id}/budget-quotation/preview` | multipart, un archivo `file`; BudgetsAssignee | 200, BudgetQuotationPreviewResponse; 400 archivo inválido; 409 workflow o SG distinto | 404 |
| POST `/api/projects/{id}/budget-quotation/confirm` | multipart: `file` y JSON `confirmation`; BudgetsAssignee | 201, ConfirmedBudgetQuotationResponse; 400 datos inválidos; 409 workflow, SG o confirmación existente | 404 |
| POST `/api/projects/{id}/send-to-commercial` | Sin body; BudgetsAssignee | 200, SendBudgetQuotationToCommercialResponse; 409 si no está listo o ya enviado | 404 |
| GET `/api/projects/{id}/budget-quotation/commercial-file` | Sin body; CommercialHead | 200, XLSX temporal; 409 antes del envío o después de confirmar Comercial; 500 si falta archivo referenciado | 404 |
| POST `/api/projects/{id}/commercial-quotation/preview` | multipart: solo `file`; CommercialHead | 200, CommercialQuotationPreviewResponse; 400 archivo/economía inválidos; 409 workflow/SG/estructura | 404 |
| POST `/api/projects/{id}/commercial-quotation/confirm` | multipart: solo `file`; CommercialHead | 201, CommercialQuotationConfirmationResponse; 400 archivo/economía inválidos; 409 workflow/SG/estructura/creator/duplicado | 404 |
| GET `/api/projects/{id}/commercial-quotation/file` | Sin body; Jefe o creador por Subject | 200, XLSX exacto; 403 otra identidad; 409 sin confirmación; 500 archivo referenciado ausente | 404 |

`ClientResponse` contiene `id`, `name`, `createdAt` y `updatedAt`. `ProjectResponse` contiene `id`, `clientId`, `name`, `city`, `productLine`, `sgCode`, `createdAt`, `updatedAt` y `createdBy`. Este último contiene `subject`, `email` y `name`, o es null para proyectos legacy; aparece en creación, GET, listado y respuesta de SG. Las fechas son UTC en formato ISO 8601. Las respuestas de proyecto no incluyen el objeto Client completo. `sgCode` es null hasta su primera asignación y después contiene el código normalizado.

Todas estas rutas exigen autenticación. Los códigos funcionales de la tabla se evalúan después de autenticar y autorizar; una política específica responde 403 si el Subject no coincide; sin una identidad válida responden 401 antes del binding y del caso de uso.

Se usa camelCase. `ProductLineJsonConverter` delega la lectura y escritura a `JsonStringEnumConverter<ProductLine>` de System.Text.Json con `allowIntegerValues=false`, y exige un único nombre: rechaza combinaciones separadas por comas que el convertidor estándar puede interpretar como una combinación numérica. Los nombres canónicos devueltos son `Classic`, `Essential`, `Bioconfort` y `Signature`; números JSON, cadenas numéricas, combinaciones y nombres desconocidos producen 400. El almacenamiento sigue siendo integer 1–4. La comparación de nombres no distingue mayúsculas/minúsculas.

Los requests de creación no incluyen `id`, fechas, `sgCode` ni identidad. CreateProjectRequest conserva solo clientId/name/city/productLine; createdBy, createdByGoogleSubject, createdByEmail o createdByName adicionales no pueden suplantar al creador. `JsonUnmappedMemberHandling.Disallow` rechaza campos JSON desconocidos, incluidos esos campos controlados por el dominio. Los nombres repetidos continúan permitidos.

### Contrato de tareas

El envío a Presupuestos no recibe DTO ni body. Devuelve 201 con el DTO creado, sin cabecera Location: no existe GET por Id de tarea. Tampoco existe POST genérico `/api/project-tasks` ni endpoint separado para iniciar tareas; PrepareQuotation se inicia con la primera asignación de SG, ReviewQuotation al enviar a Comercial y PresentQuotationToClient al confirmar la revisión comercial.

Ejemplo de respuesta del envío:

```json
{
  "id": "90628337-3801-4276-996b-43d901623d96",
  "projectId": "b0825722-a7d2-4385-9d13-a1a5e6fd7e66",
  "area": "Budgets",
  "type": "PrepareQuotation",
  "status": "Pending",
  "assignedAt": "2026-10-13T19:00:00Z",
  "startedAt": null,
  "completedAt": null,
  "assignedToGoogleSubject": "<BUDGETS_SUBJECT>",
  "deadlineAt": null,
  "wasOverdue": null,
  "leadTimeStatus": "NotStarted"
}
```

`ProjectTaskResponse` expone exactamente estos doce campos, sin objetos Client/Project incrustados, nombres duplicados ni SG. Los cuatro enums se serializan mediante JsonStringEnumConverter con valores numéricos deshabilitados.

El GET por área acepta los nombres definidos Commercial/Budgets y Pending/InProgress/Completed, sin distinguir mayúsculas. Ambos parámetros son obligatorios; valores ausentes, vacíos, numéricos, combinados con comas o desconocidos producen 400. La validación de nombres se realiza explícitamente en el Controller porque el binding ordinario de enums también admite números. El envío crea Budgets/Pending; la primera asignación de SG permite consultar esa tarea como Budgets/InProgress. El envío a Comercial completa PrepareQuotation y crea ReviewQuotation en Commercial/InProgress. El historial devuelve `[]` para un proyecto existente sin tareas.

### Request y respuesta de SG

PUT `/api/projects/{id}/sg-code` recibe `AssignSgCodeRequest`, por ejemplo:

```json
{ "sgCode": " sg0012 " }
```

Devuelve 200 con `AssignProjectSgCodeResponse`:

```json
{
  "project": {
    "id": "b0825722-a7d2-4385-9d13-a1a5e6fd7e66",
    "clientId": "c17113f3-f4cd-49b9-a46f-169edda59237",
    "name": "Casa Campestre",
    "city": "Bucaramanga",
    "productLine": "Signature",
    "sgCode": "SG0012",
    "createdAt": "2026-10-13T19:00:00Z",
    "updatedAt": "2026-10-13T20:00:00Z",
    "createdBy": { "subject": "google-test-subject", "email": "test@example.com", "name": "Test User" }
  },
  "prepareQuotationTask": {
    "id": "90628337-3801-4276-996b-43d901623d96",
    "projectId": "b0825722-a7d2-4385-9d13-a1a5e6fd7e66",
    "area": "Budgets",
    "type": "PrepareQuotation",
    "status": "InProgress",
    "assignedAt": "2026-10-13T19:30:00Z",
    "startedAt": "2026-10-13T20:00:00Z",
    "completedAt": null,
    "assignedToGoogleSubject": "<BUDGETS_SUBJECT>",
    "deadlineAt": "2026-10-19T22:00:00Z",
    "wasOverdue": false,
    "leadTimeStatus": "OnTrack"
  }
}
```

Una corrección devuelve la misma estructura con el SG y UpdatedAt nuevos, conservando StartedAt, DeadlineAt y WasOverdue exactamente. Un reenvío del mismo valor normalizado conserva también UpdatedAt. No se acepta null para borrar SG ni campos desconocidos. El endpoint responde 400 por request/formato inválido; 404 si no existe Project; 409 si no fue enviado, el workflow es inconsistente/no soportado o el SG está ocupado. No hay endpoints separados para corregir o iniciar.

### Preview técnico de cotización XLSX

`Application/Features/Budgets/PreviewQuotation/IBudgetQuotationParser` define `ParseAsync(Stream, string fileName, CancellationToken)` y devuelve `BudgetQuotationParseResult.Success` o `Invalid` con un código estable. Sus DTO y códigos de warning viven en Application. `CommercialItemSection` es el único enum Main=1/Additional=2, ubicado en Domain y compartido por parser, preview y catálogo persistido. HTTP conserva los nombres Main/Additional.

`PreviewBudgetQuotationUseCase` consulta `IProjectStore.GetByIdAsync` y `IProjectTaskStore.ListByProjectAsync`; sus implementaciones EF usan `AsNoTracking`. Exige un Project existente con SG y exactamente una PrepareQuotation, de área Budgets, InProgress, StartedAt presente y CompletedAt null. Invoca el parser y compara su SG normalizado con el del proyecto mediante igualdad ordinal. No usa `IBudgetsWorkflowStore`, transacciones explícitas, SELECT FOR UPDATE, mutadores del dominio ni SaveChanges. El estado se comprueba al leer; el preview no reserva ni bloquea el proyecto frente a cambios concurrentes posteriores.

`Infrastructure/Quotations/XlsxBudgetQuotationParser` abre el paquete en modo de solo lectura y AutoSave desactivado. `QuotationCellReader` centraliza shared strings, valores cacheados y formatos de ItemNumber; `UnsupportedQuotationException` es exclusivamente una señal interna de validación, convertida a resultado controlado. No se guardan ni recalculan workbooks, no se siguen enlaces ni se ejecutan macros o fórmulas.

**Formato soportado:** extensión `.xlsx` y paquete ZIP/OpenXML de workbook normal, sin VBA. La hoja debe llamarse `COTIZACIÓN`, tolerando espacios exteriores y diferencias de mayúsculas; no se busca una hoja parecida ni se consultan las bases internas. La metadata se toma de C10/C11/C12 (cliente/proyecto/ubicación), I10/I11/I12 (línea cliente/línea producción/elaborado por) y O10 (SG). Los metadatos descriptivos pueden estar vacíos; SG es obligatorio, se recorta, convierte a mayúsculas y exige `SG` + dígitos ASCII, preservando ceros iniciales. Espacios internos, signos o letras adicionales son inválidos. El nombre devuelto elimina componentes de ruta y nunca se usa para abrir un archivo local.

Un encabezado candidato tiene A=ÍTEM/ITEM. Si A de la fila siguiente está vacío se ignora el bloque, incluso si la plantilla vacía utiliza AN/AL abreviados. Para un ítem real se exige además B=UBICACIÓN, C=CONFIGURACIÓN, D=DESCRIPCIÓN (con o sin sus acentos), J=AN (MT), K=AL (MT), L=M2/M², M=CNT, N=VR UNT y O=VR TOTAL. La normalización solo recorta y compara en mayúsculas, sin aproximaciones. El preview de Presupuestos y el parser comercial leen N/O para los importes originales, además de reconocer el encabezado.

Cada bloque termina antes del siguiente encabezado o marcador ADICIONALES, o al final de los datos de la hoja. `sourceRow` es la fila del número, con índice inicial 1. La fila principal proporciona ubicación B, ancho J, alto K, m² L y cantidad M. Dentro del bloque se exigen una sola vez las etiquetas SISTEMA, CRISTAL, ACABADO ALUMINIO, CERRADURA y OBSERVACIONES en C y se leen sus valores de D, independientemente de su orden. Se permiten valores textuales vacíos, incluidas observaciones. PELÍCULA PROTECCIÓN no se exporta.

ItemNumber es texto visible, nunca identidad ni clave única. Texto e inline/shared strings conservan los ceros; para celdas numéricas se soportan General, texto (`@`) y máscaras enteras de ceros como `00`. Se rechazan otros formatos y números General de magnitud igual o superior a 100.000.000.000 que no puedan representarse con confianza. No se intenta reproducir todo el renderizado de Excel. Se conserva el orden documental y se permiten números repetidos. Antes de ADICIONALES la sección es Main; después es Additional. El título y las plantillas vacías no crean ítems.

Width, Height y SquareMeters son `decimal` positivos; Quantity es `int` positivo sin fracciones, y TotalQuantity suma en `long`. Se leen valores numéricos OpenXML con cultura invariante; textos que parecen números no se convierten por heurísticas regionales. SquareMeters procede del documento y representa el total del ítem; no se calcula Width × Height ni se multiplican unidades. Las fórmulas requieren un valor cacheado utilizable; no se devuelven expresiones ni se recalculan, tampoco si hacen referencia a otro libro.

Las imágenes incrustadas se asocian por la fila superior izquierda de su `OneCellAnchor` o `TwoCellAnchor`, dentro de los límites del bloque, incluido el encabezado. El orden de los drawings o del ZIP no decide la asociación. Una coincidencia devuelve bytes originales como base64 y su contentType; ninguna produce `MissingImage`, y varias producen `AmbiguousImage`, siempre con `image: null`. Logos fuera de los bloques se ignoran. Imágenes vinculadas externamente, agrupadas o sin anchor por celda interpretable hacen rechazar el formato. No se escriben imágenes a disco.

Se rechazan archivos sin ítems, encabezados reales incompletos, etiquetas requeridas ausentes/repetidas, medidas/cantidades inválidas, cachés requeridas inutilizables y estructuras CONTRATADO/MODIFICADO. La recotización/liquidación identificada como SG780 está fuera del contrato por su **estructura**, sin condiciones por nombre o SG en el código. `.xls`, `.csv`, `.xlsm`, archivos cifrados, corruptos o ilegibles no se admiten. Los errores esperables de ZIP/XML/OpenXML se convierten en errores de archivo sin exponer detalles internos; la cancelación se propaga.

**Carga HTTP:** POST `/api/projects/{id}/budget-quotation/preview`, `multipart/form-data`, exactamente una parte de archivo llamada `file`, sin campos adicionales. `QuotationMultipartReader`, compartido por las cargas de Presupuestos y Comercial, usa MultipartReader y buffers en memoria limitados; `DisableFormValueModelBindingAttribute` evita que MVC consuma antes el formulario o lo almacene temporalmente en disco. No recibe JSON con paths, URLs ni varios archivos. El parser también admite streams no seekable y deja abierto el stream del llamador.

Los límites son técnicos, configurables bajo `BudgetQuotationPreview`, y se validan como positivos al arrancar:

| Opción | Valor predeterminado | Propósito |
| --- | --- | --- |
| MaxUploadBytes | 30000000 | Cota en memoria del archivo; alineada con el límite de request predeterminado de Kestrel |
| MaxUncompressedBytes | 134217728 | Cota de 128 MiB para el contenido ZIP expandido, comprobada sobre longitudes declaradas y bytes leídos; también limita XML por parte |
| MaxZipEntries | 10000 | Cota de entradas de paquete |

Por ejemplo, se pueden configurar mediante `BudgetQuotationPreview__MaxUploadBytes`, `BudgetQuotationPreview__MaxUncompressedBytes` y `BudgetQuotationPreview__MaxZipEntries`. No son reglas comerciales de tamaño o cantidad de ítems. No se cambia el límite del servidor/proxy; este incluye el overhead multipart y puede rechazar antes la petición, normalmente con 413. Las cotas detectadas por el controller/parser devuelven 400. Cambiar estas opciones requiere evaluar la memoria disponible: los bytes comprimidos, XML, imágenes y base64 ocupan memoria simultáneamente. Nunca se extraen entradas ZIP al filesystem; se rechazan nombres duplicados y rutas inseguras.

**Respuesta 200**, ejemplo anónimo con una imagen ausente:

```json
{
  "projectId": "b0825722-a7d2-4385-9d13-a1a5e6fd7e66",
  "projectSgCode": "SG0012",
  "document": {
    "fileName": "cotizacion.xlsx",
    "clientName": "Cliente de ejemplo",
    "projectName": "Proyecto de ejemplo",
    "location": "Ubicación de ejemplo",
    "clientLine": "Línea cliente",
    "productionLine": "Línea producción",
    "preparedBy": "Equipo de ejemplo",
    "sgCode": "SG0012"
  },
  "itemCount": 1,
  "totalQuantity": 2,
  "items": [{
    "itemNumber": "01",
    "section": "Main",
    "location": "Zona de ejemplo",
    "system": "Sistema de ejemplo",
    "glass": "Cristal de ejemplo",
    "aluminumFinish": "Acabado de ejemplo",
    "lockType": "Cerradura de ejemplo",
    "observations": "",
    "width": 1,
    "height": 2.8,
    "squareMeters": 5.6,
    "quantity": 2,
    "image": null,
    "sourceRow": 15
  }],
  "warnings": [{
    "code": "MissingImage",
    "itemNumber": "01",
    "sourceRow": 15,
    "message": "El ítem no tiene una imagen asociable."
  }]
}
```

Cuando existe imagen, su objeto contiene `contentType` y `base64`. Section se serializa como Main/Additional y los warnings como MissingImage/AmbiguousImage. ItemCount cuenta bloques reales; TotalQuantity suma cantidades agrupadas. No hay unitPrice, totalPrice, projectTotal, costos, utilidad ni película de protección. Las advertencias solo cubren imágenes; no sustituyen errores estructurales.

El controller valida la carga, luego el caso de uso comprueba proyecto/workflow, interpreta el archivo y compara SG. Devuelve 400 por carga o documento inválido, 404 por proyecto inexistente, 409 por workflow no habilitado o SG válido distinto. Los errores del parser incluyen `code`: UnreadableWorkbook, UnsupportedWorkbookFormat, InvalidDocumentSgCode o TechnicalLimitExceeded, dentro de ProblemDetails. Un error inesperado del servidor sigue siendo 500 genérico.

Ejemplo con un proyecto existente ya habilitado y un XLSX cuyo SG coincide:

```powershell
curl.exe -H "Authorization: Bearer $googleIdToken" -F "file=@<RUTA_XLSX>" "https://localhost:7265/api/projects/<PROJECT_ID>/budget-quotation/preview"
```

El preview no guarda archivo, metadata, catálogo ni versión, ni cambia tablas. La persistencia ocurre únicamente mediante confirmación. El smoke local verificó preview 200 con PostgreSQL sobre proyectos sintéticos habilitados antes de confirmar.

### Validación local de corpus sin versionarlo

Los tests sintéticos se ejecutan con `dotnet test`; para solo el parser, `dotnet test --filter FullyQualifiedName~XlsxBudgetQuotationParserTests`. El corpus real debe permanecer fuera del repositorio y abrirse en modo lectura. No copiarlo, extraerlo, modificarlo ni registrar nombres, SG, observaciones, imágenes o previews completos.

Para repetir el smoke agregado, crear un ejecutor local descartable en `artifacts` (ignorado por Git), con referencia a Infrastructure:

```shell
dotnet new console -f net10.0 -o artifacts/CorpusPreview
dotnet add artifacts/CorpusPreview/CorpusPreview.csproj reference src/Infrastructure/Infrastructure.csproj
```

Reemplazar su Program.cs por el siguiente código y ejecutar `dotnet run --project artifacts/CorpusPreview -- "<ZIP_FUERA_DEL_REPOSITORIO>"`. El nombre sintético evita devolver nombres reales en el resultado usado por el smoke:

```csharp
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SnG.Control.Application.Features.Budgets.PreviewQuotation;
using SnG.Control.Domain.Enums;
using SnG.Control.Infrastructure.Quotations;

var parser = new XlsxBudgetQuotationParser(Options.Create(new BudgetQuotationParserOptions()));
using var zip = ZipFile.OpenRead(args[0]);
var stats = new Dictionary<string, long>();
void Count(string key, long amount = 1) => stats[key] = stats.GetValueOrDefault(key) + amount;
foreach (var entry in zip.Entries.Where(e => e.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)))
{
    using var input = entry.Open();
    Count("processed");
    if (await parser.ParseAsync(input, "quotation.xlsx", CancellationToken.None) is BudgetQuotationParseResult.Success result)
    {
        Count("valid"); Count("items", result.Items.Count);
        Count("Main", result.Items.Count(i => i.Section == CommercialItemSection.Main));
        Count("Additional", result.Items.Count(i => i.Section == CommercialItemSection.Additional));
        Count("images", result.Items.Count(i => i.Image is not null));
        foreach (var warning in result.Warnings) Count(warning.Code.ToString());
    }
    else Count("rejected");
}
Console.WriteLine(JsonSerializer.Serialize(stats));
```

Retirar el ejecutor al terminar; no agregarlo ni agregar el corpus a Git. El smoke local procesó **66 XLSX: 62 válidos y 4 rechazados**, con **1.038 ítems: 1.037 Main, 1 Additional, 1.038 imágenes, 0 MissingImage y 0 AmbiguousImage**. Los rechazos fueron un SG con espacios internos, un documento CONTRATADO/MODIFICADO y dos contenedores OLE ilegibles como XLSX. El archivo con SG inválido tiene apariencia estándar, pero no se sanea el identificador para aceptarlo. No se añadieron excepciones por nombre ni heurísticas para forzar aprobaciones.

### Confirmación de cotización

POST `/api/projects/{id}/budget-quotation/confirm` recibe exactamente dos partes multipart, en cualquier orden: `file` (XLSX) y `confirmation` (JSON UTF-8 con Content-Type application/json; charset ausente o utf-8). Se acepta que el cliente envíe el JSON como Blob con filename; ese nombre se ignora. Se rechazan duplicados de parte, partes adicionales, archivos ausentes/vacíos y JSON inválido. No acepta URLs ni paths como alternativa al archivo.

`QuotationMultipartReader` sirve a preview y confirmación, con `DisableFormValueModelBindingAttribute` para evitar buffering de MVC a disco. Aplica el límite técnico existente MaxUploadBytes a la suma de bytes de las partes; el límite del servidor/proxy también incluye overhead multipart. Propaga cancelación. El JSON usa las opciones estrictas de la API: miembros desconocidos producen 400, Quantity debe ser entera y Section acepta únicamente Main/Additional como nombres, sin números ni combinaciones.

Ejemplo de `confirmation` para un documento de un ítem:

```json
{
  "items": [{
    "section": "Main",
    "sourceRow": 15,
    "itemNumber": "01",
    "location": "V1",
    "system": "Sistema de ejemplo",
    "glass": "Cristal corregido",
    "aluminumFinish": "Acabado de ejemplo",
    "lockType": "Cerradura de ejemplo",
    "observations": "",
    "width": 2.54,
    "height": 2.8,
    "squareMeters": 5.6,
    "quantity": 8
  }]
}
```

El cliente envía solo valores confirmados. No se aceptan extracted, imagen, metadatos, SG ni precios. Section y SourceRow identifican el ítem; no son correcciones permitidas. El matching es **(Section, SourceRow)** y se exige cobertura exacta: misma cantidad, filas únicas, sin claves extra ni ausentes. ItemNumber nunca participa como identidad. El orden del payload puede variar; el catálogo y la respuesta siguen SourceRow. Los valores textuales opcionales pueden estar ausentes/null/vacíos y se normalizan a vacío; ItemNumber no puede quedar vacío. Las medidas y cantidades deben ser positivas y no se recalculan.

`ConfirmBudgetQuotationUseCase` reutiliza `PreviewBudgetQuotationUseCase`: vuelve a ejecutar el parser vigente sobre los bytes recibidos, con las mismas reglas de workflow y SG. `BudgetQuotationWorkflow.ReadyTask` concentra la regla de exactamente una PrepareQuotation en Budgets/InProgress, SG presente, StartedAt no null y CompletedAt null. No se confía en el preview anterior ni en una extracción remitida por el frontend. Como el preview es stateless, no existe un token/hash de sesión: el XLSX que llega en la confirmación es el que se reinterpreta y conserva.

Tras validar la cobertura y valores, el caso de uso construye BudgetQuotationVersion (versión 1), CommercialCatalog, ítems oficiales y snapshots inmutables de extracción, además de los archivos que deben guardarse. `IBudgetQuotationConfirmationStore` define esa única frontera de persistencia. Application no conoce DbContext, transacciones concretas ni RootPath. No se usa un repositorio genérico, UnitOfWork ni segundo parser.

**Transacción y archivos:** `EfBudgetQuotationConfirmationStore` abre una transacción después del parsing. Relee Project con `FOR SHARE` y AsNoTracking, y consulta las tareas sin locks ni tracking. El lock de Project impide una corrección concurrente de SG hasta terminar el commit; no bloquea entre sí dos confirmaciones. Revalida el workflow y el SG documental contra el valor actual. Ni Project ni ProjectTask se modifican.

Comprueba si existe versión 1 para responder pronto 409; esa consulta no sustituye la garantía única. Agrega las cuatro clases de registros y realiza un SaveChanges dentro de la transacción, todavía sin commit. Después escribe el XLSX y todas las imágenes. Solo cuando todas las escrituras terminan realiza commit. Así no se confirma una transacción antes de guardar sus archivos.

Si falla SaveChanges, una escritura o un commit que pueda revertirse, hace rollback y elimina exclusivamente las claves cuyo SaveAsync terminó correctamente en esa operación. Usa CancellationToken.None para compensar aunque el request haya sido cancelado y continúa intentando las otras eliminaciones si una falla. Desacopla únicamente sus entidades, sin limpiar entradas ajenas del ChangeTracker. Un error de compensación se propaga como error de servidor y requiere inspección; no se informa éxito.

PostgreSQL y filesystem no comparten una transacción distribuida. Un crash entre escritura y commit puede dejar archivos huérfanos. Si COMMIT pierde su respuesta y el rollback también falla, el resultado puede ser incierto: se conservan los archivos para no borrar los que quizá respaldan registros ya confirmados, y se responde error. No hay cleaner ni recuperación automática de archivos; el worker de notificaciones no reconcilia filesystem. La revisión administrativa debe comparar claves/identificadores de esa operación antes de eliminar archivos; nunca realizar una limpieza global. Los backups/restores de DB y archivos también deben coordinarse.

La concurrencia se resuelve mediante **UX_BudgetQuotationVersions_ProjectId_VersionNumber**. Solo DbUpdateException con PostgresException, SQLSTATE `23505` (`UniqueViolation`) y ese nombre exacto se convierte en AlreadyConfirmed/409, después de rollback/compensación. Otras restricciones o errores no se confunden con una segunda confirmación. Al ejecutar SaveChanges antes de escribir archivos, la operación perdedora no llega a crear sus archivos. Una confirmación posterior no reemplaza la existente ni crea versión 2.

**Respuesta:** 201 con ConfirmedBudgetQuotationResponse, sin Location ni endpoint GET artificial:

- `quotationVersionId`, `versionNumber`, `commercialCatalogId`, `projectId`, `confirmedAt`.
- `document`: fileName, clientName, projectName, location, clientLine, productionLine, preparedBy y sgCode del archivo reparseado.
- `itemCount` y `totalQuantity`, calculado con cantidades **confirmadas** en long.
- `items`: id, section, sourceRow, `extracted`, `confirmed` e `image`.
- extracted y confirmed contienen cada uno los once campos técnicos del request, sin section/sourceRow repetidos.
- image contiene únicamente `status` (Stored/Missing/Ambiguous) y `contentType`, null en los dos últimos estados. No se devuelve base64 ni claves de almacenamiento.
- `prepareQuotationTask`: ProjectTaskResponse completo. Continúa InProgress, con el mismo StartedAt y CompletedAt null.

Se conserva también Project.UpdatedAt, SgCode, Name, City y ProductLine. No se crea una tarea Comercial ni se completa Presupuestos. No se exponen precios, rutas físicas, RootPath o StorageKey. Los códigos son 400 por carga/archivo/confirmación inválida, 404 por Project inexistente, 409 por workflow no habilitado, SG distinto o versión inicial existente, y 500 genérico por errores inesperados de DB/filesystem/configuración.

**Prueba manual:** con la API iniciada, un proyecto de prueba enviado a Presupuestos y con SG asignado, ejecutar preview, preparar confirmation.json con todos sus ítems y cambiar únicamente los campos editables. Enviar el mismo XLSX junto al JSON:

```powershell
curl.exe -H "Authorization: Bearer $googleIdToken" -F "file=@<XLSX_SINTETICO>" -F "confirmation=@<CONFIRMATION_JSON>;type=application/json" "https://localhost:7265/api/projects/<PROJECT_ID>/budget-quotation/confirm"
```

Exigir 201 y comparar extracted/confirmed; una segunda petición debe producir 409. Verificar en PostgreSQL una versión 1, un catálogo y un snapshot por ítem, con claves relativas. En la raíz configurada comparar bytes del XLSX y de imágenes con los enviados. Comprobar que Project y PrepareQuotation conservan todos sus valores. No confirmar masivamente el corpus real.

Los tests de confirmación cubren cada campo editable, cambios múltiples, orden invertido, Main/Additional y números repetidos, validaciones de cobertura, JSON estricto, imágenes Stored/Missing/Ambiguous, extensiones seguras, invariantes y metadata EF. Los tests de almacenamiento utilizan directorios temporales propios y comprueban bytes, traversal, no overwrite, cancelación y retirada de temporales. Los tests de compensación utilizan el ChangeTracker y LocalFileStorage reales, con SaveChanges/transacción controlados para provocar fallos en SaveChanges, XLSX, imagen intermedia y commit; cubren además cancelación, constraint específico y resultado incierto de commit. No necesitan PostgreSQL. Infrastructure expone sus miembros internos únicamente al assembly de tests para verificar esa fase de persistencia sin crear un framework de transacciones.

### Envío de Presupuestos a Comercial

POST `/api/projects/{id}/send-to-commercial` requiere la política BudgetsAssignee y no recibe body; contenido inesperado produce 400. `SendBudgetQuotationToCommercialUseCase` consulta el estado mediante `IBudgetQuotationCommercialHandoffStore` y obtiene de ICommercialHeadIdentity el Subject que entrega a SendAsync para asignar la tarea. Requiere exactamente una PrepareQuotation en Budgets/InProgress, StartedAt presente y CompletedAt null; versión 1, catálogo con al menos un ítem y una extracción por ítem; original disponible; ausencia de ReviewQuotation y de ConfirmedFileStorageKey. Project inexistente produce 404; estado inválido, envío previo, original ausente o documento incompatible producen 409.

Project.SgCode debe coincidir ordinalmente con BudgetQuotationVersion.DocumentSgCode. Un cambio posterior a la confirmación produce 409 con `code: SgChangedAfterConfirmation`, también al revalidar dentro de la transacción. No se corrige O10 ni se reconcilia el SG. El endpoint no modifica Project, catálogo, ítems ni extracciones.

`IFileStorage.ReadAsync` carga el original mediante su clave relativa. `XlsxBudgetQuotationWorkbookGenerator`, implementación de `IBudgetQuotationWorkbookGenerator`, valida el original con el parser existente de solo lectura y compara los ítems con sus extracciones persistidas. La escritura está separada del parser y reutiliza únicamente la lectura estructural de filas. Abre una copia en memoria con OpenXML; no reconstruye el workbook ni reinserta imágenes. El matching exacto de Section+SourceRow conserva Main/Additional y permite ItemNumber repetidos; SourceRow corresponde a la primera fila del bloque, inmediatamente después del encabezado validado.

| Valor confirmado | Celda de destino |
| --- | --- |
| ItemNumber | A en SourceRow |
| Location | B en SourceRow |
| Width / Height / SquareMeters / Quantity | J / K / L / M en SourceRow |
| System | D de la fila cuya C contiene SISTEMA |
| Glass | D de la fila cuya C contiene CRISTAL |
| AluminumFinish | D de la fila cuya C contiene ACABADO ALUMINIO |
| LockType | D de la fila cuya C contiene CERRADURA |
| Observations | D de la fila cuya C contiene OBSERVACIONES |

Las etiquetas se localizan dentro del bloque, sin asumir offsets. No se modifican N/O, PELÍCULA PROTECCIÓN, metadata ni SG documental. Las celdas existentes mantienen StyleIndex y formatos; los textos sin fórmula se escriben como InlineString local, sin cambiar entradas compartidas de SharedStringTable. Las fórmulas se conservan; se actualiza únicamente su cached value con el valor confirmado, numérico en cultura invariante o textual según el campo. El m² oficial de DB no se recalcula. `CalculationProperties` usa Auto, FullCalculationOnLoad=true y ForceFullCalculation=true: Excel puede recalcular las fórmulas al abrir.

El generador cierra y reabre el resultado en modo lectura para validar valores, fórmulas y estilos de destino. Compara byte por byte todas las entradas ZIP salvo workbook.xml y la hoja editada: incluye ImageParts, drawings, anchors, relaciones, shared strings, estilos y otras hojas. Conserva hojas/orden, nombres definidos, merges, dimensiones y metadata. No exige igualdad binaria del ZIP completo. Una discrepancia impide almacenar el resultado.

**Persistencia:** `EfBudgetQuotationCommercialHandoffStore` abre la transacción después de generar y validar el archivo. Bloquea Project, tareas del proyecto y versión 1 con `FOR UPDATE`, en ese orden, y vuelve a comprobar estado, SG y datos usados para generar. Project se lee sin tracking. Completa PrepareQuotation, crea ReviewQuotation de Commercial con AssignedToGoogleSubject del Jefe configurado e invoca Start inmediatamente; asocia la clave mediante `BudgetQuotationVersion.SetConfirmedFileStorageKey`, que admite una única asignación relativa segura. Guarda tareas, versión y notificaciones de la transición con un SaveChanges, escribe el archivo y confirma la transacción. Los timestamps modificados se ajustan a microsegundos de PostgreSQL; AssignedAt y StartedAt de Presupuestos permanecen intactos.

Clave operativa: `projects/{projectId}/budget-quotations/{quotationVersionId}/confirmed.xlsx`. La original y las imágenes no reciben escrituras ni eliminaciones en este flujo. `ConfirmedFileStorageKey` es nullable antes del envío, con índice único que admite múltiples null. El índice parcial `UX_ProjectTasks_ProjectId_ReviewQuotation` impide dos tareas ReviewQuotation por proyecto. Solo SQLSTATE 23505 con ese nombre exacto se convierte en AlreadySent/409; otras violaciones se propagan como errores inesperados. Dos envíos simultáneos producen un 200 y un 409, con una única tarea y archivo operativos.

Si falla SaveChanges no se escribe archivo; si falla el guardado se revierte DB; si falla el commit con rollback confirmado se elimina exclusivamente el confirmed.xlsx creado por esa operación. Se restauran y desacoplan las entidades afectadas, preservando otras entradas del contexto. La compensación usa CancellationToken.None. Un fallo de limpieza se propaga como error técnico; un commit incierto conserva el archivo si tampoco puede confirmarse el rollback. Se mantienen las limitaciones DB/filesystem descritas para la confirmación: no hay transacción distribuida ni recuperación automática tras un crash.

**Respuesta 200:** `projectId`, `budgetTask`, `commercialTask`, `quotationVersionId`, `commercialCatalogId` y `commercialFileAvailable: true`. Las tareas usan ProjectTaskResponse: Presupuestos Completed con CompletedAt UTC y su asignado de Presupuestos (null solo si legacy); Comercial ReviewQuotation/InProgress con StartedAt UTC, CompletedAt null y assignedToGoogleSubject configurado. Los listados por área/proyecto también reflejan esta asignación o null histórico. ReviewQuotation inicia su deadline de cinco días y registra TaskAssigned. La finalización de Presupuestos conserva WasOverdue y resuelve o crea/resuelve el aviso de atraso cuando corresponde. La respuesta no expone StorageKey ni rutas físicas.

### Vistas persistentes de cotizaciones confirmadas

`QuotationViewsController` expone tres GET sin body:

- `/api/projects/{id}/budget-quotation/view`: ConfirmedBudgetQuotationView con projectId, quotationVersionId, versionNumber, confirmedAt, document e items. Cada ítem conserva id, Section + SourceRow, confirmed, extracted nullable, imageStatus nullable, hasImage y technicalDifferences.

- `/api/projects/{id}/commercial-quotation/view`: ConfirmedCommercialQuotationView con projectId, commercialQuotationId, confirmedAt, projectTotal e items (valores técnicos, unitPrice, totalPrice, technicalDifferences, imageStatus y hasImage).
- `/api/projects/{id}/budget-quotation/items/{itemId}/image`: stream raster del ítem histórico, validando la relación completa proyecto → versión → catálogo → ítem → extracción.

El preview, la respuesta de confirmación y la vista confirmada de Presupuestos incluyen `economics`: status, currencyCode=COP, projectTotal, projectTotalCell, items (Section/SourceRow, unitPrice, totalPrice) y mainItemTotalsDiffer. Los importes son strings decimales canónicos con dos decimales, normalizados por Money.Normalize, sin paso por double. N{SourceRow} aporta UnitPrice; O{SourceRow}, TotalPrice; el único VALOR TOTAL de N anterior a ADICIONALES identifica la celda O del total. Se reutilizan QuotationCellReader y los controles de QuotationFinancialCache. No se evalúan fórmulas ni se multiplican precios por cantidades. La suma decimal de subtotales Main se usa exclusivamente para advertir discrepancias, nunca para reemplazar ProjectTotal; puede diferir por conceptos del documento.

El archivo permanente original.xlsx de BudgetQuotationVersion ya persiste la fuente económica. QuotationReadService, después de autorizar, obtiene la clave por projectId + quotationVersionId del mismo read model y lee exclusivamente economía con límites de paquete; no busca la última versión comercial ni reextrae imágenes. No hay migración ni duplicación monetaria en DB. Las versiones históricas funcionan con su original conservado. OriginalUnavailable, InvalidEconomics, RecalculationRequired e InconsistentFormulaCache devuelven importes nulos e items vacíos. La confirmación sigue siendo técnica, sin importes editables en el request; los bloqueos financieros de la confirmación comercial permanecen vigentes.

`QuotationReadService` valida ActiveRole y acceso al proyecto antes de consultar snapshots o abrir archivos. Advisor exige creador original; Budgets exige PrepareQuotation asignada y solo lee Presupuestos; Head exige ReviewQuotation asignada; Management tiene lectura transversal. Las tareas completadas conservan acceso histórico. Los roles se validan con el resolver existente; compartir Subject entre varias membresías no activa permisos de las demás áreas. Las policies de escritura siguen separadas; las descargas XLSX comprueban también el ámbito de proyecto antes de leer storage.

`IQuotationReadStore` vive en Application; `EfQuotationReadStore` usa AsNoTracking y CancellationToken. Presupuestos consulta la última versión confirmada por VersionNumber, CommercialCatalog, CommercialCatalogItem y CommercialCatalogItemExtraction mediante left join (una extracción ausente se representa como null, sin inventar datos). Comercial consulta CommercialQuotation y CommercialQuotationItem enlazados al ítem histórico por CommercialCatalogItemId, reutilizando su extracción para el estado de imagen. Se utiliza ConfirmedAt real en ambas entidades. Orden: sección y fila fuente. `CommercialTechnicalDifferences.Compare` calcula las diferencias en Backend: extraído → confirmado para Presupuestos y Presupuestos → Comercial para el snapshot comercial. Los importes se leen tal como fueron persistidos; no se recalculan.

Los JSON no contienen storage keys ni bytes. La vista de Presupuestos abre el original para economía; el endpoint de imagen admite PNG/JPEG/GIF/BMP/TIFF, con nosniff y no-store. Las consultas no dependen de confirmed.xlsx: siguen disponibles después de eliminar ese temporal. Estas consultas no escriben datos ni crean versiones del negocio.

Respuestas: 401 sin identidad, 403 sin rol activo válido o acceso al proyecto, 404 para proyecto/cotización/imagen inexistentes (una imagen referenciada cuyo archivo falta también devuelve 404). Los GET usan no-store. El Frontend oculta una cotización inexistente y distingue los errores de permisos o disponibilidad.

`QuotationReadViewTests` cubre matriz multirol, ownership, asignación, imágenes y ausencia de rutas en JSON. `QuotationReadStorePostgresTests` comprueba metadata, decimales, fechas, diferencias, extracción ausente y relación de imágenes con entidades sintéticas bajo rollback, sin archivos. Para ejecutar este último: `SNG_RUN_POSTGRES_SMOKE=1` y `dotnet test -c Release --no-restore --filter FullyQualifiedName~QuotationReadStorePostgresTests`.

### Estado persistente de cotización — Presupuestos 4D

GET `/api/projects/{id}/budget-quotation/status`, sin body, exige BudgetsAssignee. `BudgetQuotationStatusController` delega en `IBudgetQuotationStatusStore`; no otorga acceso adicional a Gerencia ni a Intelligence. Proyecto inexistente: 404; existente: 200 aunque no exista confirmación; conserva 401/403.

Contrato cerrado: projectId, status, versionNumber nullable, confirmedAt nullable, itemCount, totalQuantity, documentSgCode nullable, currentProjectSgCode nullable, canSendToCommercial, sendBlockReason. Los enums se serializan como strings mediante converters registrados con allowIntegerValues=false; JsonUnmappedMemberHandling.Disallow permanece intacto. Status: NotConfirmed / Confirmed / SentToCommercial. SendBlockReason: None / SgChangedAfterConfirmation / InvalidState.

NotConfirmed devuelve versionNumber/confirmedAt/documentSgCode null, cantidades cero, canSend=false y None. Confirmed conserva metadata de versión 1 y del catálogo, con capability oficial. SentToCommercial mantiene metadata histórica y canSend=false/None. No devuelve UUIDs de versión/catálogo, Subject, bytes, claves de storage, rutas, imágenes ni extracción.

`CommercialHandoffReadiness.Validate` contiene la regla pura extraída de `CommercialHandoffState.Validate`: revisión existente, tarea de preparación completada o clave operativa ya asignada indican AlreadySent; después valida ReadyTask, versión 1, catálogo/ítems/cobertura de extracciones y SG. El comando construye esos hechos desde su snapshot completo; el GET desde agregados. El comando la sigue invocando antes de generar y bajo lock, conserva SameDocumentAs, constraints y compensación sin cambios. No se duplican reglas. El histórico no depende exclusivamente de ConfirmedFileStorageKey: las tareas preservan SentToCommercial tras revisión completada y retirada del temporal.

`EfBudgetQuotationStatusStore` usa hasta cuatro consultas AsNoTracking: proyecto, tareas, proyección mínima de versión 1 y agregados correlacionados del catálogo (COUNT, SUM Quantity, cobertura de extracciones por IDs). Sin versión usa tres; proyecto ausente, una. No carga valores extraídos/imágenes ni tiene dependencia de IFileStorage/parser/generador. La capability expresa readiness de persistencia; la disponibilidad física y correspondencia del workbook siguen siendo verificaciones del POST. No hay nueva tabla, migración ni modificación de archivos por GET.

Tests HTTP cubren 404/401/403, enums string y contrato exacto, no-confirmada/confirmada/bloqueada/enviada y fases posteriores sin archivos. Las regresiones del comando cubren concurrencia y compensación. Un test opt-in ejecuta el store real en PostgreSQL con entidades sintéticas y rollback obligatorio, verifica conteos y rollback sin tocar proyectos reales:

```powershell
$env:SNG_RUN_POSTGRES_SMOKE="1"
dotnet test --no-restore -c Release --filter "FullyQualifiedName~BudgetQuotationStatusPostgresTests"
Remove-Item Env:SNG_RUN_POSTGRES_SMOKE
```

Usa configuración local User Secrets/entorno sin imprimirla; no crea esquema ni aplica migraciones. La suite habitual omite ese smoke y no requiere PostgreSQL. Para validar con la API Debug local en ejecución, usar dotnet build/test --no-restore -c Release; no es necesario detenerla. Reiniciarla con 4D antes del smoke UI.

El usuario verificó el smoke real de 4D: reload conserva Confirmed y recupera metadata real; el envío exige confirmación humana y Cancelar no modifica estado; el envío exitoso completa PrepareQuotation, crea/inicia ReviewQuotation y actualiza tareas/actividad; otro reload conserva SentToCommercial sin volver a ofrecer envío. El bloqueo por SG distinto debe probarse solo en un caso seguro/sintético; no se alteran proyectos reales para forzar inconsistencias. Revisión Comercial en UI queda fuera de 4D.

### Descarga del Excel operativo

GET `/api/projects/{id}/budget-quotation/commercial-file` exige CommercialHead antes de invocar `DownloadCommercialQuotationUseCase`: 401 sin autenticación válida y 403 para otro Subject, aunque coincida el email. El mismo Subject con distinto email sigue autorizado mientras CommercialHead esté activo. Project inexistente o sin ReviewQuotation asignada al Subject activo produce 404; versión 1 o clave operativa ausentes producen 409. Una clave existente en DB cuyo archivo falta produce 500 genérico y diagnóstico técnico en logs, sin revelar rutas ni regenerar el documento.

Responde 200 con `Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`. `File(bytes, contentType, OriginalFileName)` de ASP.NET serializa Content-Disposition de forma segura; filename/filename* representan el nombre original, sin sufijos. Los bytes son exactamente los almacenados. La consulta no guarda DB, no escribe archivos, no cambia fechas/tareas y no incrementa contadores. Puede repetirse sin efectos secundarios.

**Prueba manual:** tras confirmar un XLSX sintético de dos ítems con imágenes, fórmula M2 y precios ficticios, cambiar Width, Glass y Quantity durante esa confirmación. Enviar sin cuerpo con un token de Presupuestos y descargar con un token del Jefe Comercial configurado:

```powershell
$projectUrl = "https://localhost:7265/api/projects/<PROJECT_ID>"
Invoke-RestMethod -Headers $authHeaders -Method Post -Uri "$projectUrl/send-to-commercial"
curl.exe -H "Authorization: Bearer $googleIdToken" -D "<HEADERS_TEMPORALES>" -o "<XLSX_DESCARGADO>" "$projectUrl/budget-quotation/commercial-file"
```

Verificar estado de ambas tareas, clave relativa, hash del original antes/después, valores corregidos, fórmula/cache, ImageParts y N/O. Repetir GET y comparar bytes, headers y snapshot de DB, incluidas las fechas. Para concurrencia usar otro proyecto sintético confirmado y dos POST simultáneos: exigir 1×200 y 1×409, una PrepareQuotation Completed, una ReviewQuotation InProgress asignada al Subject configurado y un solo confirmed.xlsx. Retirar solo los registros y carpetas creados por la prueba.

Los tests cubren Complete y la clave operativa; precondiciones y SG histórico; cada campo del generador, cambios múltiples, filas/secciones, estilos, fórmulas/cache, shared strings, imágenes/anchors, estructura y celdas protegidas; compensación con fallos de SaveChanges, archivo, commit y cleanup; códigos HTTP, nombre Unicode, bytes exactos y descargas sin cambios.

### Revisión y confirmación comercial

Los POST comerciales reciben **solo una parte file**, reutilizando QuotationMultipartReader y los límites existentes. No hay DTO de correcciones ni precios/totales proporcionados por JSON. Preview responde 200 y no escribe DB, guarda archivos, elimina el temporal ni cambia tareas. Confirmación responde **201 Created**, sin Location artificial: crea una cotización y su archivo, con descarga en la ruta conocida del proyecto.

**Parser:** `ICommercialQuotationParser` es una abstracción específica de Application. `XlsxCommercialQuotationParser` llama primero al IBudgetQuotationParser aprobado para validar paquete, extensión, entradas ZIP, tamaño expandido, estructura, SG, claves y valores técnicos. Solo después abre los mismos bytes en lectura con AutoSave=false para economía. No modifica el parser técnico ni recalcula fórmulas. No guarda nuevas imágenes; las extraídas durante validación permanecen en memoria y los ítems finales enlazan las históricas.

UnitPrice viene de **N{SourceRow}** y TotalPrice de **O{SourceRow}**, también para Additional. ProjectTotal se lee de O en la única fila cuya N, tras Trim y comparación sin distinguir case, dice exactamente VALOR TOTAL y precede al único marcador ADICIONALES de A. Cero o varios totales principales y valores no numéricos producen 400 InvalidEconomics. Un marcador ADICIONALES ausente o ambiguo produce 400 InvalidWorkbook con UnsupportedWorkbookFormat. El total posterior no se agrega ni sustituye al principal. No hay referencias a O169, filas derivadas del número de ítems ni sumas propias.

Los importes usan decimal y CultureInfo.InvariantCulture, leyendo representación OOXML numérica y cached values de fórmulas. Se acepta cero, se rechazan negativos, texto económico y fórmulas sin caché numérica. No se usa double, texto formateado, Excel ni LibreOffice. Money.Normalize aplica escala 2 con MidpointRounding.AwayFromZero en el parser y el dominio. UnitPrice, TotalPrice y ProjectTotal se mapean a **PostgreSQL numeric(20,2)**. No se calcula precio por cantidad, sumatorias, IVA ni descuentos.

La validación financiera es de lectura, no un motor Excel. Si existen fórmulas monetarias, se
rechazan con HTTP 400 / RecalculationRequired las marcas explícitas forceFullCalc, fullCalcOnLoad,
calcCompleted=false, calcMode=manual, sheetCalcPr.fullCalcOnLoad o ca en la celda económica.
El confirmed.xlsx generado por Presupuestos conserva fórmulas y cachés, pero marca recálculo:
debe revisarse, recalcularse y guardarse en Excel antes de su confirmación comercial.
InconsistentFormulaCache rechaza diferencias numéricas exactas en referencias locales directas
y IFERROR(referencia,""), antes de Money.Normalize. No evalúa multiplicaciones, sumas, impuestos,
fórmulas compartidas/de matriz ni dependencias externas. La ausencia de esas señales no certifica
vigencia de cachés complejas; calcId por sí solo tampoco prueba obsolescencia. Comparar siempre
el preview con el archivo revisado y guardado. No se cambian snapshots, importes ni XLSX históricos.
Prueba externa opcional: SNG_FINANCIAL_SOURCE_XLSX valida valores guardados y SHA256 en solo lectura.

**Matching y diferencias:** CommercialItemKey es Section+SourceRow. Debe coincidir exactamente el conjunto del XLSX con el catálogo; duplicados, claves ausentes o adicionales producen 409 StructuralMismatch con missingItems/unexpectedItems. No hay matching aproximado, por ItemNumber, Location ni orden. Insertar/eliminar ítems o desplazar su estructura no está soportado. Cambiar ItemNumber sí está permitido si conserva la clave.

CommercialTechnicalDifferences compara los once campos de CatalogItemValues: ItemNumber, Location, System, Glass, AluminumFinish, LockType, Observations, Width, Height, SquareMeters y Quantity. Usa igualdad ordinal para textos normalizados por las reglas técnicas existentes y numérica para medidas/cantidad. TechnicalDifference contiene field (enum serializado como string), previousValue y currentValue (representaciones textuales invariantes, también para números). No compara imágenes, Section/SourceRow ni PELÍCULA PROTECCIÓN. No persiste una tabla de diferencias: ambos snapshots permiten reconstruirlas.

**Respuesta de preview:** projectId, projectTotal, items, hasTechnicalDifferences y technicalDifferencesCount. Cada ítem contiene section, sourceRow, commercialCatalogItemId, values con los once campos técnicos, unitPrice, totalPrice y technicalDifferences. El resumen cuenta campos cambiados. No devuelve rutas, claves de almacenamiento, configuración, tokens ni imágenes duplicadas. Los cambios técnicos son informativos y permiten confirmar.

**Workflow:** exige Project existente, una ReviewQuotation Commercial/InProgress con StartedAt y sin CompletedAt, asignada exactamente al Subject del Jefe configurado; versión inicial, catálogo, ítems/extracciones, confirmed.xlsx disponible y coincidencia de Project.SgCode con el SG histórico y el XLSX recibido. Cotización comercial o PresentQuotationToClient existentes producen AlreadyConfirmed/409. La confirmación exige además creador conocido: un legacy sin Subject devuelve ProjectCreatorUnavailable/409; el preview puede funcionar. No hay fallback de asesor.

`ConfirmCommercialQuotationUseCase` vuelve a parsear el archivo mediante la preparación compartida con preview. Construye CommercialQuotation con el actor real de ICurrentUser (Subject requerido, Email/Name snapshots opcionales) y CommercialQuotationItems con valores e importes del XLSX. Los cambios comerciales no sobrescriben CommercialCatalog, CommercialCatalogItem ni CommercialCatalogItemExtraction: esos registros son historia de Presupuestos; CommercialQuotationItem tiene precedencia como realidad comercial vigente después de confirmar.

| Entidad | Datos persistidos |
| --- | --- |
| CommercialQuotation | Id, ProjectId, BudgetQuotationVersionId, CommercialCatalogId, OriginalFileName, FileStorageKey, ProjectTotal, ConfirmedAt UTC, ConfirmedByGoogleSubject, ConfirmedByEmail nullable, ConfirmedByName nullable |
| CommercialQuotationItem | Id, CommercialQuotationId, CommercialCatalogItemId, Section, SourceRow, los once valores técnicos, UnitPrice, TotalPrice |

Las FK son Restrict; no hay cascadas que borren historia. Las entidades mantienen setters privados y constructor privado para EF. Se conservan snapshots del actor sin tokens ni roles. CommercialCatalogItemId permite reutilizar imágenes históricas; no existe nueva columna ni archivo de imagen comercial.

Índices/constraints: **UX_CommercialQuotations_ProjectId** garantiza una cotización inicial por proyecto; FileStorageKey es único. Los ítems son únicos por (CommercialQuotationId, Section, SourceRow) y por (CommercialQuotationId, CommercialCatalogItemId), además de los índices de FK. CHECKs protegen economía no negativa, Subject no vacío, valores técnicos positivos, ItemNumber no vacío, fila positiva y sección válida. **UX_ProjectTasks_ProjectId_PresentQuotationToClient**, UNIQUE(ProjectId) WHERE Type=3, conserva los índices Type=1 y Type=2. Los nombres usados para traducir conflictos son explícitos.

**Transacción y archivos:** EfCommercialQuotationStore genera el plan después del parsing; abre transacción y bloquea Project, tareas y versión inicial mediante FOR UPDATE en ese orden. Revalida workflow y el catálogo usado. Lee y conserva los bytes del temporal antes de modificar archivos. Completa ReviewQuotation sin cambiar StartedAt; crea PresentQuotationToClient=3 en Commercial, le asigna Project.CreatedByGoogleSubject y llama Start inmediatamente. AssignedAt/StartedAt son UTC, CompletedAt null, DeadlineAt calculado una vez y WasOverdue=false; registra TaskAssigned y resuelve el atraso de ReviewQuotation en la misma transacción. No completa la presentación ni avanza a aprobación o pago.

Orden: limpiar la referencia mediante **BudgetQuotationVersion.ClearConfirmedFileStorageKey()**, agregar cotización/ítems/tarea y un SaveChanges dentro de la transacción; guardar los bytes originales subidos en `projects/{projectId}/commercial-quotations/{commercialQuotationId}/commercial.xlsx`; eliminar físicamente confirmed.xlsx; commit. El XLSX comercial no se regenera, reescribe ni sanea. OriginalFileName se reduce al nombre final y ASP.NET File result serializa Content-Disposition seguro. Original.xlsx e imágenes no reciben escrituras ni borrados.

Ante fallo se intenta rollback. Con rollback confirmado, o antes de haber intentado commit, se restaura el temporal si su eliminación se intentó y se elimina exclusivamente el archivo comercial creado por la operación. La restauración verifica si el temporal ya existe y coincide, sin sobrescribir otro contenido. Se conservan entradas ajenas del ChangeTracker y se restauran/desacoplan las propias. Compensación usa CancellationToken.None. Los errores de rollback, limpieza o restauración se propagan para inspección; no se devuelve éxito. Si se intentó commit y su resultado es incierto porque también falla rollback, se conserva commercial.xlsx y no se altera ciegamente la retención. Un crash puede requerir reconciliación manual: PostgreSQL y filesystem no constituyen una transacción distribuida.

Solo 23505 sobre UX_CommercialQuotations_ProjectId o UX_ProjectTasks_ProjectId_PresentQuotationToClient se traduce a AlreadyConfirmed/409, después de compensar. Otros errores siguen siendo técnicos. Los locks serializan requests y las restricciones DB son la garantía adicional. Dos confirmaciones generan una ganadora y un conflicto, una cotización con sus ítems, una presentación y un archivo final.

**Respuesta 201:** projectId, commercialQuotationId, projectTotal, commercialFileAvailable, reviewTask, presentQuotationToClientTask, hasTechnicalDifferences y technicalDifferencesCount. Las tareas usan el DTO existente con assignedToGoogleSubject y enums como strings. Los listados por proyecto o Commercial/InProgress pueden devolver ReviewQuotation o PresentQuotationToClient según el estado real.

**Descarga final:** DownloadCommercialQuotationFileUseCase exige CommercialHead activo con ReviewQuotation asignada o CommercialAdvisor activo con Subject igual al creador del Project, antes de leer storage. Email no participa; otro autenticado recibe 403 y sin autenticación responde 401 mediante la política global. Proyecto inexistente o fuera del ámbito: 404; sin cotización confirmada: 409; archivo referenciado ausente: 500 genérico. GET entrega bytes almacenados y OriginalFileName, sin regeneración ni escrituras. La antigua ruta budget-quotation/commercial-file permanece exclusiva del Jefe, responde 409 al quedar la clave temporal null y nunca sustituye la descarga final. Limpiar esa clave no permite reenviar: ReviewQuotation sigue protegiendo el flujo.

**Validación:** tests sintéticos cubren importes y cachés, cultura invariante, ceros, fracciones y magnitudes grandes; once diferencias independientes y múltiples; claves Main/Additional ausentes/adicionales; workflow, legacy y reparseo; autorización 401/403 por Subject; descarga y contratos; retención real en TemporaryStorage; fallos de SaveChanges, guardado, borrado antes/después, commit, rollback, limpieza y restauración. Los tests HTTP concurrentes usan persistencia simulada serializada y la fase real de archivos del store. El smoke PostgreSQL comprueba además una confirmación ganadora y AlreadyConfirmed, registros/fechas/asignación, importes exactos y retención. Una falla inyectada después del borrado físico del temporal comprobó rollback real y restauración byte por byte. Se retiraron solo IDs sintéticos y se verificaron huellas de los datos preexistentes.

**Corpus local de solo lectura:** 66 XLSX, 62 aceptados por ambos parsers y 4 rechazos ya existentes del parser técnico; 1.038 ítems económicos leídos. Se observaron totales principales en filas 147, 217, 245, 343, 344 y 364. No hubo rechazos económicos adicionales ni se relajó el formato para recotizaciones/CONTRATADO-MODIFICADO. No se copiaron archivos ni datos de clientes al repositorio ni se confirmó un proyecto real.

### Errores

- `[ApiController]` responde 400 para errores de binding, JSON, Guid, enum o campos de texto requeridos.
- Las invariantes permanecen en Domain. `ApiExceptionHandler` convierte `ArgumentException` y sus derivadas de validación en 400 con un ProblemDetails genérico.
- Los Controllers convierten los resultados inexistentes en 404 y AlreadySent en 409 con ProblemDetails. Clientes y proyectos utilizan `CreatedAtAction` para los 201, cuya cabecera Location apunta al GET correspondiente; el envío a Presupuestos responde 201 con la tarea y sin Location; el envío a Comercial responde 200 con ambas tareas.
- El middleware de excepciones está activo también en Development. Los errores inesperados responden 500 con ProblemDetails genérico, sin mensajes de EF/Npgsql, SQL, stack traces ni detalles de configuración. ApiExceptionHandler registra explícitamente estos errores con ILogger, pues .NET 10 puede suprimir el diagnóstico del middleware para excepciones atendidas.
- Cada store traduce únicamente sus conflictos únicos esperados: los índices PrepareQuotation y ReviewQuotation a AlreadySent/409, el índice SG a SgCodeAlreadyInUse/409, y los índices de versión de Presupuestos, cotización comercial por proyecto y PresentQuotationToClient a AlreadyConfirmed/409. Otros errores de persistencia no se confunden con duplicados esperados. Las FK siguen protegiendo la integridad si un cliente o proyecto desaparece externamente entre la comprobación y el guardado; la API no ofrece eliminación.

### Prueba manual

Con la API iniciada por el perfil HTTPS y `$authHeaders` definido con un ID token válido según la sección de autenticación, ejecutar desde PowerShell con datos ficticios:

```powershell
$baseUrl = 'https://localhost:7265'
$authHeaders['X-SnG-Active-Role'] = 'CommercialAdvisor'
$clientBody = @{ name = 'Cliente Demo' } | ConvertTo-Json
$client = Invoke-RestMethod -Headers $authHeaders -Method Post -Uri "$baseUrl/api/clients" -ContentType 'application/json' -Body $clientBody

$projectBody = @{
    clientId = $client.id
    name = 'Casa Campestre'
    city = 'Bucaramanga'
    productLine = 'Signature'
} | ConvertTo-Json
$project = Invoke-RestMethod -Headers $authHeaders -Method Post -Uri "$baseUrl/api/projects" -ContentType 'application/json' -Body $projectBody

Invoke-RestMethod -Headers $authHeaders "$baseUrl/api/clients/$($client.id)"
Invoke-RestMethod -Headers $authHeaders "$baseUrl/api/projects/$($project.id)"
Invoke-RestMethod -Headers $authHeaders "$baseUrl/api/clients"
Invoke-RestMethod -Headers $authHeaders "$baseUrl/api/projects"

# Antes del envío, cargar al menos un documento mediante el contrato batch documentado.
# El token del envío debe pertenecer al Asesor creador con CommercialAdvisor activo.
# Debe estar vacío: crear Project no lo envía a Presupuestos.
Invoke-RestMethod -Headers $authHeaders "$baseUrl/api/projects/$($project.id)/tasks"
$task = Invoke-RestMethod -Headers $authHeaders -Method Post -Uri "$baseUrl/api/projects/$($project.id)/send-to-budgets"
$task # Budgets / PrepareQuotation / Pending; fechas de inicio y fin nulas.
$budgetsHeaders = @{ Authorization = "Bearer <GOOGLE_ID_TOKEN_PRESUPUESTOS>"; 'X-SnG-Active-Role' = 'Budgets' }
Invoke-RestMethod -Headers $budgetsHeaders "$baseUrl/api/project-tasks?area=Budgets&status=Pending"
Invoke-RestMethod -Headers $authHeaders "$baseUrl/api/projects/$($project.id)/tasks"
# Este segundo envío debe producir HTTP 409 (Invoke-RestMethod informa el error).
Invoke-RestMethod -Headers $authHeaders -Method Post -Uri "$baseUrl/api/projects/$($project.id)/send-to-budgets"

# Usar códigos libres; el sistema no calcula el siguiente SG.
# Desde aquí, usar un token de la cuenta de Presupuestos configurada.
$sgUrl = "$baseUrl/api/projects/$($project.id)/sg-code"
$assignment = Invoke-RestMethod -Headers $budgetsHeaders -Method Put -Uri $sgUrl -ContentType 'application/json' -Body '{"sgCode":" sg0012 "}'
$originalStart = $assignment.prepareQuotationTask.startedAt
$correction = Invoke-RestMethod -Headers $budgetsHeaders -Method Put -Uri $sgUrl -ContentType 'application/json' -Body '{"sgCode":"sg0042"}'
$repeat = Invoke-RestMethod -Headers $budgetsHeaders -Method Put -Uri $sgUrl -ContentType 'application/json' -Body '{"sgCode":" SG0042 "}'
$originalStart -eq $correction.prepareQuotationTask.startedAt # True
$originalStart -eq $repeat.prepareQuotationTask.startedAt # True
$correction.project.updatedAt -eq $repeat.project.updatedAt # True
Invoke-RestMethod -Headers $budgetsHeaders "$baseUrl/api/project-tasks?area=Budgets&status=InProgress"
```

Los POST de creación y el primer envío generan registros reales y responden 201; los GET del ejemplo responden 200. El segundo envío responde 409 sin escribir otra tarea. Un Guid válido inexistente responde 404 al consultar entidades, enviar o consultar su historial. Cambiar `productLine`, `area` o `status` por un número o nombre desconocido, omitir área/estado, dejar `name`/`city` en blanco o enviar `Guid.Empty` como `clientId` produce 400. Un proyecto con cliente inexistente produce 404 sin insertar el proyecto.

Solo existe baja lógica documental antes del handoff; no hay borrado de proyectos/clientes. Para validaciones controladas en una base compartida, identificar los registros de prueba con una marca única y retirar exclusivamente esos registros mediante una operación administrativa segura, primero las ProjectNotifications de la prueba, después ProjectTasks, Projects y finalmente Clients; si la prueba incluye cotizaciones, retirar previamente sus registros dependientes en orden de FK. No modificar datos ajenos a la prueba.

### Validación de concurrencia real

La suite sin conexión no sustituye esta comprobación. Crear un cliente y un proyecto nuevos con una marca única, y lanzar dos POST simultáneos al envío de ese proyecto con clientes HTTP reales. Esperar todas las respuestas y comprobar exactamente un 201 y un 409, sin 500. Consultar PostgreSQL con los identificadores de la prueba y verificar una sola fila ProjectTasks con ProjectId del proyecto temporal y Type=1. Si se lanzan más solicitudes, una sola debe crear y todas las demás deben responder 409.

Para SG, crear dos proyectos temporales enviados a Presupuestos y elegir un código libre. Lanzar dos PUT simultáneos con ese mismo código. Exigir **1×200 y 1×409**, y consultar la base: solo un proyecto debe tenerlo; el rechazado debe conservar SgCode=null, tarea Pending y StartedAt=null. No basta con comprobar los códigos HTTP.

## Lead Time, calendario y notificaciones

### Configuración y tiempo

`Authentication:BudgetsAssigneeSubject` es requerida mediante BudgetsAssigneeOptions y ValidateOnStart. Null, vacío y whitespace fallan con `Configure Authentication:BudgetsAssigneeSubject.`, sin revelar el valor. ConfiguredBudgetsAssigneeIdentity implementa IBudgetsAssigneeIdentity en Api; Application solo conoce Subject. Es una cadena opaca conservada exactamente: no se recorta, cambia de case ni sustituye por email. Configurar con User Secrets o `Authentication__BudgetsAssigneeSubject`; no versionar el valor real. Los tests usan valores sintéticos. Esta identidad también autoriza las operaciones de Presupuestos mediante la política BudgetsAssignee.

```shell
dotnet user-secrets set "Authentication:BudgetsAssigneeSubject" "<GOOGLE_SUBJECT_PRESUPUESTOS>" --project src/Api
```

TaskTiming recibe TimeProvider (TimeProvider.System en producción) y ColombiaBusinessCalendar. El calendario convierte instantes UTC mediante TimeZoneInfo.FindSystemTimeZoneById("America/Bogota"), trabaja con DateOnly local y convierte horas empresariales a UTC; no depende de la zona del servidor ni usa un offset fijo. No hay paquetes, APIs o conectividad para festivos. Las pruebas utilizan un TimeProvider controlado sin paquetes adicionales.

La política declara por separado cinco días para PrepareQuotation=1, ReviewQuotation=2 y PresentQuotationToClient=3. Un tipo sin política falla explícitamente. Se excluye el día inicial, se avanza por lunes a sábado que no sean festivos y se fija el quinto día a las 17:00 locales. Iniciar el 14/10/2026 produce **20/10/2026 17:00 America/Bogota = 22:00 UTC**. DeadlineAt queda persistido y congelado; no se recalcula al leer, corregir SG, reiniciar o ejecutar el worker.

ColombiaBusinessCalendar deriva los festivos fijos, los trasladados al lunes y los dependientes de Pascua conforme al [artículo 1 de la Ley 51 de 1983](https://www.funcionpublica.gov.co/eva/gestornormativo/norma.php?i=4954). EasterSunday usa el algoritmo gregoriano Meeus/Jones/Butcher: Jueves/Viernes Santo son Pascua−3/−2; Ascensión, Corpus Christi y Sagrado Corazón se calculan con +39/+60/+68 y se trasladan al lunes cuando corresponda. Los fijos sin traslado son 1 de enero, 1 de mayo, 20 de julio, 7 de agosto, 8 y 25 de diciembre; los trasladados son 6 de enero, 19 de marzo, 29 de junio, 15 de agosto, 12 de octubre, 1 y 11 de noviembre.

Desde 2026 incluye además el 9 de julio, trasladado al lunes por el [artículo 6 de la Ley 2578 de 2026](https://lector.ramajudicial.gov.co/SIDN/NORMATIVA/TEXTOS_COMPLETOS/7_LEYES/LEYES%202026/Ley%202578%20de%202026.pdf); en 2026 se observa el 13 de julio. No se aplica retroactivamente a años anteriores. Se prueban las 19 fechas observadas de 2026. No hay calendarios regionales, cierres internos ni excepciones manuales.

### Estado persistido y proyección

ProjectTask incorpora DeadlineAt nullable y WasOverdue nullable. Pending nuevo tiene ambos null; Start exige instantes UTC explícitos y fija false. DetectOverdue marca true solo si InProgress y now > DeadlineAt. Complete usa un instante explícito: <= deadline cumple, > deadline incumple y true nunca vuelve a false. Si el reloj se corrige hacia atrás después de detectar atraso, se conserva true y ResolvedAt registra el CompletedAt real, aunque quede antes de ScheduledAt; no se inventa una fecha posterior. Las tareas legacy sin deadline permanecen desconocidas, incluso al completarse. No hay backfill ni cálculo retroactivo durante la migración.

Las comparaciones usan el reloj sin redondearlo antes de evaluar los límites. PostgreSQL conserva microsegundos: las transiciones existentes ajustan sus timestamps; una finalización estrictamente posterior al deadline se lleva al siguiente microsegundo representable si hace falta, para no convertirla en igualdad al persistir. El resto conserva el ajuste de precisión existente. AssignedAt se suministra desde el reloj a los tres flujos de asignación.

ProjectTaskResponse conserva sus nueve campos previos y agrega deadlineAt, wasOverdue y leadTimeStatus. El último no se persiste y se serializa como string:

| Estado | Regla |
| --- | --- |
| NotStarted | Pending sin deadline |
| UntrackedLegacy | InProgress/Completed sin deadline o cumplimiento desconocido |
| OnTrack | InProgress antes del umbral de aviso previo |
| DueSoon | Desde dos días laborales antes a las 08:00, hasta antes del día de vencimiento |
| DueToday | Día local de vencimiento, hasta el deadline inclusive |
| Overdue | InProgress y now > DeadlineAt |
| CompletedOnTime / CompletedLate | Completed según WasOverdue false / true |

GET no persiste WasOverdue ni genera notificaciones. Por eso puede mostrar Overdue antes de que el próximo sweep haya persistido true. DueToday se muestra desde el inicio del día; su notificación se genera desde las 08:00.

### Entidad, eventos y trazabilidad

ProjectNotification tiene doce columnas explícitas: Id, ProjectId, ProjectTaskId, RecipientGoogleSubject, Kind, State, Title, Message, ScheduledAt, CreatedAt, ResolvedAt y EventKey. Guid generados por Domain; textos text; Kind/State integer; fechas timestamp with time zone. Solo ResolvedAt admite null. FK a Project y ProjectTask con Restrict. CHECKs validan textos no vacíos, Kind válido y coherencia entre tipo, estado y resolución. No hay Users, roles, tokens ni campos leído/no leído.

NotificationKind: TaskAssigned=1, DueInTwoBusinessDays=2, DueToday=3, Overdue=4, ProjectDocumentsUpdated=5. NotificationState: Informational=1, Active=2, Resolved=3. Solo Overdue puede estar Active/Resolved; los demás son Informational. Los títulos se declaran para cada tipo de tarea y evento en lenguaje operativo, sin nombres técnicos. Message remite al detalle del proyecto sin inventar contexto.

Para TaskAssigned, DueInTwoBusinessDays, DueToday y Overdue, EventKey combina TaskId, Kind, SHA-256 del Subject exacto y AssignedAt a precisión de microsegundos. Ese instante identifica el ciclo actual; la futura reasignación podrá distinguir otro ciclo sin borrar historia. El hash evita introducir el Subject literal en la clave, pero no reemplaza la autorización. EventKey no se devuelve por HTTP. La unicidad real es **UX_ProjectNotifications_EventKey**; los otros índices son RecipientGoogleSubject+CreatedAt, ProjectId+CreatedAt y ProjectTaskId.

ScheduledAt es el instante empresarial; CreatedAt el momento de generación del registro. TaskAssigned usa AssignedAt como ScheduledAt y se inserta en la misma operación/transacción que la nueva tarea, incluso Pending. Los avisos temporales usan exclusivamente el deadline almacenado: dos días laborales hacia atrás a las 08:00, el día de vencimiento a las 08:00 y atraso solo después de DeadlineAt. Overdue conserva DeadlineAt como ScheduledAt. Un deadline de martes genera el recordatorio previo el sábado, si no hay festivos intermedios.

Al completar tarde, se resuelve Overdue existente con ResolvedAt=CompletedAt; si falta, se crea ya resuelto. Se preservan título, mensaje, destinatario, ScheduledAt y CreatedAt de avisos existentes. No se borran notificaciones ni se generan recordatorios para tareas ya completadas. EfProjectNotificationWriter prepara cambios dentro del DbContext de la transición sin guardar ni confirmar por separado. Sus snapshots restauran tracking y valores originales ante rollback; los registros nuevos se desacoplan. Las estrategias de archivos conservan su compensación y tratamiento de commit incierto.

### Worker y recuperación

ProjectNotificationBackgroundService ejecuta un sweep inmediato y usa PeriodicTimer de un minuto con TimeProvider. Crea un scope por iteración, respeta cancelación y registra únicamente el tipo de excepción de un fallo; el siguiente intervalo reintenta sin tumbar la API. ProjectNotificationSweepService captura ahora desde el reloj y delega una iteración; ProjectNotificationPlanner contiene las reglas determinísticas reutilizadas por workflow y sweep.

EfProjectNotificationSweepStore consulta tareas relevantes en lotes de hasta 200 con avance por Id. Pending/InProgress con asignado recuperan TaskAssigned faltante. InProgress con deadline permite detectar avisos/atraso, aunque falte asignado; en ese caso puede guardar WasOverdue pero no inventa destinatario. Completed solo entra para evidencia tardía faltante o Overdue activo pendiente de resolver. No recorre todo el historial en memoria ni genera asignaciones para legacy Completed.

Cada lote abre transacción, bloquea Projects por Id y luego tareas por Id, siguiendo el orden del workflow, carga las notificaciones del lote juntas y revalida los estados actuales. No hay una consulta por tarea. Bloqueos más índice único permiten varias instancias; únicamente SQLSTATE 23505 con UX_ProjectNotifications_EventKey se trata como carrera conocida, revierte el lote y permite reconciliar en el siguiente sweep. Otros errores llegan al supervisor del worker para registro y reintento. No hay Redis, broker, Hangfire ni transacción distribuida.

Tras downtime se crea **solo el hito temporal actualmente más relevante**: Overdue tiene prioridad, después DueToday y después DueInTwoBusinessDays. No se reconstruyen avisos menos relevantes omitidos ni se eliminan los que sí fueron emitidos. La recuperación de TaskAssigned es independiente e idempotente. Un aviso previsto a las 08:00 generado a las 09:20 conserva ambos instantes.

Si el proceso está detenido no emite avisos. En Cloud Run u otro hosting con scale-to-zero será necesario mantener una instancia activa con CPU disponible o ejecutar el sweep mediante un scheduler externo. El servicio permite esa integración futura; no se implementa Cloud Scheduler ni otro canal en este alcance.

### API y validación

GET /api/notifications usa exclusivamente ICurrentUser.Subject, ordena CreatedAt descendente e Id como desempate y rechaza cualquier query parameter con 400. GET /api/projects/{id}/notifications exige autenticación general, responde 404 si no existe el proyecto y devuelve todo su historial por CreatedAt ascendente con Id como desempate. Ambas consultas requieren un rol activo poseído; su contenido no se filtra por área. Sin autenticación responden 401.

ProjectNotificationResponse contiene id, projectId, projectTaskId, recipientGoogleSubject, taskType, kind, state, title, message, scheduledAt, createdAt, resolvedAt y deadlineAt. Incluye Informational, Active y Resolved; omite EventKey y cualquier configuración o credencial. Las consultas filtran y ordenan en SQL antes de construir los DTO.

La suite prueba calendario, límites exactos, políticas, deadlines congelados, proyección sin escritura, recuperación normal/tardía, concurrencia simulada, permisos/contratos HTTP y validación de configuración. El worker se prueba con un temporizador controlado, sin esperar minutos. Los tests HTTP remueven el worker real. La compensación verifica restauración de Overdue Active después de un rollback posterior a SaveChanges, además de los tests de archivos existentes.

El smoke PostgreSQL aplica la novena migración sin backfill, compara huellas de datos anteriores y ejecuta asignación, primer SG, sweeps concurrentes, recuperación, queries, resolución e índice único en un esquema sintético aislado. Retira ese esquema al finalizar y verifica que los datos preexistentes no cambiaron. Los tests normales y el runtime del calendario no necesitan Internet. Los parsers XLSX permanecen intactos.

La regla futura de reasignación conserva historia, emite otro TaskAssigned y reinicia cinco días desde el siguiente laboral para el nuevo destinatario. No existe endpoint ni implementación de reasignación, resolución manual, read/unread o canales externos.

## Reglas permanentes para continuar

- Implementar solo funcionalidad solicitada y reglas confirmadas. Reportar decisiones pendientes sin codificarlas silenciosamente.
- Mantener invariantes en Domain, coordinación de casos de uso en Application, persistencia e integraciones en Infrastructure y Controllers delgados en Api.
- Respetar referencias y namespaces. No introducir abstracciones, servicios, patrones ni librerías sin necesidad actual.
- Eliminar código muerto, dependencias sin uso, duplicación y ejemplos de template. No conservar código comentado ni versiones históricas; Git conserva la historia.
- Revisar y mantener ambos README como documentación viva: el funcional explica negocio y alcance, el técnico explica implementación y operación. No son changelogs.
- Validar herramientas, restore, build, tests y migraciones; corregir warnings atribuibles al código sin ocultarlos mediante configuración. Reportar los que legítimamente permanezcan y cualquier supuesto o impedimento.
- No incluir secretos reales en archivos versionados. No aplicar migraciones automáticamente al arrancar.

Siguen pendientes la aprobación del cliente, anticipo, finalización de PresentQuotationToClient, reasignación de asesor/tareas y, en alcance posterior, la edición de otros campos, eliminación de proyectos/clientes, generación automática de SG, auditoría/historial de cambios SG, nuevas versiones y edición posterior de cotizaciones, revisiones comerciales posteriores, devolución de tareas y demás módulos funcionales. El preview XLSX de Presupuestos es stateless; su confirmación crea el catálogo base y conserva el documento inicial, sin implementar versión 2 ni edición posterior. La bandeja personal filtra rol activo, tipo, área, Subject asignado y estado, dentro del ámbito de lectura del proyecto; notificaciones personales se filtran exclusivamente por la identidad autenticada; no hay paginación, búsqueda ni orden configurable. Las reglas de reenvío/retrabajo requieren definición posterior; el flujo inicial vigente admite una sola PrepareQuotation, ReviewQuotation y PresentQuotationToClient por proyecto.

### Verificación de la bandeja personal

TaskInboxTests usa un mismo Subject multirol para comprobar los tres estados, tipo/área/assignee exactos, tareas ajenas o sin asignación, 403 de área incompatible/Management y conservación del historial completo por proyecto. SharedReadAccessPostgresTests verifica el filtro SQL con las tres tareas asignadas al mismo Subject y rollback sintético. No hay migración ni cambios de ownership, assignment, handoffs, deadlines o notificaciones.

Smoke: Presupuestos → Tareas abre Pending; después del envío, PrepareQuotation aparece al elegir Completed pero ReviewQuotation nunca aparece en esa bandeja. Jefe → Tareas abre InProgress con ReviewQuotation; Asesor → Tareas abre InProgress con PresentQuotationToClient después de 4E. El expediente continúa mostrando las tres tareas según su historial. Gerencia usa Operación.

### Verificación de lectura compartida por área

SharedReadAccessTests cubre listados y URLs directas con un mismo Subject multirol, assignments completados, proyectos ajenos y legacy; IdentityAreasTests conserva aislamiento de escrituras. SharedReadAccessPostgresTests verifica la traducción SQL de los ámbitos y clientes relacionados con filas sintéticas y rollback, sin cambios persistentes ni archivos. Para ejecutarlo localmente: SNG_RUN_POSTGRES_SMOKE=1 y dotnet test --filter FullyQualifiedName~SharedReadAccessPostgresTests. No requiere corpus XLSX ni migración.

Smoke manual: seleccionar Presupuestos, entrar directamente a Clientes y Proyectos y abrir un expediente asignado; verificar documentos, snapshot y acciones Budget según estado, sin creación ni envío inicial. Completar el handoff y comprobar que el expediente sigue visible. Cambiar a Jefe Comercial y comprobar lectura con ReviewQuotation asignada, snapshot de Presupuestos y revisión Comercial, sin SG ni edición documental; completar ReviewQuotation y comprobar acceso histórico. Cambiar a Gerencia y consultar cualquier cliente/proyecto sin escrituras. Una URL ajena en Budgets/Head debe responder 403/404 y no mostrar información. Repetir tras reload y en 375/768/1024 px y desktop.


## Money Contract V1

Commercial expone `currencyCode: "COP"` en preview, confirmación y vista persistente
`GET /api/projects/{id}/commercial-quotation/view`. Es una propiedad contractual derivada de
`Money.CurrencyCode`, no una columna repetida ni una moneda inferida por clientes.
Budget no añade moneda: su vista técnica no incluye importes.

`Money.Normalize` es la política única: decimal no negativo, dos decimales y
`MidpointRounding.AwayFromZero`. Máximo `999999999999999999.99`, correspondiente a numeric(20,2).
Parser y constructores de dominio la aplican a ProjectTotal, UnitPrice y TotalPrice, incluso
si el caller no usa XLSX. Los valores fuera de rango se rechazan. No se recalculan totales,
precios por cantidad, IVA ni descuentos; las medidas Width/Height/SquareMeters no cambian.
El archivo comercial conserva sus bytes originales, aunque el snapshot normalice sus importes.

La nueva migración `FormalizeCommercialQuotationMoney` ejecuta ROUND explícito sobre esos
campos antes de cambiar sus columnas. PostgreSQL numeric resuelve empates alejándose de cero,
igual que la política del dominio ([documentación](https://www.postgresql.org/docs/16/datatype-numeric.html)).
Conserva filas, PK, FK e índices únicos; no cambia migraciones anteriores. Un valor fuera del
rango impide la migración transaccional, no se recorta silenciosamente. Down amplía el tipo,
pero no puede recuperar los decimales ya redondeados.

Tras publicar, aplicar manualmente a la base configurada:

```powershell
dotnet ef database update --configuration Release --project src/Infrastructure --startup-project src/Api -- --environment Development
```

No se aplica automáticamente en startup ni desde tests. Las pruebas opcionales
`MoneyMigrationPostgresTests` requieren SNG_MONEY_TEST_DATABASE apuntando a un clúster aislado
loopback con una base vacía `money_contract_test`; no leen User Secrets ni configuración de la API.
`MoneyCorpusTests` usa SNG_MONEY_CORPUS_ZIP, abre el ZIP externo en solo lectura y comprueba
únicamente los contadores del corpus aprobado y la escala monetaria. No copia documentos a Git.
La suite normal omite estos dos smokes si sus variables no están definidas.

Frontend requiere el pequeño ajuste de sus contratos cerrados para aceptar y exigir currencyCode;
el payload nuevo y los consumidores actualizados deben desplegarse juntos. Intelligence valida
COP y precisión, no vuelve a redondear. La consulta global de precios de todas las cotizaciones
(quotation portfolio analytics) queda fuera de Money Contract V1.


### Lectura agregada de cotizaciones

ManagementController expone GET /api/management/quotation-portfolio bajo ManagementRequirement.
GetManagementQuotationPortfolioUseCase arma cobertura y aplica Money.CurrencyCode;
IManagementReadStore.GetQuotationPortfolioAsync obtiene dos queries de resumen y tres top 10
server-side en EfManagementReadStore.Portfolio. RepeatableRead conserva una única vista entre
los SELECT, secuenciales sobre el mismo DbContext, con CancellationToken. No hay N+1 ni tracking.

Los tests normales verifican autorización, OpenAPI y traducción Npgsql de las queries de portfolio y mix.
El test optativo QuotationPortfolioTests.RealQueriesAreBoundedExactStableAndDoNotExposeInternalIds
requiere SNG_PORTFOLIO_TEST_DATABASE: exclusivamente loopback 127.0.0.1, base vacía
quotation_portfolio_test en cluster temporal sintético. Aplica migraciones solo en esa base,
verifica resultados, empates, cobertura y 21 lecturas constantes. Nunca usa configuración de
la aplicación. El ejecutor del smoke debe detener y eliminar su cluster temporal al finalizar.


Mix V2 agrega cuatro dimensiones en EfManagementReadStore.Mix: una consulta de
cobertura/maximo monetario y tres rankings por dimension. GROUP BY, COUNT, SUM,
ORDER BY y LIMIT se ejecutan en PostgreSQL. Son 21 SELECT constantes dentro de la
misma transaccion RepeatableRead, incluso con portfolio vacio. La cobertura distingue
items especificados y vacios; las combinaciones exigen ambos valores. No se modifican
entidades ni migraciones. El smoke sintetico verifica rankings, empates, collation C,
precision, overflow fuera del top, cobertura y aislamiento de cada lectura.


## Commercial Presentation Outcome V1

El asesor asignado registra la respuesta del cliente con
`POST /api/projects/{id}/client-quotation-decision`: `{ decision, note }`.
Decision acepta exactamente Approved, ChangesRequested o Rejected; nota opcional hasta
2000 caracteres. El contrato rechaza campos adicionales y fechas del cliente.
Requiere ActiveRole CommercialAdvisor y Subject asignado a PresentQuotationToClient
InProgress, iniciada, sin completar, con cotización comercial confirmada y revisión
completada. Una inconsistencia devuelve 409; otro asesor recibe 403.

`ClientQuotationDecision` es una entidad inmutable separada, ligada a Project,
CommercialQuotation y ProjectTask. La migración AddClientQuotationDecisions crea la tabla,
FK Restrict, checks y dos índices únicos independientes (cotización y presentación).
No modifica migraciones anteriores ni CommercialQuotation. Aplicar la migración manualmente
antes de desplegar este contrato; no se aplica al iniciar la API.

La transacción bloquea proyecto/tareas, comprueba estado y decisión previa, inserta la
respuesta y llama ProjectTask.Complete con la misma hora UTC de registro. Reutiliza la
reconciliación de notificaciones, incluido atraso/resolución. Las solicitudes concurrentes
no sobrescriben: una gana y otra devuelve 409 AlreadyRecorded. Un fallo revierte todo.
201 devuelve `{ outcome, task }`; outcome usa el mismo contrato de lectura.

`GET /api/projects/{id}/client-quotation-decision` usa el acceso compartido existente.
200 devuelve available/projectId/commercialQuotationId/presentQuotationTaskId/decision/note/
recordedAt. Sin registro: available=false y todos los campos opcionales null. Un proyecto
inexistente o fuera del alcance devuelve 404 ProjectNotFound. No expone identidad del asesor.

Approved no implica pago o producción, ChangesRequested no inicia recotización y Rejected
no cierra el proyecto. Solo se registra la decisión y completa presentación; no hay edición,
nueva tarea automática de Accounting/Presupuestos ni nuevo estado de proyecto.

Pruebas normales offline; el smoke optativo SNG_DECISION_TEST_DATABASE exige un cluster
sintético loopback vacío con base client_decision_test. Valida migración, concurrencia,
rollback y acceso compartido. Nunca usar la base operacional para ese smoke.


## Comprobantes y verificación del anticipo

DepositVerificationService valida rol activo, selección documental, dinero decimal COP, fecha
con zona no futura y observación obligatoria de rechazo. Configuración privada:
Authentication:AccountingAssigneeSubject y Authentication:InstallationAssigneeSubject.
La primera es obligatoria al arrancar; la segunda bloquea solo aprobación si falta (503).
ConfiguredRoleResolver reconoce Installation; su ámbito y bandeja incluyen únicamente
TakeMeasurements asignadas; el checklist y la radicación se describen en Instalación.

Contratos (todos autenticados y limitados al ámbito del proyecto):
- GET /api/projects/{id}/deposit-verifications: projectId, available, canManage, canSubmit,
  canReview, accounting, documents y reviews. Las capacidades son informativas; cada escritura
  vuelve a autorizar. Review expone id/status/submittedAt/decidedAt/decidedBy(email,name)/note/
  documentIds; no expone storage keys ni Subjects.
- POST /api/projects/{id}/deposit-verifications/receipts/batch: multipart existente con manifest
  y files; 201 DocumentBatchResponse. Hasta 15 comprobantes vigentes, 100 MB por archivo y
  300 MB por carga, separados del cupo de documentos generales. Descarga mediante el endpoint
  documental existente. No aparecen en el ZIP ni en el envío documental a Presupuestos.
- POST /api/projects/{id}/deposit-verifications: {requestKey,documentIds}, 200 {reviewId}.
  RequestKey UUID estable por intento lógico; repetir la misma selección devuelve la misma
  revisión. Cambiar documentos con la misma clave da 409 VerificationRequestConflict.
- POST /api/projects/{id}/deposit-verifications/{reviewId}/decision:
  {approve,amount,receivedAt,note}, 204. Approve requiere amount string decimal positivo y
  receivedAt con zona; rechazo usa ambos null y note no vacía. Amount conserva Money.Normalize
  AwayFromZero/numeric(20,2); COP y RecordedAt son propiedad del servidor.
- GET /api/projects/{id}/initial-deposit conserva el contrato histórico AccountingState.
  Los POST antiguos initial-deposit y send-approved-quotation-to-accounting devuelven 409
  DepositVerificationRequired: no permiten eludir la revisión documental.

EfDepositVerificationStore bloquea proyecto → tareas → revisión y verifica la Comercial
vigente con Approved. El proyecto serializa también las cargas documentales. El envío reutiliza
AccountingHandoff/RegisterInitialDeposit; tras rechazo la tarea queda Pending a la espera del
nuevo envío. Cada solicitud conserva sus IDs exactos y cada decisión es única e inmutable.
Índices PostgreSQL garantizan clave idempotente, una revisión pendiente por proyecto, una
aprobación por handoff y una TakeMeasurements por proyecto. Los vínculos documentales usan
FK compuestas por proyecto y Restrict. Lecturas AsNoTracking en RepeatableRead.

Aprobar inserta InitialDeposit, inicia/completa RegisterInitialDeposit con RecordedAt, crea
TakeMeasurements Pending sin SLA y reconcilia notificaciones en la misma transacción.
Rechazar solo resuelve la revisión y notifica al asesor; no registra dinero ni crea Instalación.
DepositVerificationRequested/Approved/Rejected son informativas y únicas por revisión y
destinatario. TaskAssigned se conserva; no hay suscripciones de grupo. Rollback restaura
tracking. Las cargas reutilizan la compensación DB/filesystem y protección de rutas existente.

Migración 20261103134446_AddDepositVerificationReceipts: ProjectDocuments.IsPaymentReceipt
(false para documentos anteriores), DepositVerifications, DepositVerificationDocuments,
índices y restricciones de tarea/notificaciones. No modifica migraciones previas. Down rechaza
evidencia existente para evitar destruir historial. Aplicar en operación solo con autorización.
Los anticipos históricos siguen visibles; no se inventan comprobantes ni tareas retroactivas.

Prueba PostgreSQL aislada: SNG_VERIFICATION_TEST_DATABASE debe apuntar a 127.0.0.1/base
verification_test; ejecutar dotnet test -c Release --filter FullyQualifiedName~DepositVerification.
Usa archivos e identidades sintéticos y verifica migración, permisos, múltiples comprobantes,
idempotencia concurrente, rechazo/reenvío, aprobación única, notificaciones y rollback.
No requiere corpus XLSX, servicios externos ni migración de bases operativas.

El rechazo contable crea CorrectDepositReceipts (tipo 6, Commercial), asignada al asesor
responsable e iniciada sin SLA. Aparece en su bandeja En curso y enlaza el expediente.
El reenvío completa esa tarea en la misma transacción que crea la revisión contable.
Los rechazos consecutivos conservan tareas distintas ya completadas; solo puede existir
una corrección activa por proyecto. No se reabre PresentQuotationToClient.
La tarea es visible al asesor asignado y a Gerencia; las revisiones y comprobantes conservan
las lecturas compartidas existentes. No se crean correcciones retroactivas para rechazos previos.

20261103190730_AddDepositReceiptCorrectionTasks añade CorrectionTaskId nullable a
DepositVerification, FK Restrict y vínculo único; un índice parcial impide dos correcciones
activas por proyecto. El CHECK exige área Commercial y ausencia de ronda, deadline y atraso.
Down rechaza historia de corrección existente. Aplicar solo con autorización operativa;
la verificación automática utiliza exclusivamente el PostgreSQL temporal de pruebas.

Precisión técnica de comparación: ancho/alto a 3 decimales en metros (1 mm) y superficie a
6 decimales en m², con MidpointRounding.AwayFromZero. TechnicalMeasurePrecision se aplica
solo al comparar; CommercialTechnicalDifferences devuelve las diferencias reales con valores
normalizados legibles. Cantidades siguen siendo enteros exactos; precios, fórmulas y valores
originales persistidos no cambian. No es una tolerancia de fabricación o instalación.
Frontend implementa la misma regla mediante dígitos decimales/BigInt; los casos de prueba
compartidos por contrato cubren equivalencias, mitades, bordes, valores pequeños y grandes.


## Instalación: persistencia y radicación

`MeasurementChecklist` vincula proyecto, CommercialQuotation del InitialDeposit, ronda y tarea
TakeMeasurements. `MeasurementStructure` tiene UUID propio, FK al ítem cuando es cotizada,
ordinal/cantidad de origen y dimensiones descriptivas. Las adiciones nacen sin medir; retiros y
marcas guardan actor/UTC. `MeasurementEvent` conserva las correcciones, incluso desmarcados.
`MeasurementSubmissionDocument` vincula exactamente los documentos congelados; SnapshotJson
(jsonb) y SubmissionId conservan la radicación formal en el checklist. Ingeniería consume ese snapshot inmutable al crear su tarea y checklist en la misma transacción.

Rutas bajo `/api/projects/{id}/measurements`:
- GET: estado, responsable, resumen oficial, estructuras, documentos, historial y snapshot final.
- POST `/initialize`: generación idempotente y StartUntracked de la tarea existente.
- POST `/commands`: RequestKey, ExpectedRevision y Action (`Measure`, `Add`, `Retire`, `RecordActual`, `Submit`).
  Measure usa StructureId/Measured; Add usa Description/Width/Height; Retire usa StructureId;
  Submit exige DocumentIds iguales al conjunto vigente. Campos ajenos a la acción son rechazados.
- POST `/documents/batch?requestKey={uuid}`: multipart existente, archivos separados mediante
  IsMeasurementDocument; admite sustitución conservando historia. La primera carga exitosa
  conserva la clave de lote; repetir esa clave devuelve sus mismos IDs sin cargar otra vez.
  Usar una clave nueva cuando cambia la selección. Límites existentes: 15 vigentes, 100 MB por
  archivo y 300 MB por lote. Descargas mediante `/documents/{documentId}/file` con ámbito compartido.

Solo Installation activa, configurada y asignada escribe. Lecturas del checklist: Instalación
asignada y Management. Las mutaciones bloquean Project, luego ProjectTasks; las cargas comparten
el bloqueo Project. Unicidad PostgreSQL protege checklist por proyecto/tarea, unidad por ítem y
ordinal, y evento por RequestKey/revisión. ExpectedRevision rechaza cambios obsoletos (409);
repetir un comando idéntico devuelve éxito, reutilizar su clave con otro contenido devuelve 409.
Las lecturas usan RepeatableRead/AsNoTracking. La radicación persiste snapshot, enlaces, auditoría,
cierre de tarea y reconciliación de notificaciones en una transacción. No escribe nuevos archivos;
las cargas previas reutilizan la compensación documental. No existe atomicidad distribuida DB/filesystem.

La migración `AddInstallationMeasurements` añade cuatro tablas e IsMeasurementDocument.
No se aplica al arrancar ni se aplicó sobre la base operativa; requiere autorización de despliegue.
Las pruebas PostgreSQL usan exclusivamente `SNG_VERIFICATION_TEST_DATABASE` con host loopback y
base `verification_test`, datos sintéticos y almacenamiento temporal. No se requiere corpus XLSX.

`RecordActual` usa StructureId, ActualWidth, ActualHeight y ActualSystemText (libre, hasta 2000
caracteres). Ancho/alto deben ser decimales positivos; no hay redondeo ni modificación de los
valores originales. La acción conserva RequestKey/ExpectedRevision, bloqueo de proyecto/tareas,
rol Installation asignado y prohibición de modificar retiradas o radicadas. No altera el check
Medido ni el progreso. MeasurementEvent.DetailsJson conserva antes/después junto a actor/UTC.
Add inicializa la captura en obra desde su entrada, conservando también el registro inicial.
Note queda solo para compatibilidad histórica; el servicio rechaza notas nuevas de Add.

`20261104150914_AddInstallationActualMeasurements` añade tres columnas nullable a
MeasurementStructures y DetailsJson jsonb a MeasurementEvents. El CHECK exige los tres valores
presentes y válidos, o los tres nulos. No migra datos originales ni reescribe snapshots anteriores.
Las propiedades nuevas opcionales del snapshot se leen como null cuando faltan en versiones
históricas. La serialización de comandos sin captura mantiene su fingerprint previo para reintentos.

La lectura devuelve ImageCatalogItemId solo si existe una imagen persistida; une el ítem comercial
de la estructura con su extracción histórica en una consulta, sin usar la última cotización ni
leer XLSX. Reutiliza GET budget-quotation/items/{itemId}/image con autorización del proyecto.
No expone claves de almacenamiento. La radicación incluye campos de obra e imagen referenciada
en el snapshot; Management conserva lectura. La migración operativa requiere autorización.

Lectura y nuevas radicaciones comparten el orden SQL de estructuras: LEFT JOIN al
CommercialQuotationItem referenciado, Section, SourceRow, UnitOrdinal e Id de estructura.
Las agregadas (sin ítem) van al final por CreatedAt e Id; las retiradas no se excluyen del
orden histórico. No se consulta la cotización más reciente ni se reescriben snapshots ya radicados.

Ficha técnica y croquis por unidad:
- `RecordTechnical` en POST measurements/commands recibe StructureId y Technical: HandleStatus
  (null/NotApplicable/Confirmed), HandleHeightMm, ViewSide (Inside/Outside), OpeningType
  (NotApplicable/HingedDoor/HingedWindow/Other), OpeningDirection (Inward/Outward), OpeningHand
  (Left/Right = lado de bisagras desde ViewSide), HasAlfajia y AlfajiaMeasurementMm.
- Se admiten borradores incompletos, nunca combinaciones contradictorias. Manija positiva hasta
  5000 mm y no mayor al alto real; alfajía positiva hasta 10000 mm. Son límites de captura,
  no reglas de fabricación. No aplicable implica valor nulo. Ventanas no requieren lado.
- Measure=true valida medidas reales y ficha completa en servidor. Los registros ya medidos
  anteriores no se desmarcan ni reciben valores ficticios; pueden radicarse con sus datos históricos.
  Para guardar ficha incompleta sobre una unidad medida se debe desmarcar explícitamente primero.
- `RecordSketch` recibe StructureId y Sketch { Drawing, Webp (base64) }. Drawing v1 contiene
  Width=1200, Height=900, Strokes [{Tool: Pen/Eraser, Color, Width, Points:[{X,Y}]}]. Coordenadas
  normalizadas [0,1], proporción 4:3, hasta 200 trazos, 2000 puntos/trazo y 20000 puntos totales.
  Colores permitidos #111111/#0066cc/#cc2222, grosor 1–20 px del lienzo de referencia. Borrador
  pinta blanco sobre un fondo opaco. Límite JSON de trazos 1 MB; petición completa 2 MB;
  WEBP 512 KB: VP8 simple histórico o VP8X + ICCP + VP8 opaco, sin animación, alfa ni EXIF/XMP. Se verifican tamaños, orden de bloques, perfil ICC y dimensiones del contenedor y fotograma; se conservan los bytes del navegador. No hay SVG ni URLs.
  La descarga del reporte usa el nombre saneado `Radicacion de medidas-[Nombre del proyecto].pdf`, leído del proyecto autorizado; no cambia PDF, hash ni snapshot.
  El guardado valida contenedor/cabecera y límites; la generación del expediente decodifica también el WEBP y falla sin radicar si no es legible.
- Cada guardado crea MeasurementSketchVersion inmutable con DrawingJson, SHA256, actor/UTC y
  revisión; WEBP usa IFileStorage en clave generada por servidor por proyecto/estructura/versión.
  Versiones anteriores se conservan. Mínimo dos segundos entre versiones por unidad, bajo el
  mismo bloqueo de proyecto/tareas; replay de RequestKey no crea otra versión ni archivo.
- GET measurements/structures/{structureId}/sketches/{versionId} devuelve los trazos y referencia;
  GET con sufijo /image devuelve image/webp autenticado. Solo Installation asignada y Management
  mediante scope del proyecto; no se exponen claves físicas. La lectura verifica SHA256 del WEBP.
- Datos técnicos, referencia de versión, revisión y evento antes/después se confirman atómicamente.
  El archivo se escribe antes del commit, se elimina solo tras rollback conocido y se conserva
  ante commit incierto. Un fallo abrupto previo al commit puede dejar un archivo huérfano privado;
  no se elimina evidencia mediante una limpieza automática especulativa.
- El snapshot incluye Technical y SketchVersionId de la misma transacción; la versión referida
  contiene trazos y WEBP inmutables. No se reescriben snapshots previos ni archivos de cotización.
- Migración 20261104183948_AddInstallationTechnicalSketches: columnas nullable TechnicalJson,
  SketchVersionId, auditoría técnica/croquis, y tabla de versiones. Validar/aplicar solo con
  autorización operativa explícita; no hay backfill ni regeneración del checklist.
- mm es la unidad de negocio de todas las longitudes. Ancho/alto heredados permanecen en m por
  compatibilidad, con conversión centralizada en Frontend measurements/units.ts. Manija/alfajía
  se almacenan en mm; m² y dinero no se convierten. No cambiar contratos de otros módulos.


Ficha ampliada y expediente de Instalación:
- `RecordActual` añade `MeasurementType` (Agreed/Production/Opening nullable), `ActualLocation`
  (500 caracteres) e `InstallationObservations` (4000, opcional). Conserva ancho/alto en metros,
  comparación/presentación en mm y auditoría Before/After. No altera los datos cotizados.
- `Reinstate` usa RequestKey/ExpectedRevision y el bloqueo existente proyecto/tareas. Limpia el
  retiro vigente y Medido, sin borrar eventos ni crear unidades. Orden comercial/adiciones intacto.
- `ResolveComparison` recibe Comparison {Resolution: Comparable/NotComparable, Reason}. Solo la
  Instalación configurada/asignada activa escribe. La resolución se vincula al tipo y dimensiones
  persistidos; modificaciones de esos datos la invalidan. Sin clasificación de origen no hay
  equivalencia automática. NotComparable registra revisión, conserva PendingReview y no calcula
  diferencias. Comparable usa decimal, abs(delta*1000) y comparación estricta >100, sin redondeos
  de fabricación. Las adiciones sin referencia comercial no admiten Comparable.
- `RecordModification` recibe Modification {Type: Dimensional/System/Typology/Configuration/
  Structural/Other, Reason (2000), Description (4000), SketchVersionId}. Cada guardado crea un
  VersionId; AssessmentChangeJson conserva antes/después. El ID de croquis debe seleccionarse
  explícitamente de la estructura actual. La evidencia queda vinculada a dimensiones, sistema,
  ubicación, tipo y ficha técnica. Un croquis existente no completa por sí solo la modificación.
- Drawing v2 conserva Strokes y añade Annotations [{Id,Content,X,Y,Size,Color,Order}], máximo 50,
  contenido no vacío hasta 300 caracteres sin controles, coordenadas [0,1], tamaño 12–72 y colores
  permitidos del editor. IDs y orden únicos. V1 continúa leyendo y reintentando sin cambiar su
  fingerprint. Los textos se dibujan sobre la capa de trazos; el borrador de trazos no elimina textos.
- `GET /api/projects/{id}/measurements/submissions/{submissionId}/report` devuelve application/pdf,
  attachment, no-store, nosniff. Lectura scope-filtered para Installation/Management; no claves físicas.
  Un snapshot anterior sin reporte devuelve 404. El Frontend ofrece descarga solo si Snapshot.Report existe.
- El render usa SkiaSharp 3.119.2 (MIT, https://github.com/mono/SkiaSharp) y Noto Sans embebida (OFL,
  licencia incluida). Evita navegador, capturas de tarjetas y servicios externos. Los assets nativos
  Windows/macOS llegan con SkiaSharp; un despliegue Linux requiere el paquete nativo Linux compatible.
- Composición A3 horizontal, hasta tres filas con croquis / imagen cotizada / ficha técnica.
  Se planifican líneas y páginas antes del render: fuente fija, ajuste de texto y continuaciones
  con identidad e imágenes repetidas; pie con página/total. El orden es el del snapshot.
  Identidad visual de la maqueta aprobada: logo corporativo embebido, encabezado con bloque de
  proyecto, franja oscura, tarjetas redondeadas y ficha de etiquetas/valores. Observaciones a ancho
  de columna; estados pendientes explícitos, identificadores en texto secundario.
  No hay portada independiente ni regeneración de PDFs históricos.
- Las imágenes se resuelven por CommercialQuotationItemId de la cotización del checklist,
  validando proyecto, catálogo, versión presupuestal, Section/SourceRow y clave histórica exacta.
  Una consulta obtiene los vínculos y otra las extracciones; cada ítem se lee una vez aunque tenga
  varias unidades. Los bytes se incorporan al PDF, sin URLs ni nueva extracción XLSX. La composición usa los valores predeterminados de Skia para codificación sin pérdida; conserva dimensiones, proporción y alfa de PNG/WEBP sin convertirlos a JPEG. Los PDFs ya radicados no se regeneran.
  Archivo histórico ausente: indicación explícita. Vínculo incorrecto, bytes corruptos o límites
  excedidos: falla la preparación sin cerrar la tarea. Límite por imagen: 16 MiB / 24 millones
  de píxeles; imágenes comerciales acumuladas: 128 MiB. Decodificación en memoria, sin assets públicos.
- Antes de abrir la transacción de escritura, se construye y renderiza un snapshot propuesto usando
  versiones inmutables de WEBP verificadas por SHA256. Bajo los locks se revalida revisión, documentos
  y proyección exacta. Solo entonces se confirma ese mismo snapshot, MeasurementReport (PDF bytea,
  versión, SubmissionId, copia canónica del snapshot, SHA256 de ambos), evento, tarea y notificaciones.
  No se renderiza bajo lock ni se coordina un archivo PDF externo con PostgreSQL. Fallo de render,
  revisión, SaveChanges o rollback no deja una radicación sin PDF. Replay devuelve la misma operación.
  La descarga verifica hashes y equivalencia del snapshot JSONB con su copia canónica; devuelve los
  mismos bytes históricos, sin consultar dibujos mutables.
- Migraciones aditivas: 20261104213553_ExpandInstallationSiteDetails,
  20261104214213_AddInstallationAssessments y 20261104214806_AddInstallationSubmissionReports.
  Campos nuevos nullable, sin backfill. Validación solo en PostgreSQL temporal aislado; no aplicar
  a bases operativas sin autorización específica. Copias previas de snapshots permanecen intactas.


## Ingeniería: persistencia y operación

`EngineeringChecklist`, `EngineeringStructure`, `EngineeringEvent` y `EngineeringActa` conservan la radicación de origen, posición e identidad física, checks, actor/UTC, revisión, claves idempotentes, versiones PDF y snapshot final. `PrepareOpeningRecord = 7`, `OperationalArea.Engineering = 5`; los valores previos no cambian. La tarea comienza sin SLA ni ronda. El Google Subject se configura exclusivamente en User Secrets o entorno mediante `Authentication:EngineeringAssigneeSubject`; habilita membresía Engineering, pero cada escritura requiere también rol activo y asignación persistida.

Migración: `AddEngineeringOpeningRecords`. Añade cuatro tablas y la clasificación `ProjectDocument.IsEngineeringDocument`, índices únicos y restricciones de estado. No modifica radicaciones ni genera tareas históricas. Aplicar mediante despliegue autorizado; nunca automáticamente al iniciar la API. Las pruebas usan PostgreSQL temporal aislado.

Rutas bajo `/api/projects/{id}/engineering`:
- `GET`: estado y fichas congeladas con lectura limitada al proyecto.
- `PUT /structures/{structureId}/check`: `requestKey`, `expectedRevision` de la estructura, `completed`.
- `POST /acta?requestKey=...&expectedRevision=...`: multipart del protocolo documental existente, un PDF y referencia de reemplazo vigente.
- `GET /acta/{documentId}/download`: bytes históricos autenticados, tamaño/hash verificados, attachment y no-store.
- `GET /structures/{structureId}/sketches/{versionId}/image`: solo versiones vinculadas al snapshot.
- `POST /complete`: `requestKey`, revisión del checklist y `documentId` vigente; exige checks completos y bytes íntegros.
- `POST /initialize-historical`: incorporación explícita descrita abajo.

Las escrituras bloquean proyecto y tareas en ese orden. Los checks usan revisión individual; reemplazos y cierre usan revisión global. Un replay exige misma operación y actor. Las restricciones únicas protegen tarea por proyecto, radicación, posiciones, versiones y eventos idempotentes. El progreso se deriva del estado persistido; el último check no cierra automáticamente. Al cerrar se conserva el snapshot final, se concilian las notificaciones existentes y se crea atómicamente la primera ronda comercial del Acta.

Los PDFs se almacenan mediante ProjectDocument y el almacenamiento protegido existente. Se valida nombre, extensión, límite, cabecera, cierre y referencia xref; es filtrado estructural básico, no antivirus ni validación exhaustiva de todos los objetos PDF. Las descargas son adjuntos privados. Un rollback conocido elimina solo el archivo nuevo; un resultado de commit incierto conserva sus bytes. Los documentos de Ingeniería quedan fuera de cargas/ZIPs/reemplazos genéricos.

### Incorporación explícita de radicaciones históricas

No ejecutar sobre una base operativa sin autorización específica. Antes de actuar, verificar proyecto, radicación definitiva y ausencia de Ingeniería; conservar los identificadores originales. Con identidad configurada y rol activo Engineering, enviar `POST /api/projects/{id}/engineering/initialize-historical` con `{ "submissionId": "<radicacion-existente>", "reason": "<motivo-autorizado>" }`. El servidor exige tarea de Instalación completada, snapshot y PDF persistidos con hashes coherentes; crea únicamente el trabajo de Ingeniería y registra el motivo. Una repetición de la misma radicación no duplica la tarea; otra radicación es conflicto. No reconstruye PDFs ausentes ni cambia datos históricos. Verificar luego GET de Ingeniería y bandeja del responsable. No usar un backfill automático ni enviar solicitudes por lotes sobre SG1003/SG1004.


## Rondas de presentación del Acta de Vanos

`OpeningRecordRound` conserva radicación/checklist original, número consecutivo, tarea comercial, versión presentada, timestamps/actores de presentación y respuesta, tarea/documento de revisión, PDF firmado y tarea final de radicación. Estados: Ready → Presented → ChangesRequested → Revised (crea siguiente Ready), o Presented → Accepted. `OpeningRecordStructure` separa aprobación, documento que la sustenta, solicitud individual, declaración de afectación y check de revisión. `OpeningRecordDocument` distingue Revised y Signed; reutiliza ProjectDocument/almacenamiento protegido, sin reemplazar documentos de otros tipos ni modificar la versión inicial. `OpeningRecordEvent` registra comandos, valores anteriores/nuevos, tarea, actor, UTC y clave/fingerprint idempotente.

Tipos nuevos estables: `PresentOpeningRecord=8` (Commercial), `ReviewOpeningRecord=9` y `FileOpeningRecord=10` (Engineering). No usan QuotationRound ni SLA. Presentación y revisión comienzan al asignarse; el contador del cliente solo nace de PresentedAt. FileOpeningRecord nace Pending; su ejecucion incremental utiliza la API opening-filings descrita abajo. Las bandejas incorporan los tipos según rol activo y Subject asignado. El asesor es `Project.CreatedByGoogleSubject`, con membresía CommercialAdvisor explícita; nunca se inventa identidad histórica. Ingeniería utiliza Authentication:EngineeringAssigneeSubject.

Migración `AddOpeningRecordRounds`: cuatro tablas, claves foráneas restrictivas, unicidad de ronda por proyecto/número, una ronda activa, una aceptada, relaciones únicas de tareas, versiones por proyecto/tipo y claves/revisiones de auditoría. No hay backfill. Probar/aplicar solo en PostgreSQL aislado durante desarrollo; despliegue operativo requiere autorización independiente.

Rutas bajo `/api/projects/{id}/opening-records`:

- GET: estructuras de la radicación, rondas ordenadas, documentos privados/metadatos, permisos y tiempo de respuesta calculado desde timestamps persistidos; ServerNow permite refresco visual sin tomar eventos del navegador.
- POST `/rounds/{roundId}/commands`: requestKey, expectedRevision y action en Present, ConfirmStructure, RequestChanges, DeclareAffected, ReviewCheck, CloseReview o Accept. Los checks usan revisión individual; el resto, revisión global. No existe Stop.
- POST `/rounds/{roundId}/documents?requestKey=...&expectedRevision=...&kind=Signed|Revised`: multipart documental existente, un PDF hasta 25 MiB, reemplazo exclusivamente de su tipo y ronda. Revised exige declaración previa y congela los IDs afectados con el upload; CloseReview rechaza una declaración diferente hasta cargar una versión asociada.
- GET `/documents/{documentId}/download`: autorización por proyecto, verificación de hash/tamaño, attachment, no-store, sin URLs públicas.
- POST `/initialize-historical`: checklistId definitivo y reason obligatorio; solo Ingeniería asignada/configurada. No se invoca automáticamente desde UI.

Las transiciones bloquean proyecto y tareas en el orden existente. Se comprueba rol, Subject y asignación antes del replay; misma clave con distinto contenido o actor produce conflicto. La liberación del índice de ronda activa y la inserción de su sucesora ocurren dentro de una misma transacción. Cierre inicial, respuesta, nueva ronda y aceptación persisten conjuntamente tareas/notificaciones. Los reintentos no crean trabajo duplicado. Los PDFs conservan bytes inmutables; rollback conocido elimina únicamente el archivo nuevo y commit incierto conserva sus bytes.

ConfirmStructure permite deshacer solo confirmaciones de la ronda actual, antes de responder; una aprobación heredada no se elimina arbitrariamente. RequestChanges acepta únicamente estructuras pendientes y confirma las restantes mediante la respuesta explícita. DeclareAffected incluye obligatoriamente las solicitadas y permite otras; el nuevo PDF no conserva automáticamente una aprobación afectada. Accepted requiere todas Confirmed, documento Signed de la misma ronda y `attestSignedForPresentedVersion=true`. Esa atestación humana queda en auditoría/snapshot, sin afirmar validación electrónica de firma.

### Habilitación controlada de Ingeniería histórica cerrada

No ejecutar sobre datos operativos sin autorización. Identificar el proyecto, checklist inicial de Ingeniería Completed, snapshot final y Acta definitiva; verificar la configuración del asesor responsable. Con Ingeniería activa/configurada/asignada, enviar POST `/api/projects/{id}/opening-records/initialize-historical` con `{ "checklistId": "<id-existente>", "reason": "<motivo-autorizado>" }`. El servidor comprueba fuente cerrada y bytes íntegros, conserva sus registros originales y crea exclusivamente la primera ronda/tarea/notificación. Repetir no duplica; no se crean tareas por migración. Revisar después GET y la bandeja del asesor. No ejecutar lotes sobre proyectos reales ni reconstruir documentos ausentes.

## Base persistente incremental por tramos

### Modelo y límites de activación

`20261110154907_AddIncrementalFlowFoundation` es aditiva. Su recurso SQL versionado
`20261110152041_IncrementalFoundation.sql` forma parte de la migración, no es un script
para ejecutar manualmente en producción. No modifica los bytes de documentos, croquis,
snapshots anteriores, aprobaciones, tareas ni notificaciones históricas.

- `MeasurementSubmission` reutiliza el SubmissionId histórico; Number es único por proyecto.
  `MeasurementSubmissionMember` referencia el StructureId físico original, versión de evidencia,
  posición y snapshot/hash. La primera evidencia de una estructura solo pertenece a una entrega.
- `MeasurementAmendment` conserva entrega, versión >= 2, motivo, actor, fecha y clave/fingerprint.
  Sus miembros son nuevas versiones de estructuras de esa misma entrega. No existe API ni
  procedimiento de autorización de enmiendas en esta entrega.
- `SubmissionReport` referencia el reporte histórico mediante ChecklistId sin copiar/regenerar
  sus bytes. Los reportes nativos almacenan bytes, snapshot canónico y hashes propios por entrega
  y versión. Los documentos asociados tienen declaración y membresía explícita por evidencia.
- `EngineeringWorkspace` permanece abierto por proyecto; `EngineeringReceipt` registra cada
  radicación recibida. Los checks se identifican por estructura y versión de evidencia.
  Los checks del flujo vigente se proyectan desde su fila histórica, conservando revisión y actor.
- `WorkPackageAssignment` guarda PackageId, área, Google Subject y fechas. EndedAt nulo
  representa asignación activa; existe una sola asignación activa por paquete y área.
  LegacyTaskId conserva la responsabilidad actual; no se introduce administración de personal.
- `OpeningRecord` es la identidad lógica del Acta. Sus miembros pueden provenir de distintas
  radicaciones. `ActiveOpeningRecordMember` es la reserva activa: PK ProjectId + StructureId
  impide que dos Actas reserven la misma estructura. Una membresía sin reserva no constituye
  activación. Los futuros consumidores deben reservar antes de activar un Acta.
- `OpeningRoundLink` tiene identidad propia y numeración por Acta; LegacyRoundId opcional
  vincula la ronda anterior sin cambiar su ID. Las nuevas identidades no crean rondas operativas.
- `OpeningDocumentScope/Member` conserva documento, clase, versión, hash y alcance.
  No interpreta el contenido ni valida criptográficamente firmas.
- `OpeningAcceptance/Member` identifica cada aceptación explícita y su subconjunto, documentos
  presentado/firmado y ronda. La misma estructura no puede aceptarse dos veces en una misma Acta.
  Una aceptación nativa no puede quedar vacía; sus miembros deben pertenecer a ambos alcances.
- `OpeningFilingDelivery` enlaza una aceptación con una tarea propia. ProjectTask incorpora
  IncrementalAcceptanceId nullable, único y con FK del mismo proyecto. El índice
  UX_ProjectTasks_FileOpeningRecord sigue protegiendo el circuito legado (Type=10 y vínculo nulo).
  Las tareas nativas requieren aceptación y entrega explícitas; ningún endpoint actual usa
  la fábrica `ForOpeningAcceptance`. La ejecucion incremental de Radicacion de Vanos se describe abajo.

Se mantienen la unicidad de los checklists/tareas originales y los índices de rondas
activas/aceptadas por proyecto. La recepción parcial se habilita exclusivamente mediante el modo descrito abajo.
La base de evidencia se consume ahora mediante las APIs de Ingenieria y Comercial incremental descritas al final.
La confirmación implícita de no observadas del circuito anterior no se reinterpreta mediante
la migración; la base nueva exige membresía explícita de cada aceptación.

### Clasificación histórica y evidencia

La clasificación se realiza una vez desde el snapshot congelado. Nunca usa valores actuales
de medidas para reconstruir evidencia; solo consulta identidades y pertenencia al proyecto
para validar referencias.

| Campo | Significado |
|---|---|
| EvidenceStatus=COMPLETE | Alcance e integrantes reconstruibles inequívocamente |
| EvidenceStatus=LEGACY_INCOMPLETE_EVIDENCE | Alcance insuficiente o incoherente; no se crean integrantes inferidos |
| ReportAvailability=ABSENT | No existe reporte histórico; es independiente de la integridad del alcance |
| ReportAvailability=PRESENT | Existe reporte histórico, conservado por referencia |
| ScopeIssue | Motivo determinista: miembros ausentes/malformados/duplicados, alcance vacío, referencias no resolubles o discrepancia de integridad |

Sin PDF y con alcance verificable puede ser COMPLETE. Con PDF y sin miembros puede ser
LEGACY_INCOMPLETE_EVIDENCE. Las firmas, Actas, rondas y tareas anteriores se conservan incluso
en el segundo caso, sin inventar membresías. El origen sigue consultándose mediante los
endpoints históricos y sus permisos actuales; no se agrega una ruta pública de diagnóstico.

Cuando existe reporte, se conserva SnapshotCanonical exacto. Sin reporte, se conserva la
representación del JSONB ya almacenado; no se afirma recuperar el texto previo a PostgreSQL.
Se mantienen separados el hash calculado de esa evidencia y los hashes originales del reporte.
No hay promoción automática a COMPLETE ni conciliación manual implementada.

### Transacciones, integridad y compatibilidad

`IncrementalLegacyProjection.SyncAsync` ejecuta `sng_incremental_legacy_project` después de
persistir cambios legacy y antes del commit, bajo los locks y la autorización existentes.
Solo materializa asociaciones derivadas; no crea tareas/notificaciones ni escribe el historial
original. Un fallo revierte ambos modelos. Los reintentos conservan las identidades existentes.

Las FKs compuestas incluyen ProjectId y la evidencia precisa. Los índices únicos protegen
miembros, reservas activas, versiones, claves de reintento, aceptación y origen de tarea.
Los triggers impiden actualizar/eliminar evidencia congelada y ampliar después el alcance
de una entrega, documento o aceptación. Cinco tablas raíz tienen la columna técnica SQL
CreationTransactionId (no expuesta en el modelo HTTP), generada con pg_current_xact_id():
sirve para permitir inserciones atómicas incluso con savepoints EF; no es fecha, actor ni
evidencia de negocio. Los futuros consumidores deben guardar raíces e integrantes dentro
de una misma transacción explícita.

La migración no genera procesos posteriores. Down solo permite revertir proyecciones
derivadas: si hay evidencia incremental nativa o enmiendas, rechaza el downgrade y exige
una migración hacia delante. No se debe usar Down para borrar trabajo operativo.

### Verificación y despliegue

`IncrementalFoundationTests` verifica base vacía y actualización desde la migración anterior,
proyecto sin radicación, radicación sin PDF válida, Ingeniería abierta/cerrada, varias rondas,
aceptación firmada, tarea de radicación pendiente y legado incompleto con/sin PDF.
Compara filas históricas, bytes, hashes, snapshots, fechas, estados e identidades antes/después.
También verifica tramos mezclados, aceptaciones parciales, dos tareas, enmiendas, restricciones
entre proyectos, asignaciones, inmutabilidad, reintentos y rollback.

Las pruebas PostgreSQL requieren `SNG_VERIFICATION_TEST_DATABASE` apuntando exclusivamente
a localhost y database `verification_test`, con datos sintéticos. Las pruebas de migración
crean esquemas aislados. No requieren Google, backend remoto ni documentos privados.

Aplicar en una base operativa requiere autorización independiente. Antes de activar módulos
incrementales futuros, implementar sus permisos/asignaciones, comandos, reservas activas,
contratos y UI; no reutilizar las lecturas singulares legacy como si fueran colecciones.


## Instalacion incremental y recepcion de Ingenieria (Entrega 2)

`EnableIncrementalMeasurements` agrega solamente `MeasurementChecklist.IsIncremental`
(default false) y `EngineeringCheckEvents`. Reutiliza las entidades de Entrega 1. No modifica
las migraciones publicadas ni aplica cambios automaticamente. Los triggers nuevos congelan
estructuras/croquis enviados, protegen el modo del checklist y la auditoria, impiden crear
EngineeringChecklist global para proyectos incrementales y exigen PDF/recepcion/checks atomicos.
Down rechaza cualquier workspace incremental o auditoria nativa; se requiere migracion hacia delante.

### Activacion y expedientes en curso

1. Desplegar migracion, Backend y Frontend compatibles antes de habilitar nuevos proyectos.
2. Configurar en entorno/User Secrets `Measurements:IncrementalNewProjectsFromUtc` con un
   corte UTC explicito terminado en Z, acordado FUTURO al primer despliegue. No retrocederlo
   ni usar fechas anteriores para incorporar proyectos existentes. Ausencia = flujo tradicional.
3. Al inicializar medidas, solo Project.CreatedAt >= corte crea IsIncremental=true. El valor
   queda persistido e inmutable. Checklists existentes siempre conservan false y sus tareas;
   no se reabren expedientes, ni se cambia el modo al reiniciar o retirar la configuracion.
4. Exigir tambien Authentication:EngineeringAssigneeSubject configurado. Cada escritura
   requiere rol activo, Subject configurado y tarea asignada; checks exigen ademas paquete.

La habilitacion operativa requiere autorizacion separada. Esta entrega se prueba solo en
PostgreSQL temporal; no habilita el corte ni migra bases operativas.

### Contrato y consistencia

POST `/api/projects/{id}/measurements/submissions` devuelve 201 con la entrega original tanto
al confirmar como al repetir exactamente la solicitud. Body: requestKey, expectedRevision,
structureIds (1..500 IDs fisicos unicos), documents (0..15 entradas de documentId + structureIds
cubiertos, no vacios, contenidos en la seleccion). DocumentId identifica la version inmutable;
Declaration conserva filename/hash/actor/UTC, y SubmissionDocumentMembers conserva el alcance.
No requiere todos los documentos ni los duplica. Sustituir un archivo no cambia entregas previas.

El fingerprint normaliza seleccion y alcance documental, incluye expectedRevision y versiones
identificadas por sus IDs. Misma clave con otro contenido/actor => 409. Proyecto/tareas se bloquean
antes de releer revision, estructuras y documentos y preparar PDF. El render se hace bajo ese
lock: serializa tambien tramos disjuntos para impedir cambios durante su preparacion. Un segundo
comando con revision antigua requiere refresco y nueva confirmacion, nunca reintento automatico.
Esto aumenta el tiempo de retencion del lock por proyecto; medirlo en smoke antes de activar.

La misma transaccion guarda entrega, miembros, report, manifiesto, auditoria de Instalacion,
recepcion, checks y asignaciones. En el primer tramo crea una sola tarea PrepareOpeningRecord
sin SLA e inicia EngineeringWorkspace. TakeMeasurements permanece InProgress, sin SubmissionId
global. WorkPackageAssignment de Installation queda cerrado por entrega; Engineering queda activo.
El fallo de cualquiera revierte todo. Reintentos inciertos conservan clave y PDF original.

GET measurements agrega isIncremental y deliveries (orden Number), incluidos legados completos
/incompletos y disponibilidad independiente del PDF. Las estructuras conservan sus IDs.
GET `measurements/submissions/{submissionId}/report` devuelve bytes guardados, verifica hashes,
requiere acceso de Installation/Management/Engineering y usa nombre saneado
`Radicacion de medidas-[proyecto]-Tramo 01.pdf` para entregas nativas. A3 horizontal y tres
estructuras por pagina se mantienen; el PDF contiene exclusivamente las fuentes seleccionadas.

GET engineering en modo incremental devuelve isIncremental=true, checklistId/submissionId=null,
deliveries y origin por fila (submissionId/number/evidenceVersion). Orden: tramo, posicion congelada.
PUT `engineering/structures/{structureId}/check` reutiliza el contrato existente; revision individual,
clave idempotente, actor/UTC y evento append-only. Agregar tramos no toca checks anteriores.
`complete`, `acta` e `initialize-historical` rechazan proyectos incrementales. El flujo comercial incremental utiliza su API separada, descrita en la seccion siguiente.
El circuito historico y su proyeccion de compatibilidad permanecen independientes y operativos.

Pruebas: IncrementalMeasurementPostgresTests cubre 10 -> 3 -> 4, PDF 1/2 paginas, reintento
concurrente, revisiones, freeze, nueva estructura, archivo compartido, checks y bloqueos legacy.
IncrementalFoundationTests compara columnas historicas originales y bytes con/sin evidencia;
IsIncremental se comprueba false por separado. Los tests HTTP cubren rol/Subject y los de contrato
cubren limites y corte. No usar datos reales para smoke ni activar proyectos historicos.


## Ingenieria incremental y multiples Actas (Entrega 3)

La recepcion acumulativa conserva StructureId, SubmissionId, EvidenceVersion, orden original, snapshots,
PDFs de Instalacion y checks previos. No crea EngineeringChecklist global para proyectos incrementales.
`EngineeringWorkspace.Revision` controla creaciones; cada paquete, Acta y check tiene revision propia.
Todas las escrituras toman primero el lock del proyecto y despues tareas, para serializar nuevas
recepciones, asignaciones, reservas, checks, documentos y confirmaciones sin reemplazar evidencia.

### Responsables y paquetes

`Authentication:EngineeringAssigneeSubject` mantiene su significado historico y actua como coordinador.
La lista opcional server-only `Authentication:EngineeringWorkerSubjects` permite hasta 100 Subjects
adicionales distintos, no vacios, recortados y de hasta 200 caracteres. Todos reciben membresia Engineering
a traves de auth/me; el rol activo y las asignaciones persistidas siguen siendo obligatorios.
No hay descubrimiento de empleados, usuarios sinteticos operativos ni configuracion publica de Subjects.
El coordinador asigna/reasigna paquetes a esas identidades. Un trabajador opera solo sus estructuras.

`EngineeringPackage` referencia la asignacion vigente; `EngineeringAssignedStructure` es la pertenencia
efectiva unica por proyecto/estructura. `WorkPackageAssignment/Member` conserva todos los integrantes y
responsables historicos. Las recepciones forman paquetes de entrada; una seleccion explicita transfiere
sus estructuras a un paquete manual, incluso entre tramos. Un paquete manual no puede ser absorbido
por otra seleccion; la reasignacion explicita cambia el responsable del paquete completo. Paquetes
finalizados o con estructuras reservadas en Actas no se reasignan. Una entrada totalmente transferida
queda Transferred, sin borrar su historial.

`EngineeringPackage=11` es un nuevo tipo estable de tarea Engineering sin QuotationRound ni SLA.
Una asignacion manual crea tarea y notificacion; la reasignacion cierra la tarea anterior y crea otra,
conservando ambos registros y el evento explicito. Las bandejas y permisos de lectura reutilizan tareas.
El expediente historico permanece consultable por responsables anteriores; la escritura requiere la
asignacion efectiva actual. PrepareOpeningRecord global permanece abierto para nuevas recepciones.

### Actas y documentos

`OpeningRecord/Member` conserva identidad logica, numero por proyecto y evidencia exacta. La reserva
`ActiveOpeningRecordMember` tiene unicidad proyecto/estructura; tambien se conserva despues de confirmar,
hasta que una futura entrega defina su ciclo comercial. No permite duplicar Actas iniciales.
`EngineeringOpeningState` almacena responsable, revision, PDF vigente y confirmacion canonica con hash.
Draft/InProgress/Ready son proyecciones oficiales segun checks y cobertura PDF; Confirmed es persistido.

El manifiesto admite ampliacion explicita antes de confirmar, sin quitar miembros existentes; la UI
ordena siempre por origen aunque las posiciones originales ya persistidas no se renumeran. Agregar
miembros invalida la cobertura del PDF anterior. No se crean enmiendas ni se infiere evidencia vigente
a partir de estructuras mutables: las operaciones iniciales exigen evidencia recibida version 1.

Cada PDF (hasta 25 MiB) reutiliza ProjectDocument privado, validacion estructural y SHA-256. OpeningDocumentScope
conserva version/declaracion y OpeningDocumentMember su alcance inmutable. `scopeHash` identifica exactamente
el manifiesto declarado por el usuario; no valida semanticamente el contenido PDF. Los bytes de versiones
anteriores no se reemplazan. Solo se puede seleccionar una version cuyo alcance coincida con el manifiesto.
Rollback conocido elimina solo el archivo nuevo; commit incierto conserva bytes para conciliacion segura.

Confirmar exige todas las estructuras trabajadas, asignacion vigente y PDF integro con cobertura exacta.
Congela miembros, checks, documento, actor/UTC y snapshot canonico; cierra los paquetes y tareas cuyos
integrantes efectivos ya estan todos confirmados, sin cerrar globalmente el proyecto. El estado funcional
es "Confirmada por Ingenieria". La confirmacion crea atomicamente una tarea comercial y primera ronda
por Acta, con asesor configurado. Los bloqueos del consumidor historico se conservan.

### Contratos y persistencia

GET `/api/projects/{id}/engineering` agrega `workspace` solo en modo incremental: proyecto/SG, revision,
capacidades, identidades asignables solo para coordinador, paquetes, Actas y auditoria. Cada fila agrega
`work` con paquete, responsable, permiso efectivo, actor del check y Acta asociada.

- POST `/engineering/workspace/commands`: {requestKey, expectedRevision, action, targetId, subject, structures, documentId}.
  Actions cerradas: CreatePackage, ReassignPackage, CreateActa, AddStructures, SelectDocument, ConfirmActa.
  Estructuras explicitas: {structureId, submissionId, evidenceVersion}; 1..500 sin duplicados.
- POST `/engineering/opening-records/{actaId}/documents?requestKey&expectedRevision&scopeHash`: multipart existente.
- GET `/engineering/opening-records/{actaId}/documents/{documentId}/download`: permiso por proyecto y pertenencia exacta.

`EngineeringWorkspaceEvent` conserva actor, comando canonico, fingerprint, clave y resultado. Repetir la
misma operacion devuelve el resultado original; reutilizar clave con actor/cuerpo distintos es conflicto.
409 no reintenta escrituras. La autorizacion se vuelve a evaluar al operar; el servidor es la autoridad.

La migracion aditiva `EnableIncrementalEngineering` agrega cuatro tablas, revision del workspace y
restricciones. Solo proyecta paquetes de entrada/asignaciones de checklists IsIncremental=true; no toca
registros historicos, LEGACY_INCOMPLETE_EVIDENCE, documentos ni aprobaciones. Triggers impiden mutar
confirmaciones, miembros/documentos confirmados, checks congelados, reservas de asignacion y auditoria.
Down rechaza cualquier operacion/Acta nativa o paquete manual; no destruye trabajo. No se modificaron
migraciones publicadas. Validar y desplegar coordinadamente con Frontend; no aplicar a la base operativa
sin la autorizacion de despliegue correspondiente.

Pruebas: PostgreSQL temporal incluye dos trabajadores, paquetes/reasignacion, Acta entre dos tramos,
dos Actas, reserva concurrente, conflicto de revision, PDFs/versiones/cobertura/seleccion, confirmacion
repetida, integridad SQL y continuidad. Las regresiones historicas conservan IDs/snapshots/PDFs y el
bloqueo del consumidor comercial antiguo. No se modifica la auditoria gerencial ni Intelligence.

## Comercial incremental y aceptaciones parciales (Entrega 4)

Cada Acta confirmada recibe su propia tarea PresentOpeningRecord y secuencia de rondas. El asesor
es el creador del proyecto, con membresia CommercialAdvisor configurada; si falta se rechaza el cierre.
Para Actas confirmadas antes de esta entrega, Receive es explicito, autorizado e idempotente; la
migracion y los GET no inventan tareas ni identidades. No se cierran globalmente areas ni proyecto.

CommercialOpeningRound/Row guardan PDF presentado, evidencia por estructura, estado y revision.
Present registra UTC en servidor. Las respuestas conservan primera respuesta, eventos individuales,
tiempo transcurrido y resolucion completa, sin SLA. RequestChanges solo toca seleccionadas; omitidas
siguen pendientes. Accept une confirmacion y liberacion documentada: PDF firmado persistido de la
misma Acta/ronda, cobertura y evidencia exactas, seleccion explicita y atestacion humana obligatoria.
No hay verificacion criptografica de firma. Cada OpeningAcceptance conserva miembros/evidencias y
crea exactamente una OpeningFilingDelivery + FileOpeningRecord Pending, sin inicio ni plazo. Un PDF
puede reutilizarse para subconjuntos disjuntos sin copiar bytes. No se implementa ejecutar Radicacion.

Una revision Engineering por Acta agrupa solicitudes sucesivas, preservando todos los motivos en
CommercialOpeningEvent. Cada nueva solicitud invalida declaracion/PDF revisado y reinicia solo checks
de estructuras nuevamente solicitadas. Comercial puede responder sobre otras pendientes. Engineering
guarda checks propios, declara las realmente cambiadas (lista vacia permitida), sube un PDF completo
y cierra explicitamente. Se crea ronda siguiente; solo evidencia declarada cambiada incrementa version.
Las aprobaciones no afectadas y su documento fuente permanecen; nunca respondidas siguen pendientes.
Datos radicados de Instalacion siguen inmutables: el PDF revisado es la evidencia tecnica nueva.
Una estructura ya liberada mediante aceptacion no admite revisiones ni otra liberacion; no se inventa
revocacion de aceptaciones o tareas. Documentos anteriores y razones permanecen consultables.

Contratos bajo `/api/projects/{id}/incremental-opening-records`:
- GET raiz: Actas, rondas, estructuras, revisiones, documentos, aceptaciones, eventos y tiempos oficiales.
- POST `{actaId}/commands`: Receive, Present, RequestChanges, Accept, DeclareChanged, ReviewCheck, CloseReview.
- POST `{actaId}/rounds/{roundId}/documents`: multipart manifest JSON primero y un file PDF (25 MiB).
- GET `{actaId}/documents/{documentId}/download`: permiso de proyecto y pertenencia exacta, adjunto privado.

Comandos usan requestKey, expectedRevision y, para selecciones, structureId/evidenceVersion/expectedRevision
por estructura (1..500, sin duplicados; DeclareChanged permite cero). ReviewCheck usa revision del check;
DeclareChanged/CloseReview usan revision de revision Engineering. Present usa revision de ronda. Aceptar
o pedir cambios usa revision por estructura: respuestas disjuntas no se invalidan por actividad ajena.
PDF SIGNED fija miembros/version/documento fuente; REVISED fija cobertura completa y versiones futuras.
Se valida rol activo + Subject + asignacion antes de replay. Fingerprint incluye Acta, comando o metadata,
nombre/hash de archivo. Lock proyecto/tareas serializa cambios; 409 nunca reintenta automaticamente.
Los bytes nuevos se compensan solo ante rollback conocido; commit incierto los conserva.

Migracion aditiva `20261110192609_EnableIncrementalCommercialOpenings`: ocho tablas CommercialOpening*,
FKs compuestas de proyecto/Acta/ronda/documento/evidencia, unica ronda/revision activa por Acta,
concurrencia por revision, manifiestos/documentos/auditoria inmutables y validacion diferida de
aceptacion firmada. El ultimo conjunto aceptado se persiste antes de congelar la ronda, dentro de la
misma transaccion. Down rechaza rondas nativas. No cambia migraciones publicadas, PDFs ni legado
LEGACY_INCOMPLETE_EVIDENCE. El consumidor historico sigue separado con su comportamiento autorizado.

Verificacion: PostgreSQL temporal valida dos Actas, rondas independientes, omisiones pendientes,
PDF compartido, aceptaciones simultaneas, checks/agrupacion, nuevas versiones, aprobaciones conservadas,
pendientes entre rondas, resolucion, idempotencia y freeze SQL. La prueba de fundacion siembra el esquema
publicado anterior y comprueba aceptaciones intactas tras actualizar; las regresiones historicas tambien
conservan IDs/snapshots/PDFs/estados. HTTP valida rol activo, atestacion, limites y multipart cerrado.
Desplegar coordinadamente Backend + Frontend. No aplicar migraciones a la base operativa sin autorizacion.


## Validacion integral incremental (Entrega 5)

Veredicto de la validacion del 2026-11-10: APTO CON RESERVAS, no aptitud para produccion.
`IncrementalEndToEndTests` ejercita 10 estructuras, tramos 4+6, dos Actas, respuestas parciales,
revision/ronda nueva y cuatro tareas pendientes de radicacion mediante controladores HTTP reales,
PostgreSQL temporal y almacenamiento sintetico. Tambien se ejecuto por el BFF Next real.
Esta validacion historica no cambio la logica productiva; el expediente de Radicacion de Vanos se describe abajo.

El harness vive solo en el assembly de tests: vacia configuracion, usa identidad sintetica y reloj
controlado, sin Google ni servicios externos. Requiere SNG_VERIFICATION_TEST_DATABASE hacia una
base aislada verification_test en loopback y crea un esquema aleatorio. SNG_E2E_ARTIFACTS permite
exportar JSON/PDF sinteticos fuera de Git. SNG_E2E_BFF_URL opcional apunta al BFF aislado; al usarlo,
el servidor de prueba escucha en localhost:55486. Nunca usar una base o un BFF operativos.
La simulacion de membresia revocada modifica las opciones efectivas del test; no implica hot reload
productivo: ConfiguredRoleResolver usa IOptions y los cambios de membresia requieren reinicio.

Carreras HTTP: mismo check, confirmacion/presentacion/aceptacion repetidas, dos respuestas sobre
la misma fila y aceptacion comercial durante check de revision. Se complementan con las carreras
PostgreSQL existentes de tramos disjuntos, reservas, cargas, aceptacion frente a cambios y fingerprints.
Se ampliaron los seeds historicos con Acta presentada y revision pendiente, conservando filas,
IDs, tareas, documentos disponibles, hashes y snapshots tras aplicar la cadena completa.

Validacion: focalizadas 6/6; suite completa 1238 aprobadas, 11 opt-in omitidas; Release sin errores
ni advertencias; 28 migraciones verificadas, modelo EF sin cambios pendientes. Las cuatro finales
siguen siendo AddIncrementalFlowFoundation, EnableIncrementalMeasurements, EnableIncrementalEngineering
y 20261110192609_EnableIncrementalCommercialOpenings. Ninguna migracion fue modificada o creada.
Frontend: 1433 tests, lint/typecheck/build aprobados con Next 16.4.5; 53 pruebas focalizadas.

El informe detallado, matriz documental/concurrencia/autorizacion, avisos Next, omisiones y guia
manual estan en README_TECHNICAL.md del repositorio sng-control-frontend, seccion "Validacion
integral del flujo incremental - Entrega 5" (titulo equivalente con acentos).
Pendientes: smoke de navegador autenticado/responsive (host automatizado no disponible), Google
corporativo, despliegue/proxy/TLS, seguridad operacional y nueve avisos en dependencias frontend
de desarrollo. No hubo conexiones ni migraciones a la base operativa; no se probaron archivos privados.


## Radicacion de Vanos: expediente por aceptacion

Cada aceptacion incremental firmada conserva una sola `OpeningFilingDelivery` y su tarea
`FileOpeningRecord`. El expediente puede prepararse en varias sesiones, confirmarse y recibir
una enmienda versionada; confirmar una entrega no cierra el proyecto ni otras tareas.

El Jefe configurado en `Authentication:EngineeringAssigneeSubject` crea cada Acta indicando
explicitamente un Subject del equipo y puede reasignarla mientras no este confirmada. La
asignacion registra actor, fecha, responsable anterior y nuevo. Dentro del Acta, su responsable
revisa exclusivamente sus integrantes, aunque procedan de paquetes ajenos. Fuera de ella se
conserva la autorizacion por paquete. Agregar nuevos integrantes sigue exigiendo acceso al
paquete de origen. La aceptacion hereda el responsable acreditado del Acta para la radicacion.
No hay reasignacion automatica de historicos ni modificaciones ordinarias de Actas confirmadas.

Las siete categorias obligatorias (Lista de Estructuras, Secciones, Planos, Lista de Corte No
Optimizada, Vidrios, Materiales, Modulacion de Vidrios) admiten varios PDF cada una. Adicionales
es opcional y tambien admite varios PDF. En esta entrega la allowlist es exclusivamente PDF:
los validadores XLSX de cotizacion no son validadores generales de anexos y no se habilitan
XLS/XLSX/CSV sin una validacion documental apropiada. La inspeccion PDF existente comprueba
formato, encabezado y xref; no certifica contenido tecnico, firmas ni ausencia de malware.
Los archivos privados se descargan como adjuntos, con hash y tamano verificados.

El numero de radicado se ingresa manualmente, conserva original y normalizado y es unico
**dentro del proyecto**, incluidos expedientes confirmados. El OP conserva ambos valores,
se asocia al ID fisico permanente y es unico por version del expediente. Normalizacion:
ASCII acotado a 80 caracteres, trim, eliminacion de espacios y uppercase; no se extraen
ni inventan codigos. La confirmacion exige todos los integrantes exactos de la aceptacion,
OP confirmados y al menos un PDF vigente por categoria obligatoria.

Carga, sustitucion y retiro se auditan sin borrar bytes historicos. Una enmienda exige motivo,
conserva identidad y numero, clona referencias a los archivos vigentes sin copiar bytes y
crea otra version. La entrega original permanece inmutable y descargable.

Los expedientes historicos sin evidencia completa conservan su consulta y bloqueo
`LEGACY_INCOMPLETE_EVIDENCE`; no hay backfill, PDFs regenerados ni radicaciones ficticias.


### Persistencia y contrato de Radicacion de Vanos

Migracion nueva: `20261111211910_AddOpeningFilingExecution`. No modifica migraciones publicadas.
`OpeningFilingWorkspaces` extiende la identidad existente; `OpeningFilingVersions`,
`OpeningFilingMembers`, `OpeningFilingDocuments` y `OpeningFilingEvents` conservan versiones,
OP, asociaciones privadas con ProjectDocuments y auditoria/idempotencia. `OpeningFilingReleases`
guarda una entrega inmutable por version confirmada para **Production y Logistics** simultaneamente,
sin tareas productivas, despacho externo ni simulacion de esos modulos. Su manifiesto incluye
proyecto, filing, aceptacion, documentos fuente, miembros/OP, versiones, hashes, responsable y UTC.
Los consumidores futuros deben fijar VersionId; una enmienda no reemplaza su entrega original.

API bajo `/api/projects/{id}/opening-filings`:
- GET: todos los expedientes e historiales autorizados del proyecto.
- POST `{filingId}/commands`: Save, Withdraw, Confirm, Amend, con RequestKey y ExpectedRevision.
- POST `{filingId}/documents`: multipart cerrado (manifest JSON y un PDF por request); multiples
  archivos de la UI se transmiten progresivamente sin limite funcional por tarjeta.
- GET `{filingId}/versions/{versionId}/documents/{documentId}/download`: descarga privada versionada.

Los writes validan rol activo en Application y Subject asignado, proyecto, tarea, Acta y aceptacion
en persistencia. Se bloquea proyecto antes de tareas; revision, request key/fingerprint y uniques
PostgreSQL protegen carreras. Confirmacion, finalizacion de la unica tarea, notificaciones y
release se confirman en la misma transaccion. SQL protege miembros exactos, version congelada,
numero confirmado, eventos y documentos historicos. El filesystem usa compensacion solo si
el rollback es conocido; ante resultado incierto del commit conserva archivos para el replay.

Limites tecnicos configurables: `OpeningFiling:MaxFileBytes` (default 26214400, maximo del lector
PDF existente) y `OpeningFiling:MaxProjectStorageBytes` (default 2147483648). La cuota cuenta bytes
fisicos historicos una sola vez; no hay un numero maximo de archivos por categoria o radicaciones.
El servidor sigue siendo autoridad; un 409 exige recargar sin reintentar writes automaticamente.
