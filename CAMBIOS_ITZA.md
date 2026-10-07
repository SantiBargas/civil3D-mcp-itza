# Cambios ITZA sobre Sacred-G/Civil3D-mcp

Registro de todo lo que ITZA cambia respecto del upstream
([Sacred-G/Civil3D-mcp](https://github.com/Sacred-G/Civil3D-mcp), base: commit `9e437a3`).
Este fork es **sacred-g-itza** (repo privado
[SantiBargas/sacred-g-itza](https://github.com/SantiBargas/sacred-g-itza)).

Formato: una sección por día (MM-DD), lo más nuevo arriba. Cada cambio dice **qué**, **por qué** y
**estado de verificación** (compila / tests / probado en vivo en Civil 3D).

---

## 10-07 (2026)

### Renombre a sacred-g-itza

- Carpeta local `Documents\GitHub\Civil3D-mcp` → `Documents\GitHub\sacred-g-itza`.
- `package.json` / `package-lock.json`: `name` y `bin` → `sacred-g-itza`; `repository`/`bugs`/
  `homepage` → `SantiBargas/sacred-g-itza`. `scripts/inspect-package.mjs` valida el `bin` nuevo.
- Se mantiene el nombre del servidor MCP `civil3d-mcp` (`src/index.ts` y registro en Claude Code)
  para que las tools sigan llamándose `mcp__civil3d-mcp__*`.
- Git: `origin` = [SantiBargas/sacred-g-itza](https://github.com/SantiBargas/sacred-g-itza)
  (**privado**, no fork de GitHub — un fork de repo público no puede ser privado y este repo tiene
  contexto interno de ITZA); `upstream` = Sacred-G. Se pusheó el historial de `main` hasta
  `9e437a3`; los cambios ITZA quedan sin commitear hasta que los commitee el usuario.
- MCP re-registrado (scope user) con la ruta nueva del bootstrap script.
- **Gotcha del renombre:** la carpeta quedaba bloqueada por instancias viejas del servidor MCP,
  otras sesiones de Claude y un proceso auxiliar de VS Code (vigilante/Git del repo). Se encontró
  con `resmon` → CPU → Identificadores asociados.

### Parcelas: creación por COM en vez de reflexión .NET

- **Archivo:** `Civil3D-MCP-Plugin/ParcelEditingCommands.cs` → `CreateParcelFromEntity`.
- **Problema:** `civil3d_parcel create` siempre fallaba con *"Parcel was not created. Check that the
  boundary is closed..."*, aun con polígonos válidos.
- **Causa (confirmada con log de diagnóstico + lectura de metadata de `AeccDbMgd.dll`):** la API
  .NET de Civil 3D 2026 **no tiene ningún método para crear parcelas**. `Site` solo expone
  `GetParcelIds()` (lectura); `site.Parcels` no existe; `Parcel` y `ParcelSegment` no tienen
  métodos estáticos de creación. Las 3 vías que probaba el upstream por reflexión
  (`site.CreateParcelFromEntity`, `site.Parcels.Add`, `Parcel.Create`) devolvían `null` en silencio.
- **Fix:** ruta COM — `site.AcadObject.ParcelSegments.AddFromEntity(entity.AcadObject, false)`;
  Civil 3D forma la parcela solo a partir de los segmentos. La parcela nueva se detecta comparando
  `site.GetParcelIds()` antes/después. Si COM tira excepción, se devuelve el mensaje real
  (`CIVIL3D.API_ERROR`) en vez de tragarlo. Mismo mecanismo que usa HMK CAD Pilot (observado en la
  auditoría de comportamiento del 09-25, no copiado).
- Se borró el log de diagnóstico temporal (`%TEMP%\parcel-diag.log`) y el helper `FindType` que
  quedó sin uso.
- **Estado:** ✅ **probado en vivo** (Civil 3D 2026, dibujo métrico): "Lote 1" 20×30 → 600 m²;
  "Lote 2" adyacente compartiendo lado → 450 m², las dos en el Site "Loteo".

### Nueva acción `civil3d_parcel` → `create_site`

- **Archivos:** `ParcelEditingCommands.cs` (`CreateSiteAsync`), `CommandDispatcher.cs`
  (método `createSite`), `src/tools/domains/parcelDomain.ts` (acción `create_site`).
- **Por qué:** el upstream no tenía forma de crear un Site, y sin Site no se puede crear ninguna
  parcela → había que crearlo a mano desde Prospector.
- **Cómo:** `Site.Create(civilDoc, siteName)` (sí existe en la API .NET — verificado en la metadata
  del DLL). Devuelve `CIVIL3D.CONFLICT` si ya existe un Site con ese nombre (sin distinguir
  mayúsculas). Capability `create` → pide aprobación (`civil3d_request_approval`) como el resto.
- **Estado:** compila, `npm run build` OK, 422/422 tests OK, ✅ **probado en vivo** (Site "Loteo"
  creado, handle `712C`).

### Parcelas: `perimeter` y `style` en `null`

- `civil3d_parcel create/get/list/report` devuelven `perimeter: null` y `style: null`; en `list`
  rompe la validación zod del lado TS (espera `number`/`string`) → `list` falla entero.
- **Causa (metadata de `AeccDbMgd.dll`):** `Parcel` no tiene propiedad `Perimeter` en .NET (solo
  `StyleId, Address, TaxId, IsAreaSelectionLabelPinned, Centroid, AreaLocation,
  AreaSelectionLabelLocation, AreaSelectionLabelStyleId, Number` + las de `Entity`). El nombre del
  estilo (`StyleBase.Name`) sí existe, pero la lectura genérica por reflexión devuelve `null`.
- **Fix:** `GetParcelPerimeter` lee `parcel.AcadObject.Statistics.Perimeter` (COM) en vez de
  reflexión + vértices; nuevo helper `GetParcelStyleName` castea el estilo a `StyleBase` y lee
  `Name` tipado (reemplaza las 3 lecturas por reflexión en `get`/`list`/`report`).
- **Estado:** ✅ **probado en vivo** — Lote 1: 600 m² / 100 m; Lote 2: 450 m² / 90 m; `list`
  devuelve ambos con `style: "Property"`.

---

### Fix de los 4 bugs de la etapa 1

1. **Import de puntos** (`PointCommands.cs`, `ParsePointImport`): `pnezd` → X = Este
   (`tokens[2]`), Y = Norte (`tokens[1]`); `penz` → X, Y, Z en orden. Ahora además: respeta el
   número de punto `P` del archivo (si ya existe en el dibujo → `CIVIL3D.CONFLICT` y no importa
   nada, la transacción se descarta entera); saltea una primera línea de encabezado no numérica (el
   crash `'Northing' was not in a correct format` del 09-22); parsea con `InvariantCulture` (punto
   decimal) y da error claro si un número no parsea; la descripción conserva espacios.
   `xyzd` acepta líneas sin descripción.
2. **`groupName` en import**: faltaba en el schema zod (`pointDomain.ts`, se descartaba antes de
   llegar al plugin) y el plugin no lo usaba. Ahora se agregan los números importados al
   `IncludeNumbers` del grupo, creándolo si no existe (`PointGroupCommands.AddPointNumbersToGroup`).
3. **Capas** (`LookupUtils.GetLayerId`): si la capa pedida no existe, se crea (validando el nombre)
   en vez de caer en la capa actual en silencio. Aplica a todas las tools que reciben `layer`
   (polilíneas, textos, líneas, alineamientos, perfiles).
4. **Carpetas permitidas** (`FileBoundary.LoadRoots`): raíces por defecto siempre presentes —
   Documents + Escritorio (lectura y escritura) y `%LOCALAPPDATA%\Autodesk` (solo lectura, ahí
   están las plantillas `.dwt` de Civil 3D). Las variables `CIVIL3D_FILE_ROOTS` /
   `_IMPORT_ROOTS` / `_EXPORT_ROOTS` ahora **suman** carpetas, ya no reemplazan las de por defecto
   (antes, setear una variable dejaba sin acceso a Documents). Tablas de variables actualizadas en
  `README.md` y `docs/DEPLOYMENT.md`.
- Extra: el `export` de puntos escribe `pnezd` en orden N,E (antes E,N) y con
  `FormattableString.Invariant` (antes, en Windows en español salían comas decimales que rompían
  el CSV).
- **Corrección en la prueba en vivo:** reasignarle a un punto su mismo número tira
  `ArgumentException: PointNumber is existing` (y el `catch` original esperaba
  `Autodesk.AutoCAD.Runtime.Exception`) → ahora solo se reasigna si difiere del autoasignado, y se
  atrapa `ArgumentException`.
- **Estado:** compila; `npm run build` OK; 422/422 tests; `FileBoundaryHarness` OK; ✅ **probado en
  vivo** repitiendo la etapa 1 en limpio: `drawing new` con la plantilla métrica de
  `%LOCALAPPDATA%\Autodesk` (bug 4, import); `import pnezd` con encabezado `P,N,E,Z,D` + `groupName`
  → 35 puntos con números 1–35, punto 35 en E 5240 / N 10160, grupo "Relevamiento" con 35 (bugs
  1 y 2); polilínea en capa nueva `TERRENO-PERIMETRO` (bug 3); `save` directo al Escritorio
  (bug 4, export).
- Mejora menor pendiente: el grupo guarda `IncludeNumbers` como lista completa
  (`1,2,3,…,35`) en vez de rangos (`1-35`).

### Prueba del loteo, etapa 1 (comparación con HMK) — bugs encontrados

Etapa 1 completa con tools propias (35 puntos, grupo, TIN de 48 triángulos, 38.400 m², borde
exterior), idéntica a HMK. En el camino aparecieron:

1. **`civil3d_point import` formato `pnezd` invierte N y E** (`PointCommands.cs`,
   `ParsePointImport`): toma `tokens[1]` (Norte) como X y `tokens[2]` (Este) como Y. `penz`
   también está mal (`tokens[3]` como Y, que es la Z). El `export` `pnezd`
   (`PointGroupCommands.cs`) escribe X,Y en vez de N,E. Fix propuesto: pnezd → X=`tokens[2]`,
   Y=`tokens[1]`; penz → X=`tokens[1]`, Y=`tokens[2]`, Z=`tokens[3]`; export pnezd → Y antes que X.
   Además usar `CultureInfo.InvariantCulture` en los `double.Parse`.
2. **`import` ignora `groupName`**: el TS lo acepta pero el plugin no crea ni llena el grupo.
3. **`civil3d_geometry create_polyline` ignora `layer`**: la polilínea queda en capa `0`.
4. **Abrir plantilla / guardar fuera de `Documents` bloqueado** por `FileBoundary`
   (`CIVIL3D_FILE_ROOTS` / `_IMPORT_ROOTS` / `_EXPORT_ROOTS`; si no están, la única raíz es
   `Documents`). Bloquea `drawing new` con la plantilla de `AppData\...\Template` y `save` a otras
   carpetas. Opción: setear `CIVIL3D_FILE_ROOTS` (requiere reiniciar Civil 3D) o sumar la carpeta
   de plantillas como raíz por defecto.

---

## 09-22 (2026)

### Reflexión: búsqueda de propiedades/métodos en interfaces

- **Archivo:** `Civil3D-MCP-Plugin/Civil3DCompatibility.cs` (`ResolveProperty`, métodos cacheados).
- **Qué:** si `GetProperty`/`GetMethods` no encuentra el miembro en el tipo concreto, busca también
  en las interfaces implementadas.
- **Por qué:** se hizo como hipótesis para el bug de parcelas — **resultó no ser la causa** (ver
  10-07: los métodos directamente no existen). Se deja porque es inocuo y podría explicar el bug de
  `civil3d_standards` (nombres de estilo en `null`), todavía sin verificar.
- **Estado:** compila. Sin prueba en vivo que demuestre que arregle algo.

---

## Pendientes

- Repetir con este fork las etapas 2–5 de la prueba del loteo que se hizo con HMK (datos en
  `app-itza2026/docs/itza360-agentica/archivos/loteo/`) para comparar gratis vs. pago.
- Faltantes detectados vs. HMK: ejecutor de C# en caliente (Roslyn), crear corredor, insertar
  bloques.
- Bug abierto: `civil3d_standards` devuelve `name: null` en estilos.
