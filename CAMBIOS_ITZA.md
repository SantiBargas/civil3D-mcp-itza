# Cambios ITZA sobre Sacred-G/Civil3D-mcp

Registro de todo lo que ITZA cambia respecto del upstream
([Sacred-G/Civil3D-mcp](https://github.com/Sacred-G/Civil3D-mcp), base: commit `9e437a3`).
Este fork es **sacred-g-itza** (repo privado
[SantiBargas/sacred-g-itza](https://github.com/SantiBargas/sacred-g-itza)).

Formato: una sección por día (MM-DD), lo más nuevo arriba. Cada cambio dice **qué**, **por qué** y
**estado de verificación** (compila / tests / probado en vivo en Civil 3D).

---

## 10-08 (2026)

### `civil3d_parcel report`: unidades de área y vértices

- **Bug 1 — unidades:** `ConvertArea` suponía que el dibujo siempre está en pies (`sqm` = área ×
  0,092903) → en un dibujo métrico, 871 m² salían como 80,92 «sqm». Y el TS mandaba `units: "sqft"`
  por defecto, así que sin pedir nada devolvía m² rotulados como sqft.
  **Fix:** `SquareMetersPerDrawingUnit` lee `INSUNITS` (m, ft, US survey ft, mm, cm, km, in) → el
  área se pasa a m² y de ahí a la unidad pedida (`sqm`, `ha`, `sqft`, `acres`, factores exactos).
  Sin `units`, devuelve la unidad nativa del dibujo (`sqm` en métrico, `sqft` en pies); si el dibujo
  tiene unidades no convertibles y se pidió una, `CIVIL3D.INVALID_INPUT` en vez de un número falso.
  TS: `units: args.units ?? null` (el default lo decide el plugin).
- **Bug 2 — `includeCoordinates` devolvía `vertices: []`:** leía `GetBoundary`/`GetVertices` por
  reflexión, que no existen. **Verificado en metadata:** el `Parcel` .NET no expone ningún contorno
  (ni `Parcel` ni `ParcelSegment` tienen métodos de geometría); el interop COM
  (`Autodesk.AECC.Interop.Land.dll`) sí: `IAeccParcel.ParcelLoops` → `IAeccParcelLoop` →
  `IAeccParcelSegmentElement` con `StartX/StartY/EndX/EndY`. **Fix:** `GetParcelVertices` por COM;
  cada vértice trae `loop`, `x`, `y` (los arcos, solo por su punto inicial). Si COM falla →
  `CIVIL3D.API_ERROR` con el mensaje real.
- **Estado bugs 1 y 2:** compila (0 errores, 0 advertencias), 426/426 tests TS, ✅ **probado en vivo**
  sobre `loteo-prueba-sacredgitza-etapa2.dwg`: `sqm` → Calle 3 S 871,00 y EV 3.840; `ha` → EV 0,384;
  sin `units` → `sqm` nativo; vértices por COM correctos (M1-10: 5 con la ochava
  (5109,6716; 10000)→(5112,5; 10002,8284); EV: 4; Calle 3 S: 10, porque Civil 3D parte sus segmentos
  donde llegan los linderos de los lotes vecinos).
- **Bug 3 — CSV de `report` (`outputPath`):** ignoraba `includeCoordinates` y escribía los números
  con la cultura del sistema (coma decimal en Windows en español → columnas corridas, el mismo bug que
  tenía el export de puntos), sin escapar comas en nombres. **Fix:** números con
  `InvariantCulture`, campos con comillas cuando hace falta, columna `Vertices` (`x y;x y;…`) si se
  pide `includeCoordinates`.
- **Bug 4 — `lot_line_adjust` nunca funcionó:** probaba por reflexión `AdjustLotLine` y `SlideAngle`,
  que **no existen** en Civil 3D 2026 (ni en `AeccDbMgd.dll` ni en `Autodesk.AECC.Interop.Land.dll`;
  los nombres no aparecen en ninguna de las dos) → no hacía nada y devolvía «Lot line adjustment
  attempted». Además suponía el dibujo en pies. **Fix:** `targetAreaSqFt` se convierte a unidades del
  dibujo con `SquareMetersPerDrawingUnit`; si ningún método se pudo invocar → `CIVIL3D.NOT_SUPPORTED`
  explícito. Ajustar linderos de verdad (mover el segmento iterando hasta el área) queda como
  pendiente.
- **Estado bugs 3 y 4:** compila (0 errores, 0 advertencias), ✅ **probado en vivo** sobre
  `loteo-prueba-sacredgitza-etapa2.dwg`: CSV de las 85 parcelas con punto decimal, 5 columnas en
  todas las filas y suma 38.400,000 m²; con `includeCoordinates`, columna `Vertices` correcta (M1-10,
  Calle 3 S, EV); `lot_line_adjust` → `CIVIL3D.NOT_SUPPORTED` y el dibujo sin cambios
  (`unsavedChanges: false`, 85 parcelas). CSV de prueba borrados.

### Trampa de `create_many`: región ya cerrada por segmentos de otras parcelas

- **Qué pasó (etapa 2 del loteo, rehecha desde cero):** lote de 85 parcelas (80 lotes + EV + 4
  calles) → falló la última, `parcels[84] 'Calle 3 N' was not created (84 created before it; the
  batch is not committed)`. El rollback funcionó (`list` → 0 parcelas).
- **Causa:** Civil 3D forma una parcela por **cada región cerrada** de la red de segmentos del Site,
  no por polilínea. La región de Calle 3 N ya estaba rodeada por los lotes de M3/M4 + Calle 1; al
  agregar Calle 2 (anterior en la lista) se cerraron **dos regiones a la vez** (Calle 2 y Calle 3 N).
  Después, el polígono de Calle 3 N solo agregó segmentos superpuestos → ninguna parcela nueva →
  error. El mensaje ("Check that the boundary is closed") confunde: el contorno estaba bien.
  Riesgo adicional: cuando un `AddFromEntity` forma más de una parcela, hoy no está garantizado cuál
  recibe el nombre.
- **Workaround usado:** reordenar para que cada polígono sea el que cierra su propia región (Calle 3 N
  antes que Calle 2) → 85/85 creadas, nombres y áreas correctos.
- **Fix (`ParcelEditingCommands.cs`):** las parcelas que forma cada `AddFromEntity` (0, 1 o varias)
  van a una lista `unclaimed`; cada ítem reclama la que coincide con **su** polígono
  (`MatchParcelToPolygon`: área igual, tolerancia `max(1e-3, área·1e-6)`, **y** caja envolvente
  `GeometricExtents` igual a la del polígono, tolerancia `max(1e-3, lado·1e-6)`; si no hay extents,
  acepta una única coincidencia por área). Antes de cada match se descartan ids que ya no existen
  (una región puede partirse con segmentos posteriores). Lo que sobra al final se devuelve en
  `unassignedParcels` en vez de ignorarse. El error ahora dice el área esperada y lista las
  candidatas sin reclamar (handle, área, extents). `create` (una parcela, por puntos) usa el mismo
  match. Refactor: `ParsePolygon` / `AddPolygonSegments` / `PolygonArea` / `DescribeParcels`; la
  polilínea temporal se borra siempre (antes quedaba en el dibujo si no se formaba parcela).
- **Trampa de la API:** `Parcel.Centroid` devuelve **(0, 0)** en parcelas formadas dentro de la
  transacción todavía abierta (visto con el diagnóstico: `731D area=870.9997 centroid=(0.000,
  0.000)`). El primer intento del fix usaba el centroide y fallaba al empatar Calle 3 S / Calle 3 N
  (misma área) → se cambió a extents.
- **Estado:** compila (0 errores, 0 advertencias), 426/426 tests TS, ✅ **probado en vivo** (dibujo
  métrico nuevo sin guardar, Site "Prueba"): el lote de 85 con el orden que fallaba → `createdCount:
  85`, `totalArea: 38400`, `unassignedParcels: []`; Calle 3 N reclamó la región `731D` formada al
  agregar Calle 2, Calle 3 S la suya (`7321`).

### Configuración compartida para Claude Code

- **`.mcp.json`** (nuevo, raíz del repo): declara el servidor `civil3d-mcp` apuntando a
  `scripts/claude-bootstrap-and-run.ps1` (ruta relativa; el script ya resuelve todo desde
  `$PSScriptRoot`). Quien abra Claude Code en este repo tiene el MCP sin configurar nada (Claude Code
  pide aprobar el `.mcp.json` la primera vez).
- **`.gitignore`**: `.claude` → `.claude/*` + `!.claude/settings.json`, para poder versionar los
  permisos compartidos.
- **`.claude/settings.json`** (nuevo): `{"permissions":{"allow":["mcp__civil3d-mcp"]}}` — lo creó
  Santi a mano porque el clasificador de permisos de Claude Code bloqueó la escritura. Sin esa regla, en modo
  auto se bloquea `civil3d_request_approval` y no se puede modificar el dibujo (pasó en la etapa 2).
  Solo aplica a Claude Code; otros clientes (Claude Desktop, etc.) tienen su propio esquema.
- **Estado:** archivos creados; sin probar todavía abriendo Claude Code en este repo.

### Nueva acción `civil3d_parcel` → `create_many` (parcelas en lote)

- **Archivos:** `ParcelEditingCommands.cs` (`CreateParcelsAsync` + helper `SignedArea`),
  `CommandDispatcher.cs` (método `createParcels`), `src/tools/domains/parcelDomain.ts` (acción
  `create_many`), tests en `tests/parcel_create_many.test.ts` + `domain_manifest.test.ts`.
- **Por qué:** en la etapa 2 del loteo (primera corrida, descartada), 78 parcelas = 156 llamadas (~14 min), porque cada aprobación
  queda atada al fingerprint del dibujo y este cambia con cada parcela. Única desventaja vs. HMK en
  esa etapa.
- **Cómo:** `{ siteName, parcels: [{ name?, points, style?, areaLabelStyle? }] }`, de 1 a 300 por
  llamada; **una sola aprobación** (el hash cubre la lista entera). Antes de crear nada valida todo
  (≥ 3 vértices, área no nula, nombres sin repetir en el lote ni en el Site) → un error de entrada
  nunca deja un lote a medias. Crea en el orden recibido, dentro de una sola transacción, con el mismo
  mecanismo COM que `create`. Si Civil 3D falla a mitad de camino, tira error con índice + nombre y
  la transacción no se confirma. Devuelve `createdCount`, `totalArea` y, por parcela, nombre, handle,
  área y perímetro.
- **Rollback verificado en vivo:** lote de 3 donde la 3.ª repite la geometría de la 1.ª (pasa la
  validación, Civil 3D no puede formar parcela) → error `parcels[2] 'T-FALLA' was not created (2
  created before it; the batch is not committed)` en 246 ms, y `list` devuelve **0 parcelas**: lo
  creado por COM se deshace con la transacción.
- **Caso real verificado en vivo (primera corrida de la etapa 2, descartada — sus dibujos se borraron; la
  corrida válida es la de la sesión limpia, ver «Trampa de `create_many`» arriba):** las 78 parcelas en **una sola llamada**
  (`createdCount: 78`, `totalArea: 38400`, áreas idénticas a la etapa 2 hecha de a una). Etapa 2b
  completa (Site + 78 parcelas + 3 ejes + guardar) en ~2 min y 14 llamadas, contra ~14 min y ~166
  llamadas antes. Dibujo: `loteo-prueba-sacredgitza-etapa2-lote.dwg`.
- **Detalle:** el schema de la tool que ve Claude Code queda cacheado hasta reiniciar la sesión
  (después de `/mcp` reconnect seguía sin `create_many`), pero el servidor ya la acepta.
- **Estado:** compila (0 errores, 0 advertencias), `npm run build` OK, **426/426 tests OK** (4
  nuevos), ✅ **probado en vivo** (Civil 3D 2026, 10-08).

### Prueba del loteo, etapa 2 — primera corrida (descartada)

> Corrida invalidada para repetirla en una sesión limpia (sin contexto del diseño previo ni del de
> HMK). Se deja como historia; el resultado válido de la etapa 2 (80 lotes, 85 parcelas con
> `create_many`, `loteo-prueba-sacredgitza-etapa2.dwg`) está en la sección 9 de
> `investigacion-mcp-civil3d.md`.

Etapa 2b completa sin código y **sin bugs**: Site + 78 parcelas + ejes, áreas exactas (38.400,00 m²).
Detalle en la sección 9 de `investigacion-mcp-civil3d.md` (app-itza2026). Limitación encontrada:
**no hay creación en lote** — el token de aprobación está atado al fingerprint del dibujo, que cambia
con cada objeto creado, así que 78 parcelas = 156 llamadas (~14 min). Ver pendientes.

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

- Repetir con este fork las etapas 3–5 de la prueba (1 y 2 hechas) del loteo que se hizo con HMK (datos en
  `app-itza2026/docs/itza360-agentica/archivos/loteo/`) para comparar gratis vs. pago.
- Faltantes detectados vs. HMK: ejecutor de C# en caliente (Roslyn), crear corredor, insertar
  bloques.
- Bug abierto: `civil3d_standards` devuelve `name: null` en estilos.
- `civil3d_parcel lot_line_adjust`: implementar el ajuste real (hoy devuelve `NOT_SUPPORTED` porque la
  API de Civil 3D no trae el método; ver 10-08).
- **Para analizar después — lote genérico:** una tool `civil3d_batch` que ejecute una lista de
  acciones de cualquier dominio (polilíneas, puntos, parcelas, estructuras…) con una sola aprobación.
  Ventaja: sirve para todas las etapas (ej. etapa 5: 23 columnas + 140 árboles) sin programar un
  `create_many` por dominio. A resolver: (1) la política de aprobación hoy firma `toolName + action +
  parámetros + fingerprint` → habría que aprobar el lote entero (hash de todas las acciones) y que las
  acciones internas no pidan token propio; (2) atomicidad entre dominios distintos (¿una transacción
  para todo o rollback por acción?); (3) límite de tamaño y timeout (`CIVIL3D_COMMAND_TIMEOUT`, 120 s
  por defecto); (4) reporte de resultados por ítem. HMK lo resuelve con `execute_civil_code` (C#
  arbitrario), que es más flexible pero más riesgoso.
