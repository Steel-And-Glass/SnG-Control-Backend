# SnG Control — Steel & Glass

SnG Control es la plataforma en desarrollo para el control operativo y gerencial de Steel & Glass. Su propósito es organizar la información de clientes y proyectos como base para los procesos comerciales, técnicos y operativos de la empresa.

## Funcionalidad actualmente implementada

El backend permite a Comercial crear clientes (`Client`) y proyectos (`Project`), consultarlos por su identificador y listar cada colección mediante HTTP. También permite enviar explícitamente un proyecto a Presupuestos, donde se asigna o corrige su código SG, consultar las tareas propias del área activa por estado y consultar el historial de tareas del proyecto. Los datos se guardan en PostgreSQL, en la base `sng_control`. Presupuestos puede consultar precios unitarios, subtotales y total originales del Excel en el resumen de propuesta, tanto en preview como en la versión confirmada. Los importes son de solo lectura y están vinculados al original de esa versión; no se sustituyen por precios comerciales posteriores. Si no son confiables, se muestra una advertencia específica. Presupuestos puede obtener un preview técnico de una cotización XLSX estándar y confirmar sus valores corregidos para crear el catálogo comercial base oficial. El preview no persiste; la confirmación conserva datos en PostgreSQL y archivos en almacenamiento local configurado. Una acción posterior envía la cotización a Comercial y genera su Excel operativo temporal. El Jefe Comercial puede subir su revisión, consultar importes y diferencias técnicas y confirmar el archivo final, iniciando la presentación al cliente.

La API requiere configuración de autenticación Google, identidades del Jefe Comercial, de las cuentas de Presupuestos y Contabilidad y de Gerencia, conexión y carpeta de almacenamiento, además de todas las migraciones del repositorio aplicadas para operar. Los campos de Project solo pueden modificarse mediante la asignación o corrección del SG; confirmar una cotización no modifica Project. Este repositorio contiene solo el backend; permite quitar documentos vigentes antes del envío, pero no eliminar clientes ni proyectos. Las pruebas automatizadas verifican dominio, casos de uso, contratos HTTP, almacenamiento temporal y modelo de persistencia sin requerir una base de datos activa ni acceso a Google.

## Identidad y acceso

Google identifica al usuario mediante el **Subject (`sub`)**, nunca por email o nombre. `GET /api/auth/me` valida el ID Token y devuelve `subject`, `email`, `name`, `roles` como strings e `isManagement`, derivado de la pertenencia a Management. Esta consulta no requiere área activa.

Las membresías V1 provienen de configuración: `Authentication:CommercialAdvisorSubjects` es una lista explícita de asesores; `BudgetsAssigneeSubject`, `CommercialHeadSubject`, `AccountingAssigneeSubject` y `ManagementSubject` asignan sus respectivos roles. Una cuenta puede poseer varios, pero cada request operacional debe enviar **`X-SnG-Active-Role`** con uno que realmente posea. Sin autenticación válida se responde 401; sin área, con un valor inválido o con un rol ajeno, 403. No hay tabla Users/UserRoles, passwords ni tokens propios.

Los roles activos implementados son `CommercialAdvisor`, `Budgets`, `CommercialHead`, `Accounting`, `Installation`, `Engineering` y `Management`. Production y Logistics están reservados en el contrato, sin experiencia operativa. Solo el Asesor activo crea clientes/proyectos y envía a Presupuestos un proyecto propio. Puede modificar documentos propios; el Jefe Comercial escribe únicamente en su revisión comercial. Presupuestos y Jefe consultan documentos de proyectos con su tarea asignada, incluso completada. Gerencia tiene lectura transversal, sin adquirir escritura por compartir Subject con el creador o el Jefe.

La política BudgetsAssignee exige membresía y área activa Budgets. SG, preview, confirmación y envío a Comercial requieren también PrepareQuotation asignada al Subject actual; una tarea sin asignación o asignada a otro usuario no permite esas acciones. CommercialHead activo puede descargar el Excel operativo de proyectos con ReviewQuotation asignada y revisar/confirmar según workflow. El archivo final permite acceso al Jefe activo asignado o al Asesor activo propietario.

Cambiar de área no cambia `Project.CreatedByGoogleSubject`, el responsable comercial original, las asignaciones de tareas ni sus plazos. Los handoffs siguen asignando PrepareQuotation a Presupuestos, ReviewQuotation al Jefe y PresentQuotationToClient al creador original.

## Gerencia V1

La cuenta configurada mediante `Authentication:ManagementSubject` tiene visibilidad transversal de solo lectura. Su resumen muestra el total real de proyectos, los conteos de tareas y Lead Times por estado y área, y hasta diez tareas que requieren atención: primero atrasadas, luego las que vencen hoy y después las próximas a vencer. No se inventa un estado global de proyecto.

La consulta de operación devuelve todas las tareas actuales e históricas, con contexto de proyecto y cliente, SG, ciudad, línea de producto y fechas. Prioriza las tareas pendientes de completar según su Lead Time y deja las completadas al final. Gerencia puede consultar los proyectos, sus tareas e historial de notificaciones, y leer/descargar documentos vigentes, reemplazados y snapshot inicial de cualquier proyecto, antes o después del envío a Presupuestos; el snapshot solo está disponible si existe.

Management no concede permisos para cargar, reemplazar o quitar documentos, enviar a Presupuestos, asignar SG ni confirmar cotizaciones. Aunque el Subject también posea otros roles, Management activo permanece de solo lectura; debe cambiar al área correspondiente para ejercer sus otros permisos. La bandeja personal de notificaciones sigue mostrando solo las dirigidas al usuario; no se copian avisos ajenos. Frontend e Intelligence consumen esta funcionalidad en sus repositorios separados.

## Clientes y proyectos

El área activa determina el ámbito de lectura y las acciones permitidas, no solo qué módulos aparecen. Clientes y Proyectos son navegables directamente para las cinco áreas:

| Área activa | Clientes visibles | Proyectos visibles | Escritura |
| --- | --- | --- | --- |
| CommercialAdvisor | Registro compartido de clientes | Propios | Crear cliente/proyecto, documentos propios y envío inicial |
| Budgets | Relacionados con proyectos accesibles | PrepareQuotation asignada al Subject actual, incluida Completed | Operaciones de Presupuestos según workflow |
| CommercialHead | Relacionados con proyectos accesibles | ReviewQuotation asignada al Subject actual, incluida Completed | Revisión Comercial según workflow |
| Accounting | Relacionados con proyectos accesibles | RegisterInitialDeposit asignada, incluida Completed | Registro único del anticipo |
| Management | Todos | Todos, incluidos legacy | Ninguna |

Backend aplica el ámbito tanto a listados como a URLs directas, tareas y actividad del expediente. La asignación de tarea conserva trazabilidad y acceso histórico; no concede acciones de otra área. Un proyecto legacy sin creador solo es visible por asignación válida o mediante Gerencia. La bandeja personal de notificaciones conserva su filtro por destinatario.

Un **Client** representa un cliente. Tiene un identificador interno único, un nombre y fechas de creación y actualización. Su nombre es descriptivo y nunca reemplaza su identidad interna.

Un **Project** representa un proyecto asociado a un cliente. Tiene identificador interno único, identificador del cliente, nombre, ciudad, línea de producto, código SG opcional, fechas de creación y actualización e identidad del creador. El servidor registra el Subject del usuario autenticado y snapshots opcionales de email y nombre; el body no puede elegirlos. El creador es actualmente el asesor comercial responsable del proyecto. No existe reasignación ni un segundo responsable persistido. Los proyectos históricos pueden tener `createdBy: null`, sin inventarles identidad.

La relación es **Client 1 → N Project**: un cliente puede tener varios proyectos y cada proyecto identifica a su cliente mediante `ClientId`. El cliente debe existir antes de crear el proyecto: el caso de uso lo comprueba y rechaza la creación si no lo encuentra. La base de datos refuerza esa relación mediante una clave foránea. La relación impide eliminar un cliente que tenga proyectos asociados; no existe todavía una funcionalidad de eliminación.

## Reglas funcionales vigentes

- El nombre de un cliente y el nombre y la ciudad de un proyecto son obligatorios. No se aceptan valores nulos, vacíos o formados solamente por espacios en blanco.
- Los espacios exteriores de los nombres de cliente/proyecto y de la ciudad se eliminan; los espacios internos se conservan. El Subject se conserva exactamente. Los snapshots de email y nombre pueden ser null y no se actualizan en solicitudes posteriores.
- Los nombres no se utilizan para identificar entidades. Se permiten proyectos con el mismo nombre tanto para clientes distintos como para un mismo cliente. No hay detección ni advertencias de posibles duplicados.
- Cada entidad recibe su propio identificador interno al crearse. Las relaciones se establecen por ese identificador, nunca por nombre ni por código SG.
- Las fechas se registran en UTC. Al crear un cliente o proyecto, su fecha de actualización coincide exactamente con la de creación. Cambiar realmente el SG actualiza la fecha de modificación del proyecto y conserva su fecha de creación.

### Líneas de producto

Todo proyecto requiere una de estas cuatro opciones:

- Classic
- Essential
- Bioconfort
- Signature

La API recibe y devuelve estos nombres como texto, no como números. No se aceptan valores fuera de esta lista.

### Código SG

Todo proyecto se crea con `SgCode` sin asignar (`null`). Después del envío a Presupuestos, el usuario de esa área escribe el código manualmente. El sistema no lo genera, calcula ni reserva consecutivos. SG es un identificador de negocio y no reemplaza la identidad interna del proyecto.

Se eliminan los espacios exteriores y se convierte la entrada a mayúsculas. El formato es `SG` seguido de uno o más dígitos ASCII `0-9`, sin espacios internos, signos ni letras adicionales. Por ejemplo, `"  sg0012  "` se guarda como `SG0012`: los ceros iniciales se conservan y `SG0012` es distinto de `SG12`. No hay una cantidad adicional mínima ni máxima de dígitos.

Un SG solo puede pertenecer a un proyecto, también ante solicitudes simultáneas. La primera asignación cambia la tarea de Presupuestos de **Pendiente** a **En proceso** y fija su fecha de inicio en UTC. El código y el inicio de la tarea se guardan juntos: si el código ya pertenece a otro proyecto, no se guarda ninguno de esos cambios.

Mientras su tarea siga En proceso, Presupuestos puede corregir el SG. La corrección conserva exactamente el inicio, el deadline y el historial de atraso; no crea ni reinicia tareas. Reenviar el mismo valor normalizado tampoco cambia la fecha de actualización del proyecto. La fecha de finalización permanece vacía.

No se permite asignar SG antes de enviar el proyecto a Presupuestos. Los estados inconsistentes y las tareas completadas producen un conflicto; no se reparan automáticamente.

## Flujo funcional disponible

1. Se crea un cliente proporcionando su nombre y se obtiene su identificador interno.
2. Se crea un proyecto indicando el identificador de ese cliente, nombre, ciudad y línea de producto.
3. El sistema valida los datos y la existencia del cliente, normaliza los textos y guarda el proyecto con su identificador, creador autenticado y fechas; su código SG queda sin asignar.
4. Se puede consultar cada cliente o proyecto por su identificador, o listar la colección correspondiente. La respuesta de un proyecto incluye el identificador de su cliente, sin incrustar el objeto cliente completo.
5. El Asesor Comercial activo propietario carga la documentación del cliente y ejecuta **Enviar a Presupuestos**. Se exige al menos un documento vigente; crear el proyecto no ejecuta este envío automáticamente.
6. El envío conserva el snapshot documental inicial y crea una tarea de **preparar cotización**, asignada a Presupuestos y en estado **Pendiente**. Se puede consultar la bandeja de trabajo propio del área activa por estado, y el historial completo autorizado de tareas de cada proyecto.
7. Cada proyecto solo puede enviarse una vez al flujo inicial de Presupuestos. Un nuevo intento responde conflicto si el proyecto ya tiene esa tarea, cualquiera que sea su estado. La garantía también se mantiene ante solicitudes simultáneas: solo una crea la tarea y las demás responden conflicto. No existe reenvío ni retrabajo en el flujo actual.
8. Presupuestos asigna manualmente SG y la tarea pasa a En proceso. Puede corregir el código mediante la misma operación sin cambiar la fecha original de inicio.
9. Presupuestos prepara la cotización fuera de SnG Control, en Excel, y carga el XLSX estándar para revisar su preview técnico. El sistema comprueba el SG y conserva la tarea En proceso.
10. Presupuestos corrige los valores permitidos y confirma, enviando nuevamente el XLSX y los valores confirmados. El sistema vuelve a interpretar el archivo, valida la correspondencia exacta y crea el catálogo oficial inicial. La tarea continúa En proceso.
11. Presupuestos ejecuta **Enviar a Comercial**. El sistema genera el Excel operativo, completa PrepareQuotation e inicia ReviewQuotation de Comercial asignada al Subject del Jefe Comercial configurado.
12. El Jefe Comercial descarga el archivo temporal con su nombre original y lo modifica externamente en Excel.
13. Sube el XLSX revisado y consulta un preview informativo: precios, total del proyecto y diferencias técnicas respecto a Presupuestos. Para corregir algo debe editar el archivo y volver a subirlo.
14. Confirma el XLSX. El sistema conserva sus bytes exactos como commercial.xlsx, guarda un snapshot comercial separado, elimina confirmed.xlsx y conserva original.xlsx e imágenes.
15. ReviewQuotation se completa y PresentQuotationToClient comienza InProgress, asignada al creador original. Su StartedAt UTC inicia el Lead Time de presentación al cliente.

Las creaciones de clientes y proyectos devuelven el registro guardado y una referencia para consultarlo; el envío devuelve la tarea creada. Las consultas o envíos sobre proyectos inexistentes, o la creación de un proyecto para un cliente inexistente, responden como recurso no encontrado. Las entradas inválidas se rechazan sin crear registros. El historial de un proyecto existente sin tareas es una colección vacía.

Los identificadores, las fechas, el código SG y la identidad del creador no pueden suministrarse en las solicitudes de creación. Los listados de clientes y proyectos y el historial de tareas no tienen filtros. La bandeja de tareas exige área compatible con el rol activo y estado.

Proyectos representa el contexto operacional que se puede leer; Tareas representa únicamente trabajo asignado al Subject actual dentro de su rol activo. El historial de tareas dentro de un proyecto conserva todas sus áreas y tipos autorizados.

| Rol activo | Tipo de tarea en su bandeja | Área | Estado inicial en Frontend |
| --- | --- | --- | --- |
| CommercialAdvisor | PresentQuotationToClient | Commercial | InProgress |
| Budgets | PrepareQuotation | Budgets | Pending |
| CommercialHead | ReviewQuotation | Commercial | InProgress |

Los tres estados Pending/InProgress/Completed son consultables para tareas propias. Compartir Subject entre roles no mezcla bandejas. Management no usa esta bandeja: GET /api/project-tasks responde 403; su consulta transversal permanece en /api/management/tasks. Una petición con área incompatible también responde 403.

No hay paginación ni orden garantizado. La información persiste entre ejecuciones; la preparación de la base se realiza explícitamente y no al iniciar la API.

## Documentos del proyecto

El Asesor Comercial activo responsable (creador del proyecto) puede cargar documentos de cualquier formato, agregar archivos y reemplazarlos explícitamente durante toda la vida del proyecto. Se admiten hasta **15 documentos vigentes**, **100 MB por archivo** y **300 MB de contenido por operación** (MB decimal). Cada carga es todo-o-nada. Los archivos se conservan como bytes opacos: no se interpretan ni previsualizan.

El nombre no identifica al documento: dos archivos planos.pdf pueden coexistir. Un reemplazo debe indicar el documento anterior; crea otro identificador, conserva el archivo y la metadata anteriores como históricos y deja la nueva versión vigente. Los históricos no cuentan para el límite de 15.

Antes del primer envío, el Asesor propietario puede quitar un vigente cargado por error. La baja es lógica: queda excluido del listado y de cualquier descarga, conservando internamente sus bytes y referencia para evitar inconsistencias con almacenamiento. No hay purga física automática. Después del envío no se pueden quitar documentos ni borrar históricos.

Enviar a Presupuestos exige al menos un vigente y guarda juntos el snapshot exacto de documentos, PrepareQuotation Pending y TaskAssigned. El inicio y deadline permanecen vacíos: la primera asignación de SG inicia el Lead Time. Presupuestos obtiene acceso documental de lectura y descarga después del envío; no puede cargar, reemplazar ni quitar archivos. Gerencia también puede leer y descargar documentos antes o después del envío, sin permiso de modificación. El Jefe asignado a ReviewQuotation conserva lectura documental histórica. Solo el Asesor activo propietario puede enviar inicialmente el proyecto.

Las descargas incluyen archivo individual, históricos, ZIP de vigentes y ZIP del snapshot inicial. Los nombres repetidos se desambiguan dentro del ZIP. Las actualizaciones posteriores no cambian el snapshot ni reinician la tarea: cada batch exitoso genera una sola notificación a Presupuestos, incluso con PrepareQuotation completada. Un proyecto legacy que ya tenía esa tarea sigue enviado, puede recibir documentación y avisos, pero no se le inventa un snapshot inicial.

## Tareas de las áreas

Una **ProjectTask** representa trabajo asignado a un área y pertenece a un proyecto: **Project 1 → N ProjectTask**. Registra el área, tipo de trabajo, estado, fechas de asignación, inicio y finalización y `AssignedToGoogleSubject` opcional. PrepareQuotation se asigna a la cuenta Google compartida de Presupuestos; ReviewQuotation al Jefe Comercial y PresentQuotationToClient al creador del proyecto. Las tareas históricas pueden mantener Subject null. No existe reasignación genérica. PrepareQuotation nace Pendiente, con inicio y finalización vacíos; ReviewQuotation se inicia inmediatamente al enviar a Comercial.

El contrato contempla Comercial (`Commercial`) y Presupuestos (`Budgets`), y estados Pendiente (`Pending`), En proceso (`InProgress`) y Completado (`Completed`). El envío crea una tarea Pendiente y la primera asignación de SG la inicia. No hay una acción separada para iniciar tareas. No se permite crear tareas arbitrarias ni devolverlas. El envío a Comercial completa PrepareQuotation e inicia ReviewQuotation; el preview y la confirmación de Presupuestos no cambian el estado de su tarea. La confirmación comercial completa ReviewQuotation y crea/inicia PresentQuotationToClient para el creador original.

Las tareas constituyen el historial operacional y no se eliminan al terminar un área. No hay finalización genérica de tareas ni borrado; la base impide eliminar un proyecto con tareas asociadas. El estado operacional pertenece a las tareas, sin añadir un estado global al proyecto.

## Preview de cotización de Presupuestos

Presupuestos crea la cotización externamente y carga un único `.xlsx` con la hoja estándar **COTIZACIÓN**. El proyecto debe tener SG asignado y su tarea PrepareQuotation de Presupuestos debe estar **InProgress**, con fecha de inicio y sin fecha de finalización. El SG del documento se normaliza como el del proyecto y debe coincidir exactamente, incluidos los ceros iniciales. La carga no asigna ni corrige SG, ni reemplaza datos del proyecto o del cliente.

El preview incluye metadatos del documento y los ítems técnicos con ubicación, sistema, cristal, acabado de aluminio, cerradura, observaciones, ancho, alto, m², cantidad e imagen. Conserva el orden y el número visible de cada ítem, aunque se repita. Distingue principales (`Main`) y adicionales (`Additional`); los bloques vacíos de plantilla no generan ítems. **Quantity continúa agrupada**: no se crean unidades individuales. Los m² se toman del documento, sin recalcularlos.

Las imágenes se extraen en su formato original. Si falta una imagen o hay varias asociables al mismo bloque, ese ítem devuelve imagen vacía y una advertencia identificada por su número y fila. Una estructura o medida que no pueda interpretarse con seguridad causa rechazo del archivo completo.

Los precios de Presupuestos no se devuelven ni constituyen el catálogo económico definitivo; la autoridad económica es el XLSX revisado y confirmado por Comercial. El preview no guarda catálogo, documento, imágenes ni metadatos y no modifica ninguna entidad. Solo la confirmación convierte los valores revisados en información oficial.

No se admiten `.xls`, `.csv`, `.xlsm`, archivos cifrados/ilegibles ni recotizaciones o liquidaciones con estructura distinta, como bloques CONTRATADO/MODIFICADO. El rechazo depende del contenido, no del nombre del archivo.

## Confirmación y catálogo comercial base

Antes de confirmar se pueden corregir **ItemNumber, Location, System, Glass, AluminumFinish, LockType, Observations, Width, Height, SquareMeters y Quantity**. Los textos se recortan por fuera, conservando su contenido interno; el número de ítem es obligatorio, puede repetirse y no requiere una secuencia. Las medidas y cantidades deben ser positivas y Quantity debe ser entera. Los m² confirmados no se recalculan.

**Section, SourceRow, imagen, metadatos y SG documental no son editables.** Cada corrección debe corresponder a un ítem extraído, mediante Section y SourceRow; no se pueden agregar, omitir ni duplicar ítems. El servidor vuelve a interpretar el XLSX durante la confirmación y obtiene de allí la extracción original. No acepta una extracción declarada por el cliente como autoridad.

Confirmar crea una versión inicial de cotización y un catálogo oficial sin precios. Conserva por separado **lo extraído automáticamente y lo confirmado por Presupuestos**, incluidos los valores que no cambiaron. Main/Additional y el orden documental se mantienen; Quantity sigue agrupada. Las imágenes faltantes o ambiguas permiten confirmar y conservan ese estado, sin inventar un archivo de imagen.

El **Excel original queda intacto** y las imágenes conservan sus bytes originales. Los archivos se guardan fuera de PostgreSQL; la base conserva sus referencias. El SG del documento queda como dato histórico de esa confirmación, aunque después se corrija el SG del proyecto. Las relaciones y el almacenamiento utilizan identificadores internos estables.

Solo se admite una confirmación inicial por proyecto; una segunda produce conflicto, también si ambas se solicitan simultáneamente. No existe versión 2, reemplazo, edición posterior ni borrado del catálogo confirmado. La descarga del Excel operativo se habilita después del envío a Comercial; no hay servicio HTTP de imágenes.

La confirmación **no completa Presupuestos ni envía el proyecto a Comercial**: PrepareQuotation permanece InProgress, mantiene su fecha original de inicio y no tiene fecha de finalización. Tampoco modifica Project, Client ni sus fechas. Si falla el almacenamiento, se revierte la operación y se intenta retirar exclusivamente los archivos creados por ella; las limitaciones ante interrupciones del proceso se detallan en la guía técnica.

## Estado persistente de cotización de Presupuestos

GET `/api/projects/{id}/budget-quotation/status` permite a Presupuestos recuperar el estado oficial después de recargar, bajo la misma policy BudgetsAssignee. Responde 200 para un proyecto existente, incluso sin cotización, y 404 solo si no existe. No acepta body; mantiene 401/403 de autenticación/autorización.

- NotConfirmed: aún no existe versión inicial confirmada; metadata nullable, cantidades cero y envío no disponible.
- Confirmed: conserva versión, fecha, cantidad de ítems, suma de cantidades del catálogo histórico y SG documental/actual.
- SentToCommercial: el handoff ya ocurrió; mantiene la metadata histórica y no permite reenviar, incluso después de completar la revisión Comercial o retirar confirmed.xlsx.

`canSendToCommercial` y `sendBlockReason` provienen del backend. Las razones son None, SgChangedAfterConfirmation e InvalidState. Un SG actual distinto del confirmado bloquea el envío y no se repara automáticamente. La consulta comparte la regla con el comando existente y no lee ni genera XLSX, imágenes o extracción completa. El POST `/api/projects/{id}/send-to-commercial` sigue sin body y revalida todo antes de completar PrepareQuotation e iniciar ReviewQuotation; la consulta no sustituye esa validación ni verifica disponibilidad física del archivo.

El expediente conserva vistas completas de las cotizaciones confirmadas. La de Presupuestos muestra metadata, valores técnicos oficiales, extracción original, diferencias e imágenes históricas bajo demanda. La Comercial muestra sus valores técnicos, precios, total del proyecto y diferencias frente a Presupuestos. Ambas se reconstruyen desde datos persistidos; no se vuelve a interpretar XLSX y los snapshots permanecen disponibles después de los handoffs.

La lectura exige un área activa autorizada: el asesor original consulta ambas; Presupuestos consulta su snapshot con PrepareQuotation asignada; el Jefe consulta ambas con ReviewQuotation asignada; Gerencia consulta ambas en solo lectura. La consulta no concede permisos de edición ni amplía las descargas existentes.

## Envío a Comercial y Excel operativo

**Enviar a Comercial** es una acción explícita posterior a confirmar. Requiere la versión inicial, un catálogo con ítems y sus extracciones, el archivo original disponible y PrepareQuotation de Presupuestos En proceso. Solo se permite un envío. Si el SG actual difiere del SG histórico confirmado, el envío se bloquea; no se modifica el documento ni se reconcilian códigos automáticamente.

El sistema genera `confirmed.xlsx` a partir del original y aplica los once valores técnicos oficiales, ubicando cada ítem por su sección y fila fuente. Conserva formato, estructura, fórmulas, imágenes, metadatos y SG documental. No procesa precios ni modifica las columnas económicas o PELÍCULA PROTECCIÓN. Los m² oficiales permanecen en el catálogo; cuando una celda contiene fórmula, conserva la fórmula y su valor almacenado se actualiza con lo confirmado. Excel puede recalcularla al abrir el documento.

`original.xlsx` permanece intacto como evidencia histórica. `confirmed.xlsx` es el archivo operativo para Comercial. El envío completa **PrepareQuotation** conservando sus fechas de asignación e inicio, y crea **ReviewQuotation** de Comercial asignada personalmente al Jefe Comercial y directamente **InProgress**, con StartedAt UTC y sin fecha de finalización. Ese StartedAt inicia el plazo de cinco días laborales de revisión y sus avisos operativos. Project, catálogo y extracciones conservan sus datos.

La descarga, exclusiva del Jefe Comercial, usa exactamente el nombre original del archivo, sin sufijos. Las descargas repetidas entregan los mismos bytes almacenados y no regeneran el Excel ni cambian el workflow. Después de confirmar la revisión comercial, el temporal deja de estar disponible y su ruta responde conflicto; la descarga final tiene una ruta y autorización distintas.

## Revisión y confirmación comercial

La confirmación financiera comercial lee los importes guardados del XLSX, no recalcula Excel.
Un libro con cálculo pendiente explícito o una discrepancia verificable de caché se rechaza antes
de confirmar. Recalcular, revisar y guardar en Excel, volver a subir y comparar precios y total
con el preview. La revisión técnica de Presupuestos no certifica esos importes; las cotizaciones
comerciales ya confirmadas conservan sus valores y documentos históricos.

El preview comercial es **solo de lectura**: no admite correcciones web, precios ni totales enviados por JSON. Requiere ReviewQuotation InProgress asignada al Jefe configurado, el catálogo y sus extracciones, confirmed.xlsx disponible y SG coincidente con el histórico. Confirmar vuelve a interpretar el XLSX recibido; no confía en un preview anterior.

Comercial puede cambiar los once valores técnicos: número de ítem, ubicación, sistema, cristal, acabado, cerradura, observaciones, ancho, alto, m² y cantidad. El preview muestra valores anteriores y actuales y un resumen de diferencias. Esos cambios no bloquean la confirmación. Debe conservarse exactamente el conjunto **Section + SourceRow**: altas, bajas o desplazamientos de filas se rechazan como conflicto estructural. Esta limitación no define una regla permanente para futuras revisiones; no se intenta vincular ítems por número, nombre u orden.

Los importes se leen directamente del documento: **UnitPrice en N** y **TotalPrice en O** en la fila del ítem. **ProjectTotal** se toma de O en la única fila cuya N dice VALOR TOTAL antes del marcador ADICIONALES de A. Las filas son variables. El total posterior de adicionales no se suma. Se admiten cero y fracciones; no se recalculan multiplicaciones, sumas, IVA ni descuentos. Las fórmulas requieren un resultado numérico guardado en el XLSX.

La confirmación crea CommercialQuotation y CommercialQuotationItem como **realidad comercial vigente**, conservando CommercialCatalog, CommercialCatalogItem y CommercialCatalogItemExtraction como historia de Presupuestos. Los ítems nuevos enlazan sus ítems históricos para reutilizar las imágenes; no se crea otro conjunto de imágenes. El archivo commercial.xlsx se conserva exactamente como fue subido, sin regenerarlo.

La operación completa ReviewQuotation, preserva su inicio y crea/inicia una única PresentQuotationToClient para el Subject del creador original. Un proyecto histórico sin creador puede tener preview, pero confirmar responde **409 ProjectCreatorUnavailable**: no se inventa asesor. No existe reasignación. Solo se admite una confirmación comercial inicial; solicitudes repetidas o concurrentes no crean otra cotización ni otra tarea.

Al terminar, permanecen **original.xlsx, commercial.xlsx y las imágenes históricas**. confirmed.xlsx se elimina físicamente y su referencia queda null. Ante fallos se revierte DB y se compensan los archivos cuando el resultado es conocido; una interrupción o commit incierto puede requerir inspección administrativa, como detalla la guía técnica. La descarga final está disponible para el Jefe Comercial y el creador del proyecto y no cambia estado ni archivos.

## Plazos laborales y notificaciones

Preparar cotización, revisar cotización y presentar al cliente tienen **cinco días laborales**. Para Steel & Glass cuentan lunes a sábado; no cuentan domingos ni festivos nacionales de Colombia. El día de inicio se excluye: se cuenta desde el siguiente día laboral y se vence el quinto día a las **17:00 de Colombia**, usando America/Bogota. Por ejemplo, una tarea iniciada el 14/10/2026 vence el 20/10/2026 a las 17:00; el sábado 17 cuenta.

El deadline se guarda al iniciar y queda congelado. Consultas, reinicios, correcciones de SG y cambios futuros de política no lo recalculan. Enviar a Presupuestos asigna inmediatamente a su cuenta Google compartida y genera un aviso, pero conserva la tarea Pending sin deadline: su plazo comienza únicamente con el primer SG. ReviewQuotation y PresentQuotationToClient se asignan e inician en sus transiciones actuales.

Cada aviso se dirige exclusivamente al asignado. Se registra la asignación, un recordatorio dos días laborales antes a las 08:00, otro el día del vencimiento a las 08:00 y un único aviso de atraso después del deadline. Completar exactamente en el deadline cuenta como cumplimiento. WasOverdue conserva si hubo atraso alguna vez y nunca vuelve a false; una tarea legacy sin seguimiento conserva deadline y cumplimiento desconocidos, sin reconstrucción ficticia.

Al completar tarde, el aviso de atraso se resuelve con la fecha de finalización, pero permanece en el historial. Si el worker aún no lo había generado, la finalización deja igualmente la evidencia resuelta. No se generan recordatorios posteriores a la terminación ni se borran avisos históricos.

Se pueden consultar las notificaciones personales y el historial cronológico completo por proyecto. Son registros internos: no hay leído/no leído, contador de campana, email, WhatsApp, SMS ni push. La asignación identifica al destinatario; no agrega restricciones nuevas a las acciones generales del proyecto.

El worker comprueba las tareas al arrancar y aproximadamente cada minuto. Tras una interrupción recupera asignaciones faltantes y únicamente el hito temporal más relevante que corresponda en ese momento, sin duplicados ni acumulación de recordatorios superados. ScheduledAt conserva cuándo correspondía el aviso y CreatedAt cuándo se generó. Si el proceso está detenido, los avisos se retrasan hasta su recuperación.

La reasignación queda para un alcance futuro: deberá conservar historia, emitir una nueva asignación y reiniciar los cinco días para el nuevo responsable. Actualmente no existe operación de reasignación.

## Contexto funcional futuro

El contexto conocido contempla procesos comerciales, técnicos, operativos y gerenciales alrededor del proyecto: aprobación del cliente, devolución de tareas, evolución del catálogo comercial, nuevas versiones de cotización, mediciones, Actas de Vanos versionadas, catálogo técnico, pagos, producción, logística y órdenes de producción.

PresentQuotationToClient está iniciada para el asesor, pero todavía no hay confirmación de presentación, aprobación del cliente ni anticipo. Los módulos futuros no están implementados. Sus reglas y flujos se definirán en sus respectivas fases, sin anticiparlos en el núcleo actual.

Para ejecutar o continuar el backend, consultar [README_TECHNICAL.md](README_TECHNICAL.md).


### Moneda y precisión comercial

Las cotizaciones comerciales declaran `currencyCode: "COP"` en preview, confirmación y lectura.
ProjectTotal, UnitPrice y TotalPrice usan decimal con dos decimales, redondeados explícitamente
con AwayFromZero antes de persistir (numeric(20,2)). El workbook sigue determinando cada importe;
no se suman ni recalculan los totales. Las medidas técnicas conservan su precisión original.
La migración FormalizeCommercialQuotationMoney normaliza importes existentes sin borrar registros.
Debe aplicarse manualmente y desplegarse con los contratos actualizados de Intelligence/Frontend.


## Quotation Portfolio Analytics V1

`GET /api/management/quotation-portfolio` requiere autenticación y área activa Management.
Devuelve agregados de las cotizaciones comerciales confirmadas actualmente almacenadas: COP,
quotationCount (cotizaciones), itemCount (renglones), totalQuantity (unidades) y totalQuotedValue
(suma exacta de CommercialQuotation.ProjectTotal). No representa ventas, ingresos, facturación,
márgenes ni el historial completo de versiones comerciales. No hay filtros temporales en V1.

Rankings: highestValueItems por TotalPrice, highestUnitPriceItems por UnitPrice y
highestValueProjects por ProjectTotal, todos descendentes y con desempates determinísticos.
Cada ranking incluye total/returned/hasMore/items; máximo 10, sin limit arbitrario del cliente.
Un portfolio vacío devuelve ceros (importe 0.00) y rankings vacíos. No se calcula UnitPrice × Quantity
ni se reemplaza ProjectTotal por suma de renglones. Los importes usan decimal/COP y escala 2;
si la suma supera numeric(20,2), falla en lugar de recortarse o alterar Money Contract.

La consulta usa 21 SELECT de agregados/joins con AsNoTracking en una transacción RepeatableRead;
el número es constante con cualquier cantidad de proyectos. El orden usa collation C para nombre
proyecto e ItemNumber; luego SourceRow, ProjectId y Section para ítems, ProjectId para proyectos.
Solo se exponen identificadores de proyecto, no IDs internos de cotización/catálogo ni storage.
No hay nueva migración, cambio de entidades ni acceso a archivos XLSX.


## Quotation Mix Analytics V2

El mismo endpoint incorpora `mix`: systems, glass, aluminumFinishes y
systemGlassCombinations. Cada dimension contiene totalItems, specifiedItems,
unspecifiedItems y tres rankings top 10: byQuantity (unidades), byItemCount
(renglones) y byQuotedValue (suma de TotalPrice). Cada ranking declara
 total/returned/hasMore/items; total cuenta categorias, no renglones.

Se conservan los valores almacenados y sus mayusculas. El dominio ya recorta
espacios al guardar; valores vacios no se convierten en categorias. Una combinacion
requiere sistema y vidrio. Los desempates usan las otras dos metricas y luego
valor/tupla con collation C. Una categoria cuyo importe exceda Money falla incluso
si no aparece en el top. No representa demanda, ventas, preferencias ni rentabilidad;
no incluye porcentajes ni filtros por periodo, ciudad o cliente.

PostgreSQL agrupa, suma, cuenta y limita; no se cargan todos los items en memoria.
Los cinco SELECT originales y cuatro por dimension comparten RepeatableRead.


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

## Versioned Requotation Loop V2

Las tareas de cotización llevan `quotationRound` positivo. La ronda inicial sigue pendiente
hasta el primer SG; las siguientes empiezan en Presupuestos con el SG conservado y el
calendario oficial. Solo el asesor asignado, con CommercialAdvisor activo, puede abrirlas
mediante `POST /api/projects/{id}/requotation-request` con `{requestedChanges}` (1–4000
caracteres, sin campos adicionales), después de ChangesRequested y presentación completada.
La solicitud es inmutable y se inserta con tarea y notificaciones en una transacción.
Duplicados devuelven 409 AlreadyRequested. Approved no significa pago; Rejected no cierra
el proyecto y ChangesRequested no crea una ronda automáticamente.

`GET /api/projects/{id}/quotation-rounds` usa el acceso compartido y devuelve las primeras
20 rondas ascendentes, con total/returned/hasMore. Incluye estados oficiales, versiones,
decisiones y cambios formalizados por el asesor en la ronda destino. No calcula diferencias
entre ítems. Preview/status distinguen `quotationRound` activa de `versionNumber` confirmada.
La lectura de Presupuestos toma la mayor versión confirmada; Comercial, sus descargas y
Portfolio/Mix toman la mayor versión con cotización comercial confirmada por proyecto.
Abrir una nueva preparación conserva la comercial anterior. Los archivos y versiones
anteriores siguen almacenados; los conteos de tareas incluyen todas las rondas.

Migración: `20261028160515_AddVersionedRequotationRounds`, posterior a
`20261028152306_AddClientQuotationDecisions`. Hace backfill a ronda 1, cambia unicidad de
tareas a proyecto+ronda y agrega RequotationRequests. No se aplica automáticamente a la
base operativa. El downgrade rechaza bases con recotizaciones para no destruir historia.
El smoke opt-in RequotationPostgresTests requiere SNG_REQUOTATION_TEST_DATABASE apuntando
exclusivamente a PostgreSQL local aislado, base `requotation_test`, sin datos de producto.


## Comprobantes y verificación del anticipo

El asesor responsable, con CommercialAdvisor activo, puede cargar varios comprobantes
cuando la cotización Comercial vigente tiene decisión Approved y presentación completada.
La carga no acredita recepción de dinero ni crea trabajo contable. El envío explícito
selecciona los archivos exactos, crea una revisión pendiente y habilita RegisterInitialDeposit
para la cuenta contable configurada. Los reintentos del mismo envío no duplican la solicitud.

Contabilidad asignada consulta cotización, comprobantes e historial. Puede rechazar con
observación obligatoria o aprobar indicando el total efectivamente verificado, positivo en COP,
y fecha de recepción con zona horaria, no futura. No se suman comprobantes ni se infieren
porcentajes o saldos. Un rechazo conserva evidencia y permite al asesor agregar o sustituir
comprobantes y enviar otra solicitud; solo existe una pendiente por proyecto.

La aprobación registra InitialDeposit inmutable, completa la tarea contable, notifica al asesor
y crea una única tarea TakeMeasurements (tipo 5, área Installation=4), asignada a la cuenta de
Instalación configurada. Todo se confirma en una transacción. Contabilidad e Instalación no
tienen SLA ni ronda propia. La tarea de Instalación queda Pending hasta iniciar el checklist de medidas.
Acta de Vanos, firmas, Producción y Logística quedan fuera de este flujo.

Configurar privadamente Authentication:AccountingAssigneeSubject (obligatorio al iniciar)
y Authentication:InstallationAssigneeSubject. Si falta Instalación, la aprobación devuelve 503
InstallationAssigneeNotConfigured sin cambios. Google Subject define identidad; el rol activo
y la asignación se validan en Backend. Management mantiene lectura transversal sin escritura.
Los comprobantes usan el almacenamiento y versionado documental existente; sustituir conserva
los bytes históricos. Durante una revisión pendiente o después de aprobar no se modifican.

La migración 20261103134446_AddDepositVerificationReceipts agrega revisiones y vínculos
documentales. Su aplicación operativa requiere autorización explícita; no se migra al arrancar.
El anticipo no implica pago total, ingreso reconocido ni un saldo contractual.

El rechazo contable crea CorrectDepositReceipts (tipo 6, Commercial), asignada al asesor
responsable e iniciada sin SLA. Aparece en su bandeja En curso y enlaza el expediente.
El reenvío completa esa tarea en la misma transacción que crea la revisión contable.
Los rechazos consecutivos conservan tareas distintas ya completadas; solo puede existir
una corrección activa por proyecto. No se reabre PresentQuotationToClient.
La tarea es visible al asesor asignado y a Gerencia; las revisiones y comprobantes conservan
las lecturas compartidas existentes. No se crean correcciones retroactivas para rechazos previos.

Precisión técnica de comparación: ancho/alto a 3 decimales en metros (1 mm) y superficie a
6 decimales en m², con MidpointRounding.AwayFromZero. TechnicalMeasurePrecision se aplica
solo al comparar; CommercialTechnicalDifferences devuelve las diferencias reales con valores
normalizados legibles. Cantidades siguen siendo enteros exactos; precios, fórmulas y valores
originales persistidos no cambian. No es una tolerancia de fabricación o instalación.
Frontend implementa la misma regla mediante dígitos decimales/BigInt; los casos de prueba
compartidos por contrato cubren equivalencias, mitades, bordes, valores pequeños y grandes.


## Instalación: toma de medidas

Después de aprobar el anticipo, la cuenta asignada con rol activo Installation inicia la tarea
Toma de medidas, sin SLA. El checklist persistido genera una estructura por unidad de cada ítem
comercial de la cotización vinculada al anticipo; no reinterpreta archivos XLSX.
Las estructuras cotizadas siguen sección, fila original y unidad; las agregadas aparecen al final
por fecha de creación e identificador. Las retiradas conservan su posición histórica.
Cada unidad permite marcar/desmarcar con auditoría, agregar estructuras en obra con descripción,
ancho/alto y sistema, y retirar unidades lógicamente. La cotización y sus precios no cambian.
El avance cuenta solo estructuras vigentes; cero vigentes muestra 0 % y bloquea la radicación.

Instalación carga múltiples documentos de medidas usando el almacenamiento del proyecto.
Al confirmar Radicar medidas, todas las estructuras vigentes deben estar medidas y debe existir
al menos un documento vigente. Se congelan el snapshot y los IDs exactos de los archivos,
se registra actor/fecha y se completa la tarea en una transacción. No se admiten cambios posteriores.
Gerencia consulta checklist, progreso, historial y radicación sin escritura. El expediente muestra
estos datos también después de recargar. La radicación inicia Ingeniería desde su snapshot oficial, sin extracción automática de medidas.

La captura en obra guarda ancho, alto y sistema libre por estructura, separados de los datos
originales cotizados. Cada cambio queda auditado con valores anteriores/nuevos y actor/UTC.
El snapshot final incluye la captura y bloquea su edición. Los registros históricos sin captura
conservan valores nulos; no se copian medidas cotizadas como si fueran mediciones.
La interfaz muestra el dibujo persistido del ítem, campos asociados al dibujo y tres tarjetas
por fila en escritorio. Las observaciones nuevas de Instalación son opcionales y se conservan separadas de las notas históricas cotizadas. Las estructuras agregadas utilizan el mismo guardado y cierre.

Cada unidad incluye ficha técnica: manija pendiente/no aplicable/confirmada, vista por dentro o
por fuera, tipo de apertura, sentido y lado de bisagras, y alfajía. Izquierda/derecha siempre
indica el lado de las bisagras observado desde la vista seleccionada; cambiar de vista no lo invierte.
La sugerencia de manija de 1100 mm no se guarda como medición hasta confirmarla o editarla.
Para marcar Medido se requieren medidas reales y ficha completa, con campos condicionales según
aplicación. Los checks anteriores se conservan; desmarcarlos exige completar la ficha al remarcar.
El croquis es opcional salvo modificación formal, independiente por unidad y editable con mouse, dedo o lápiz. Sus versiones
conservan trazos editables y WEBP privado; cancelar no cambia el guardado. Retiro y radicación
bloquean la edición. El Acta de Vanos se elabora externamente y se carga en Ingeniería.

Steel & Glass utiliza **milímetros como unidad de negocio para todas las longitudes**. Instalación
muestra y captura longitudes en mm, incluidas las referencias cotizadas. Los contratos existentes
de ancho/alto siguen en metros exclusivamente por compatibilidad técnica: se convierten en los
límites de la interfaz (2000 × 2400 mm ↔ 2 × 2,4 m). Manija y alfajía se guardan directamente en mm.
Superficies en m² e importes monetarios no cambian; no se realiza una migración global de unidades.


La ficha registra el tipo del par de dimensiones (acordada, producción o vano), ubicación real
individual y observaciones opcionales. Tipo y ubicación son obligatorios para nuevas confirmaciones
Medido; no se inventan valores para registros históricos. Alfajía ofrece 114 mm, 160 mm u Otro,
conservando su valor numérico. Reintegrar recupera la misma estructura, posición y croquis, conserva
el retiro en historial y exige confirmar nuevamente Medido.

La cotización no clasifica sus dimensiones por tipo de medida. La Instalación asignada revisa
explícitamente la comparabilidad, con motivo obligatorio. Solo una referencia confirmada comparable
produce diferencias absolutas en mm: 100 mm no supera el umbral; más de 100 mm exige modificación
formal. Una revisión no comparable permanece identificada como pendiente de comparación, nunca
como ausencia de modificación; la resolución motivada permite completar el levantamiento sin
inventar ajustes productivos. Cambiar dimensiones o tipo invalida la revisión anterior.

Una modificación registra tipo, motivo, descripción, versión y asociación explícita a un croquis
persistido que documente ese cambio. Se pueden guardar borradores incompletos, pero no marcar
Medido hasta completar la modificación sobre la ficha vigente. Cambios posteriores de ficha
requieren reconfirmarla. No hay eliminación de modificaciones históricas. El editor permite textos
editables, movimiento por selección y nueva posición, eliminación y deshacer/rehacer; conserva
los croquis anteriores y exporta únicamente WEBP privado.

La radicación confirma conjuntamente el snapshot, el PDF oficial y el cierre de tarea. El reporte
A3 horizontal presenta hasta tres estructuras por página en orden comercial: croquis de obra,
imagen histórica de cotización y ficha técnica en mm. Incluye encabezado compacto, modificaciones
y paginación; el texto extenso continúa en páginas identificadas sin recortes. Las imágenes ausentes
y las fichas históricas incompletas se indican expresamente. Los PDFs ya radicados no cambian. Se descarga
por Instalación asignada y Gerencia con los permisos del proyecto, como `Radicacion de medidas-[Nombre del proyecto].pdf` (nombre seguro, contenido histórico intacto). No se genera automáticamente el Acta de Vanos; se crea la tarea de Ingeniería. Los snapshots anteriores permanecen intactos y no reciben un PDF
reconstruido desde datos actuales.


## Ingeniería: checklist y Acta de Vanos

La radicación de Instalación crea y comienza «Elaborar Acta de Vanos», asignada al Google Subject configurado en `Authentication:EngineeringAssigneeSubject`. La ausencia de configuración bloquea la radicación con un error específico antes del cierre. Tarea, notificación, checklist, snapshot y reporte se confirman conjuntamente; un reintento no duplica trabajo.

Cada unidad vigente del snapshot radicado conserva su ID, orden, medidas en mm, ficha, imágenes y croquis de solo lectura. Se incluyen adiciones y reintegradas, excluyendo las finalmente retiradas. Solo Ingeniería activa y asignada marca/desmarca cada estructura; el servidor guarda responsable, fecha UTC e historial, calcula el avance y detecta conflictos sin sobrescribir otros checks.

El Acta se elabora externamente y se carga como PDF (máximo 25 MiB). Cada reemplazo conserva los bytes y metadatos de versiones anteriores, con descarga autenticada y verificación SHA-256. El cierre requiere todos los checks y un Acta vigente íntegra, además de confirmación humana: congela el snapshot final y documento, completa la tarea y concilia sus notificaciones existentes. El cierre inicial crea la tarea de presentación del Acta para el asesor responsable y su notificación. Ingeniería no tiene SLA; conserva inicio, responsable y finalización para auditoría.

Los expedientes ya radicados no reciben tareas por la migración. Su incorporación exige el procedimiento explícito descrito en README_TECHNICAL.md, sin modificar la radicación histórica.


## Presentación y respuesta del Acta de Vanos

Al completar Ingeniería se crea «Presentar Acta de Vanos al cliente» para el asesor responsable del proyecto, validando su membresía configurada. El cierre, la ronda, la tarea y la notificación son atómicos e idempotentes. Una identidad comercial ausente o inválida bloquea el cierre sin modificar el expediente.

Cada ronda conserva el PDF exacto y todas las estructuras vigentes del snapshot, en su orden original. El asesor confirma la presentación explícitamente; solo entonces comienza el tiempo de respuesta, con fechas UTC del servidor y sin SLA, vencimientos ni alertas. Las confirmaciones individuales se guardan por estructura. La respuesta formal detiene el tiempo de esa ronda.

Solicitar cambios exige seleccionar estructuras pendientes y describir cada cambio; admite observaciones generales. La confirmación informa que las no seleccionadas quedan Confirmadas respecto al PDF presentado. Cierra esa tarea comercial y crea «Revisar cambios del Acta de Vanos» para Ingeniería. Las aprobaciones y solicitudes anteriores permanecen auditadas.

Ingeniería trabaja checks independientes de su checklist inicial, sobre las estructuras observadas y las adicionales que declare afectadas. Debe declarar explícitamente todas las afectadas, completar sus checks y cargar un nuevo PDF asociado a esa declaración. No se analizan ni infieren cambios internos del PDF. Al cerrar se congela la revisión y se crea otra ronda comercial: las afectadas vuelven a Esperando aprobación; las confirmaciones de estructuras sin cambios se conservan con su versión de origen.

Aceptar definitivamente exige todas las estructuras Confirmadas, un PDF firmado persistido en la ronda vigente y la declaración explícita del asesor de que está firmado y corresponde al Acta presentada. El sistema conserva esa declaración; no verifica criptográficamente firmas ni deduce correspondencia por nombre. La aceptación congela la ronda, completa Comercial y crea una única «Radicación de Vanos» asignada a Ingeniería, pendiente y sin SLA. Conserva las referencias a radicación original, documentos presentado/firmado y estructuras aprobadas. Su ejecución no está implementada y la aceptación no autoriza fabricar. «Detener proyecto» permanece deshabilitado.

Los expedientes anteriores se habilitan solo mediante el procedimiento controlado del manual técnico; no reciben tareas por backfill automático.

## Instalacion incremental por tramos

La persistencia incluye radicaciones independientes, evidencia versionada, enmiendas, Actas
lógicas, alcance documental, asignaciones por paquete y aceptaciones parciales con tareas
de Radicación de Vanos vinculadas a su origen. El modo incremental permite seleccionar estructuras medidas, radicar PDFs por tramo y continuar
Instalacion. Ingenieria acumula las entregas y conserva checks y evidencia. El flujo tradicional
descrito arriba permanece para expedientes anteriores.

El legado se conserva sin regenerar PDFs ni reabrir tareas. Cuando el alcance no puede
reconstruirse desde evidencia histórica, queda clasificado como
`LEGACY_INCOMPLETE_EVIDENCE`; sigue disponible por las consultas históricas autorizadas,
pero no alimenta operaciones incrementales que requieren evidencia completa.
La ausencia de PDF se registra separadamente y no invalida por sí sola una radicación.


El modo se fija al iniciar el checklist, exclusivamente para proyectos nuevos posteriores al corte
UTC explicito `Measurements:IncrementalNewProjectsFromUtc`, una vez desplegados ambos consumidores.
Sin corte configurado se mantiene el circuito tradicional. No se aplica retroactivamente.
Cada entrega conserva IDs, alcance documental, hashes, actor y PDF A3 de tres estructuras por pagina.
Solo sus estructuras quedan congeladas; pueden medirse otras y agregarse nuevas.
Ingenieria permite recepcion y checks por estructura; las Actas/cierre/presentacion comercial
incrementales siguen bloqueados. No hay enmiendas operativas ni Radicacion de Vanos en esta entrega.


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
es "Confirmada por Ingenieria - Pendiente de habilitacion comercial incremental". No crea rondas,
presentaciones, aprobaciones, firmas ni radicaciones comerciales. Todos los bloqueos historicos se conservan.

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
