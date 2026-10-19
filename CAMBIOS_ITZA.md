# Cambios ITZA sobre Sacred-G/Civil3D-mcp

Registro de todo lo que ITZA cambia respecto del upstream
([Sacred-G/Civil3D-mcp](https://github.com/Sacred-G/Civil3D-mcp), base: commit `9e437a3`).
Este fork es **civil3D-mcp-itza** (repo privado
[SantiBargas/civil3D-mcp-itza](https://github.com/SantiBargas/civil3D-mcp-itza)); se llamó **sacred-g-itza** del 10-07 al 10-13
y las entradas de esas fechas conservan el nombre de entonces.

Formato: una sección por día (MM-DD), lo más nuevo arriba. Cada cambio dice **qué**, **por qué** y
**estado de verificación** (compila / tests / probado en vivo en Civil 3D).

---

## 10-19 (2026) — kit de instalación (doble clic, sin compilar)

- **Por qué:** para que Joaquín (y otros de ITZA sin perfil de programador) instalen el MCP sin Git,
  .NET SDK, `C_References`, terminal ni acceso al repo (que sigue privado).
- **`packaging/civil3d-bundle/PackageContents.xml`** (nuevo): manifiesto de bundle de Autodesk
  (`Platform="Civil3D"`, `R25.1` = 2026, `LoadOnAutoCADStartup`). Copiado a
  `%APPDATA%\Autodesk\ApplicationPlugins\CivilMcpItza.bundle\`, Civil 3D carga el plugin solo al abrir
  (sin `NETLOAD`).
- **`scripts/build-kit.ps1` + `npm run kit`** (nuevo): compila el plugin directo a una carpeta temporal
  (`-o`, así no choca con el lock de `bin\` y Civil 3D puede quedar abierto), arma el bundle (frena si
  aparece alguna DLL que no sea la del plugin: las de Autodesk no se redistribuyen), corre `npm run build`
  + `npm run package:claude` y deja `dist\kit\civil3D-mcp-itza-kit-<versión>.zip` con
  `CivilMcpItza.bundle\` + `civil3d-mcp-itza-<versión>.mcpb`.
- **`packaging/claude-desktop/manifest.json`**: `display_name` → «Civil 3D MCP (ITZA)».
- `package-lock.json` regenerado con `npm install`: estaba desincronizado (`@emnapi/*`) y el `npm ci` del
  empaquetado del `.mcpb` fallaba.
- **Estado:** `npm run kit` OK (zip de 5,6 MB: `.mcpb` + bundle con solo la DLL del plugin). ✅ **Probado en
  vivo** (máquina de Santi): bundle copiado a `%APPDATA%\Autodesk\ApplicationPlugins`, Civil 3D abierto con
  el acceso directo «Civil 3D 2026 - Español Métrico» → **SECURELOAD pregunta** («Archivo ejecutable no
  firmado» → «Cargar siempre»); después el plugin se carga solo (`plugin.log`, puerto 8080, `drawing info`
  OK en metros). El servidor extraído del `.mcpb` arranca, lista 34 tools y `civil3d_health` da
  `connected: true`. ✅ **`.mcpb` instalado en Claude Desktop** (Santi, 10-19): no pide Node.js (Claude
  Desktop trae uno integrado); la ventana «Configurar» deja «Guardar» gris hasta cambiar algo (se cambió
  el puerto HTTP a `3001`, que además evita chocar con el MCP de Claude Code en el 3000); después hubo que
  **prender el interruptor** de la extensión. Pregunta en chat nuevo → respondió `Dibujo1.dwg`, métrico,
  vacío. Guía para usuarios: `app-itza2026/docs/itza360-agentica/informe/GuiaInstalacion.pdf`.
- Ojo en la máquina de desarrollo: con el bundle instalado, **no** hacer además `NETLOAD` de la DLL de
  `bin\` (las dos pelean por el puerto 8080). Sacar el bundle de `ApplicationPlugins` para volver al flujo
  con `NETLOAD`.

---

## 10-13 (2026) — renombre a civil3D-mcp-itza

- Repo de GitHub renombrado por Santi: `SantiBargas/sacred-g-itza` → `SantiBargas/civil3D-mcp-itza`
  (GitHub redirige la URL vieja).
- `package.json` / `package-lock.json`: `name` y `bin` → `civil3d-mcp-itza` (npm no admite mayúsculas);
  `repository`/`bugs`/`homepage` → `SantiBargas/civil3D-mcp-itza`. `scripts/inspect-package.mjs` valida el
  `bin` nuevo (pasa). El servidor MCP sigue llamándose `civil3d-mcp` → las tools no cambian de nombre.
- ✅ **Hecho por Santi con la sesión de Claude Code cerrada** (el MCP corre desde la carpeta y la
  bloquea): carpeta local `Documents\GitHub\sacred-g-itza` → `Documents\GitHub\civil3D-mcp-itza`,
  `git remote set-url origin https://github.com/SantiBargas/civil3D-mcp-itza.git` y MCP re-registrado
  (scope user) con `...\civil3D-mcp-itza\scripts\claude-bootstrap-and-run.ps1`. **Verificado** en la
  sesión siguiente: la carpeta vieja no existe, `origin` apunta a la URL nueva y `claude mcp get
  civil3d-mcp` da «Connected» con la ruta nueva. `NETLOAD` desde ahora:
  `C:\Users\santi\Documents\GitHub\civil3D-mcp-itza\Civil3D-MCP-Plugin\bin\Release\net8.0-windows\Civil3DMcpPlugin.dll`.
  Los `obj\` con la ruta vieja se regeneran en la próxima compilación.

---

## 10-13 (2026) — etapa 6 (entregables: etiquetas y láminas)

Etapa nueva (pedido de Fabián, 10-09; HMK no la hizo): 4 láminas A3 del loteo con etiquetas nativas de
Civil 3D, rótulo genérico y PDF. Antes de programar se verificó todo en la metadata
(`AeccDbMgd`/`AcDbMgd`/`accoremgd` + `Autodesk.AECC.Interop.Land.dll`).

- **`civil3d_standards label_add_many` (nueva, `LabelBatchCommands.cs`):** etiquetas en lote con 1
  aprobación y 1 transacción, API tipada: `parcel_area` (`ParcelAreaLabel.Create`; si la parcela ya
  tiene etiqueta de área se le cambia el estilo en vez de apilar otra), `parcel_segment`
  (`ParcelSegmentLabel.Create(segmento, ratio, estiloLínea, estiloCurva)`; los segmentos se recorren
  por COM `AeccSite.ParcelSegments` porque .NET no los expone; una etiqueta por elemento, ratio al
  medio del elemento según largos acumulados), `pipe` (`PipeLabel.Create(caño, ratio, estilo)`),
  `structure` (`StructureLabel.Create(id, estilo, posición + offset)`). `layer` opcional para congelar
  por ventana gráfica.
- **`label_list_styles` arreglado para `pipe` y `structure`** (devolvía `[]`: nombres de colección
  adivinados) y nuevos `parcel_area`, `parcel_line`, `parcel_curve`: colecciones tipadas
  `Styles.LabelStyles.{Parcel,Pipe,Structure}LabelStyles`. También corrige el estilo que usaba
  `label_add` para caños/estructuras.
- **`civil3d_plan_production` (`LayoutCommands.cs`):** `layout_create` (lámina con
  `PlotSettingsValidator`: dispositivo, papel más cercano a ancho × alto en mm, escala 1:1; borra la
  ventana por defecto y crea ventanas gráficas a escala `1:N` — `CustomScale` = mm por unidad del
  dibujo / N — con capas congeladas por ventana), `layout_add_entities` (líneas, polilíneas, textos
  con alineación y tablas dibujadas como líneas + textos, en una lámina o en `Model`, 1 transacción),
  `layer_list`. **`sheet_publish_pdf` implementado** (antes devolvía error a propósito): `PlotEngine`
  en primer plano (`BACKGROUNDPLOT` 0 mientras dura), varias láminas en un solo PDF, y falla si el
  archivo no quedó escrito.
- **Estado:** compila (0 errores, 0 advertencias), 468/468 tests TS (`tests/sheets_labels.test.ts`,
  4 nuevos), `npm run build` OK.
- **Primera prueba en vivo (sobre `etapa6.dwg`):** ✅ `label_list_styles` (parcelas, caños, estructuras
  con nombre), ✅ `layer_list`, ✅ `label_add_many parcel_area` (85 en 1 llamada) y `pipe` (35 en 1
  llamada). Fallaron 3 cosas:
  1. `layout_create`: `SetClosestMediaName(420, 297, mm)` tira `eInvalidInput` en «DWG To PDF.pc3».
     **Fix:** el papel se elige por nombre canónico (lleva el tamaño: `…_(420.00_x_297.00_MM)`, se
     prefiere `full_bleed`; si solo existe girado se rota 90°) o por `media` explícito.
  2. `label_add_many parcel_segment`: `ParcelSegmentLabel.Create` rechazó el ratio («The ratio should be
     in the range [0, 0]») — el rango no es 0–1 a lo largo del segmento como se suponía. **Fix:** por COM,
     `AeccParcelSegment.SegmentLabels.Add(estiloLínea, estiloCurva, punto)` con el punto medio de cada
     elemento.
  3. `sheet_publish_pdf` sobre el `Layout1` de la plantilla: `eNonePlotDevice` (sin impresora). **Fix:**
     si la lámina no tiene dispositivo, se imprime con «DWG To PDF.pc3» en A3.
  - Agregados: `label_delete_many` (borra etiquetas por handle; valida todo antes de borrar) y
    `namePattern` (regex) en `label_add_many` — las 22 conexiones de sumidero (PS-xx, 3,5 m) quedaron
    etiquetadas y amontonadas, y para las estructuras hay que separar cámaras de sumideros.
  - Visto: después de crear las etiquetas de área, un segundo `parcel_area` sobre la misma parcela la
    restiliza (`restyled: 1`) — o sea que antes las parcelas no tenían etiqueta de área como objeto; el
    número que mostraban viene de otro lado (a confirmar en el PDF).
  - Segunda tanda compilada (0 errores), 469/469 tests, `npm run build` OK; **sin probar en vivo**.
- **Estilos de etiqueta propios (`LabelStyleCommands.cs`):** captura de Santi del espacio modelo tras
  la primera tanda: con los estilos de la plantilla (inglés, acres, «56.00m of 400 mm Concrete Pipe @
  2.00%») las etiquetas son ilegibles a 1:1000 — es el problema de etiquetas que marcó Fabián. Nuevas
  `civil3d_standards label_style_get` (componentes de un estilo con contenido/códigos de campo, altura,
  offsets, visibilidad) y `label_style_create` (`StyleBase.CopyAsSibling` + edición de los componentes
  de texto por nombre: `Text.Contents/Height/XOffset/YOffset/Angle/MaxWidth`, `General.Visible`; si un
  componente no existe, error con la lista y no se crea nada). Compilado (0 errores, 0 advertencias),
  470/470 tests, `npm run build` OK.
- **Segunda prueba en vivo — ✅ todo lo de la tanda 2:** `label_style_get` (los códigos de campo vienen
  localizados: `<[Área de parcela(Usq_m|P2|RN|AP|Sn|OF)]>`, `<[Talud de tubería(FP|…)]>`; alturas en
  unidades del dibujo = metros de papel, 0,0035 = 3,5 mm); `label_style_create` × 4 («ITZA Lote» vertical
  1,8 mm, «ITZA Superficie» 2,5 mm, «ITZA Caño» `P-n Ø L= i=`, «ITZA Cámara» nombre + tapa + fondo sin
  los caños conectados); `label_delete_many` (120 borradas en 1 llamada); `namePattern`; `layout_create`
  (A3 por nombre canónico, 1:1000, 31 capas congeladas en la ventana); `layout_add_entities` (77
  entidades: marco, norte, escala gráfica, notas, cuadro de balance y rótulo como tablas, 1 llamada);
  `sheet_publish_pdf` (PDF escrito y verificado). El nombre del campo de diámetro («Diámetro interior o
  anchura de tubería») se sacó de `C3D\Dynamo\es-ES\Civil3DNodes.xml`, no se adivinó.
- **Bug visto en el PDF de control:** las etiquetas no aparecían en la lámina — la ventana gráfica nueva
  queda con escala de anotación 1:1 y Civil 3D dibuja las etiquetas con esa escala (1,8 mm en el
  modelo). **Fix:** `layout_create` asigna la escala de anotación «1:N» (o `annotationScale` explícita;
  si no existe, una de razón 1/N; si tampoco, la crea) y nueva acción `layout_viewport_scale` para
  reescalar ventanas existentes. **TS:** el `kind` de la exposición no aceptaba `parcel_line`/
  `parcel_curve` (`label_style_*`) → unión de ambos. Compilado (0 errores), 471/471 tests, `npm run
  build` OK.
- **Tercera prueba en vivo:** ✅ `layout_viewport_scale` (la L1 pasó a escala de anotación 1:1000) y
  una lámina de prueba nueva con la escala desde `layout_create` → **las etiquetas aparecen**. ✅
  `label_add_many parcel_segment` por COM: 420 etiquetas en 1 llamada, todas en el punto medio exacto;
  ✅ estilo «ITZA Lado» (solo distancia, 1,5 mm, rumbo e indicador ocultos). Aprendido:
  - En la L1 seguían sin verse porque se congeló `C-PROP-TEXT`: **Civil 3D dibuja el texto de la
    etiqueta en la capa que fija el estilo**, no en la capa de la etiqueta (`ETIQ-*`). Para ocultar
    etiquetas por lámina hay que congelar las capas de texto de Civil 3D (`C-PROP-TEXT`, `C-STRM-*`).
  - Campo de diámetro: ni «Diámetro interior o anchura de tubería» (de Dynamo) ni 5 variantes se
    resuelven (sale el texto crudo). Falta el nombre real.
  - Cada lado compartido entre dos lotes existe dos veces (un polígono por lote en la etapa 2) → 192
    etiquetas duplicadas, borradas con `label_delete_many`.
  - La etiqueta de fondo (28,50, a mitad del lado) choca con la del lote (centrada). Un `yOffset` en el
    estilo del lote se aplica en el sistema del texto girado y empeora el choque (probado y revertido).
- **Cuarta tanda (compilada, 0 errores, 472/472 tests, `npm run build` OK; sin probar en vivo):**
  `parcel_segment` con `position` (0–1 a lo largo del lado; `positionMinLength` para aplicarlo solo a
  lados largos) y **descarte de lados duplicados** (mismo punto medio y largo → `duplicates`); la
  respuesta de `label_add_many` trae como máximo 50 ítems (`itemsOmitted`) — 420 ítems pasaban el
  límite de la respuesta MCP; `label_style_get` lee también componentes `TextForEach`/`ReferenceText`
  (para sacar de «Incoming Pipes» el nombre del campo de diámetro); `civil3d_plan_production
  layout_delete`.
- **Cuarta prueba en vivo:** ✅ `TextForEach` → el campo del caño conectado es «Anchura o diámetro
  interior de tubería conectada», y para el caño **«Anchura o diámetro interior de tubería»** (estilo
  «ITZA Colector», 13 etiquetas); ✅ `position`/`positionMinLength` + descarte de duplicados (228
  creadas, 192 duplicadas descartadas, 1 llamada); ✅ `itemsOmitted`; ✅ `layout_delete` (×2); ✅ L1, L2 y
  L3 creadas. **❌ Civil 3D se cerró solo** al crear la L4 con escala 1:1250 (sin excepción en el log):
  la escala de anotación «1:1250» no existía y `SetAnnotationScale` la creaba con
  `ObjectContextCollection.AddContext` dentro de la transacción. Se perdió lo hecho desde el último
  guardado (16:54). **Fix:** ya no se crean escalas; si falta, error con la lista de escalas disponibles
  y no se toca nada.
- **Quinta prueba en vivo — cierre de la etapa 6:** rehecho lo perdido sin problemas (guardando después
  de cada paso). **Segundo cierre de Civil 3D**, esta vez al *guardar* después de crear la L4 a 1:1250 —
  y ahí «1:1250» ya existía (no se creó nada), así que **la causa no es `AddContext`**: los dos cierres
  coinciden con una ventana a escala de anotación 1:1250 que muestra la vista de perfil. Causa de fondo
  sin aislar; a 1:1000 la L4 se creó, se exportó y se guardó sin problemas. Se deja el fix de no crear
  escalas (evita un camino riesgoso igual). Resultado: 4 láminas A3 (`L1 Loteo`, `L2 Pluvial`,
  `L3 Alumbrado y arbolado`, `L4 Perfil Calle 3`) y `loteo-prueba-sacredgitza-etapa6.pdf` (4 páginas).
- **Sexta tanda (pulido del PDF) — compilada y ✅ probada en vivo:** `civil3d_standards
  label_style_edit` (edita un estilo existente; **todas sus etiquetas se actualizan al instante**, sin
  recrearlas — es la operación que más ahorra en la práctica), `civil3d_plan_production
  layout_viewport_freeze` (congela/descongela capas en una lámina existente) y `assembly list` con
  `layers`. Con eso: **coma decimal** en los 5 estilos (`AC` en vez de `AP` en el formato del campo;
  verificado en el PDF: «3840,00 m²»), texto de lote a 1,4 mm, estilo «ITZA Descarga» (sin «F ??»),
  etiquetas de las cámaras del borde este reubicadas a la izquierda (`offsetX` −16, `offsetY` 3/9,5;
  quedan en «estado arrastrado», con línea de referencia y texto más grande — lo define el estilo). El
  ensamblaje está en la capa `0`, igual que la vista de perfil: congelar `0` vacía la L4 → se dejó
  visible y se rotuló «Sección tipo». Archivos movidos a `archivos/loteo/mcpitza-loteo/` y
  `archivos/loteo/hmk-loteo/`.
- **Siguen abiertos:** las bandas de la vista de perfil usan el estilo de la plantilla (inglés, punto
  decimal); falta una acción para mover una etiqueta existente; el «estado arrastrado» de las etiquetas
  no se configura desde las tools. Causa de los cierres de Civil 3D a 1:1250 sin aislar.
- **Pendientes vistos en el PDF de la primera versión (antes del pulido):** en L2 las etiquetas de las cámaras del borde este (x = 5241,5)
  quedan cortadas por la ventana y el cabezal D-1 muestra «F ??» (sin fondo); en L1 algunos «28,50»
  todavía rozan el texto del lote; en L4 se ve el ensamblaje (su capa no está entre las congeladas); no
  hay acción para mover una etiqueta ni para editar un estilo existente (hay que copiarlo).

---

## 10-13 (2026) — bloques (preparación de la etapa 5)

- **`civil3d_geometry`: acciones nuevas `block_define`, `block_insert_many`, `block_list`**
  (`BlockCommands.cs`). Motivo: el fork no tenía forma de insertar bloques (faltante vs. HMK anotado
  en Pendientes) y la etapa 5 del loteo (alumbrado y arbolado) los necesita. Se programó **solo lo
  genérico, sin decisiones de diseño**: la propuesta de la etapa 5 la hace una sesión nueva, sin el
  contexto de HMK.
  - `block_define`: crea la definición con círculos (`x`, `y`, `radius`) y líneas (`x1..y2`) alrededor
    del origen; la geometría va en la capa 0 (toma la capa de cada inserción). Se niega si el bloque
    ya existe (no lo pisa).
  - `block_insert_many`: inserta hasta 1000 referencias (`x`, `y`, `z?`, `rotation` en grados,
    `scale?`) en una capa (se crea si no existe) con **una aprobación y una transacción**; si un ítem
    falla se aborta todo y el error dice cuál.
  - `block_list`: definiciones con cantidad de inserciones por capa y, opcional, posiciones (para
    verificar vanos y distancias después de insertar).
  - API verificada en la metadata de `AcDbMgd.dll` (`BlockReference(Point3d, ObjectId)`, `Rotation`,
    `ScaleFactors`, `BlockTableRecord.GetBlockReferenceIds`, `Circle(Point3d, Vector3d, double)`).
- **Estado:** compila (0 errores), 464/464 tests TS (`tests/geometry_blocks.test.ts`). ✅ **Probado en
  vivo (10-13, etapa 5 del loteo, `loteo-prueba-sacredgitza-etapa5.dwg`):** no hizo falta recompilar
  (el plugin cargado ya las tenía). `block_define` de `COLUMNA-AP` y `ARBOL`; `block_insert_many` de 20
  columnas (con rotación) y 128 árboles, **1 aprobación y 1 llamada por tanda**; `block_list` devolvió
  20/128 por capa y las posiciones exactas.
- **Visto en vivo:** la huella del dibujo (`drawingFingerprint`) **no cambió** después de definir los
  bloques ni de insertar 148 referencias → un token pedido antes de esas escrituras seguiría válido
  después. Ver Pendientes.

---

## 10-13 (2026) — etapa 4 (red pluvial)

Sesión de la **etapa 4 del loteo** sobre `loteo-prueba-sacredgitza-etapa4.dwg` (copia de la 3). Primer uso
en vivo del dominio `civil3d_pipe`.

### `civil3d_pipe catalog_list` / `create`: listas de piezas sin nombre ni piezas

- **Bug (visto en vivo):** `catalog_list` devolvía 6 listas (3 repetidas), todas con `name: null` y
  `parts: []` → `create` nunca encontraba la lista de piezas por nombre.
- **Causas (verificadas en la metadata de `AeccDbMgd.dll`):** (1) `StyleBase` sobrescribe solo
  `set_Name`; el getter está en `Autodesk.Civil.DatabaseServices.DBObject`, así que por reflexión
  `GetProperty("Name")` encuentra la propiedad sin getter y `CivilObjectUtils.GetName` daba `null` en
  **todo estilo** (probable causa también del bug abierto de `civil3d_standards`); (2) se probaban 4
  nombres de colección adivinados y dos apuntaban a la misma (`PartsListSet`); (3) las piezas se
  buscaban en miembros que no existen (`PartFamilies`, `PipeFamilies`…).
- **Fix:** `GetName` lee `.Name` tipado si el objeto es un `Civil.DatabaseServices.DBObject`;
  `EnumeratePartsLists` recorre `CivilDocument.Styles.PartsListSet`; las piezas salen de
  `PartsList.GetPartFamilyIdsByDomain(Pipe|Structure)` + `PartFamily[i]`. `catalog_list` agrega
  `pipeParts` y `structureParts` (`parts` queda igual). **TS:** `size_network` elige solo entre
  `pipeParts` (antes podía elegir una cámara por el número del nombre) + 2 tests
  (`tests/pipe_size_network.test.ts`).
- **Estado:** compila (0 errores), 446/446 tests TS. **Probado en vivo ✅:** 3 listas (Storm Sewer,
  Sanitary Sewer, Estándar) con nombre, piezas separadas, sin duplicados; `create` de la red "Pluvial"
  con "Storm Sewer" funcionó.

### Primer armado de la red en vivo (13 cámaras + 12 caños con `add_structure`/`add_pipe`) — OK

- `add_structure` y `add_pipe` (con `startPoint`/`endPoint` = cota del **eje** del caño + estructuras)
  funcionaron a la primera; pendientes y elevaciones respetadas.

### Segunda tanda (compilada, 451/451 tests TS) — probada en vivo ✅ (13-10, salvo `size_network` y `get_structure_properties`)

- En vivo: cabezal "D-1 Descarga" creado con `name`; caño "P-13 Descarga" con `name`;
  `hydraulic_analysis` en métrico con caudal por tramo (capacidades 0,19–1,01 m³/s, velocidades
  1,4–2,8 m/s, cámaras con sus caños); `calculate_hgl` encontró la descarga y recorrió los 13 caños
  (0 sin alcanzar, 0 en carga). Detalle menor visto: en el cabezal informa `sumpElevation` −0,69 y
  `rimElevation` 95,506 (Civil 3D lo acomoda al caño; el sump no aplica a cabezales → debería ir `null`).

- **`add_structure` con cabezal (headwall):** fallaba con "The structure's type should be junction":
  se aplicaba siempre `RimToSumpHeight`, que solo admiten las cámaras. **Fix:** solo si
  `PartType == StructJunction`.
- **`add_structure` / `add_pipe`:** parámetro opcional `name` (antes quedaban "Structure - (n)").
- **`hydraulic_analysis` / `calculate_hgl` / `get_structure_properties` reescritos con la API tipada**
  (`Network.GetPipeIds/GetStructureIds`, `Pipe.StartPoint/EndPoint/InnerDiameterOrWidth/MinimumCover/
  Start-EndStructureId`, `Structure.ConnectedPipe(i)/PipeInvertDepth(i)/GetConnectedPipeNames`,
  verificado en la metadata). Bugs que tenían (vistos en vivo): tomaban `Pipe.Slope` (fracción) como
  % → capacidad ~10 veces menor y todo "FAIL"; informaban como invert la cota del eje; cámaras sin
  caños conectados; unidades y mínimos imperiales fijos (2 ft/s, 0,5 %, cfs) aunque el dibujo sea
  métrico; el HGL ordenaba los caños por cota en vez de recorrer la red.
  Ahora: invert = eje − D/2; pendiente en % desde los inverts; unidades según `INSUNITS` (métrico:
  m³/s, m/s, mínimos por defecto 0,3 %, 0,9 m/s, 5 m/s, tapada 1,0 m — los manda el plugin, TS ya no
  fija defaults imperiales); velocidad a tirante normal (sección circular parcial); control de tapada
  (`Pipe.MinimumCover`); pendiente inversa; `perPipeDesignFlows` (caudal por tramo, también reenviado
  por el workflow `pipe_network_design`). HGL: recorre desde las descargas aguas arriba por
  `EndStructureId`→`StartStructureId`, tirante normal por tramo + pérdida por fricción a sección llena;
  **ignora pérdidas en cámaras** (simplificación, se informa en `method`).
- **Tercera tanda — bajas de `civil3d_pipe` (compilada, 457/457 tests TS; `delete` probado en vivo ✅
  13-10: borró 13 caños + 14 estructuras + la red; `delete_pipe`/`delete_structure` sin probar en vivo):**
  `delete` (red entera: caños, estructuras y la red), `delete_pipe` (desconecta ambos extremos con
  `Pipe.Disconnect` y borra), `delete_structure` (se niega si todavía tiene caños conectados). La API
  no trae `Network.Remove*` (verificado en la metadata) → `Erase()` de AutoCAD. Las tres exigen
  aprobación (`capabilities: ["delete"]`); tests en `tests/pipe_delete.test.ts`. Motivo: rehacer la
  red pluvial de la etapa 4 con tapada medida al extradós (la primera versión quedó con 0,89–0,93 m
  por no contar el espesor de pared) y con nombres legibles. Cierra parte de "Altas/bajas pendientes".
- **Cuarta tanda — `civil3d_pipe add_many` (compilada, 460/460 tests TS; probada en vivo ✅ 13-10:
  22 sumideros + 22 conexiones Ø400 en 1 aprobación y 1 llamada, nombres y cotas respetados).** Detalles
  menores vistos: en la respuesta las estructuras salen con `connectedPipes: []` (se serializan antes de
  crear los caños); `hydraulic_analysis` marca FAIL una tapada de 0,5999 contra mínimo 0,6 (comparar con
  tolerancia, p. ej. 1 mm). Alta
  en lote de estructuras y caños de una red con **una sola aprobación** y **una transacción** (si un
  ítem falla se aborta todo y el error dice cuál: `structures[i]`/`pipes[i]`). Primero las
  estructuras, después los caños; los caños pueden referenciar estructuras creadas en el mismo lote o
  ya existentes. Mismos campos que `add_structure`/`add_pipe` (incluye `name`); máx. 200 por lista.
  Plugin: `addPartsToNetwork`. Tests en `tests/pipe_add_many.test.ts`. Motivo: los ~20 sumideros de
  la etapa 4 eran ~80 llamadas; precedente `civil3d_parcel create_many`. Es la versión acotada (un solo
  dominio) del `civil3d_batch` genérico que sigue en Pendientes.
- **`size_network`:** `parsePartDiameter` leía "1,050 mm Concrete Pipe" como 1 y sin pasar a metros
  → ahora separador de miles, mm/cm → m, y descarta secciones no circulares ("1,000 x 500 mm"). Tests
  nuevos en `tests/pipe_size_network.test.ts`.

---

## 10-13 (2026)

Sesión de la **etapa 3 del loteo** (obra lineal de la Calle 3). Todo verificado en la metadata de
`C_References\AeccDbMgd.dll` antes de programar (lector `System.Reflection.Metadata` en una consola
temporal) y **probado en vivo** sobre `loteo-prueba-sacredgitza-etapa3.dwg` salvo donde se aclara.

### Ejecución del plugin: lecturas/escrituras por `Application.Idle` (cambio de fondo)

- **Problema:** toda operación que dispara un armado diferido de Civil 3D (TIN de una superficie de
  corredor al hacer `Commit` o al leerla, geometría del ensamblaje al leer `Subassembly.Points`) deja
  el contexto de comando de AutoCAD abierto para siempre. Con `ExecuteInCommandContextAsync` el
  plugin esperaba ese cierre con el semáforo tomado → **todas las tools quedaban colgadas** hasta
  cerrar Civil 3D. Diagnóstico: log paso a paso (callback, `Commit` y unlock terminaban en 65 ms) +
  `dotnet-stack report` (ningún hilo .NET en código del plugin, hilo principal libre). Un primer
  arreglo (dar la operación por terminada cuando termina el callback) no alcanzó: AutoCAD no ejecuta
  el siguiente callback de contexto de comando mientras el anterior siga abierto.
- **Fix (`CivilExecution.cs`):** `ReadAsync`/`WriteAsync` corren en el hilo principal desde
  `Application.Idle` (contexto de aplicación), con lock del documento, transacción y control de dibujo
  esperado igual que antes. Timeout de 90 s solo si Civil 3D nunca queda ocioso (libera el semáforo
  sin ejecutar). El dibujo esperado (`AsyncLocal`) se captura antes porque no fluye al evento.
  `drawing new` sigue en contexto de comando.
- **Consecuencia:** en contexto de aplicación `Corridor.Rebuild()` **no** construye las TIN de las
  superficies del corredor (quedaban "sin puntos ni triángulos" — HMK tuvo el mismo síntoma).
  **Fix:** `RebuildCorridorAndSurfaces` rearma el corredor y llama `Surface.Rebuild()` en cada
  superficie del corredor; se usa en todas las escrituras de corredor y en el job de `rebuild`.

### `civil3d_drawing undo` / `redo`

- **Bug:** mandaban `_.UNDO n` con `SendStringToExecute`, que solo encola el comando → respondían OK
  antes de que se ejecutara (un undo de prueba no deshizo el corredor). **Fix:**
  `CivilExecution.RunCommandAsync` encola el comando y espera `CommandEnded`/`CommandCancelled`/
  `CommandFailed` (timeout 60 s); devuelve `completed`. **Probado en vivo: espera bien, pero no
  deshace la última operación del plugin** — las escrituras del plugin no quedan como un paso propio
  en el undo de AutoCAD (ya pasaba antes de este cambio). Ver Pendientes.

### `civil3d_corridor`: acciones nuevas y arreglos

- **`create` (nueva):** eje + rasante + ensamblaje + tramo, frecuencia, `targetSurface` (asigna la
  superficie a todos los objetivos de tipo `Surface`, p. ej. el `TargetDTM` de los taludes) y
  `corridorSurfaces` (`[{name, linkCodes}]`, default `["Top"]`). API:
  `CorridorCollection.Add(name, baseline, alignmentId, profileId)` + `BaselineRegions.Add(name,
  assemblyId, inicio, fin)`; frecuencia en `AppliedAssemblySetting`. Los códigos de enlace recién
  existen tras el primer rebuild → arma, valida los códigos contra `GetLinkCodes()` (error con los
  disponibles) y agrega las superficies.
- **`delete`** (`corridor.Erase()`; `CorridorCollection` no tiene `Remove`), **`surface_add`**,
  **`surface_delete`** (`CorridorSurfaces.Remove`), **`region_edit`** (progresivas, ensamblaje,
  frecuencia). **Todos probados en vivo** (copia de pruebas del dibujo de la etapa 3).
- **Bugs arreglados:** `region_add` buscaba `Baseline.AddRegion` (no existe → `BaselineRegions.Add`);
  `region_delete` adivinaba `RemoveRegion`/`DeleteRegion` (→ `BaselineRegions.RemoveAt`);
  `target_mapping_get` leía `ParameterName`/`TargetId` (devolvía todo `null` → ahora
  `SubassemblyTargetInfo.LogicalName`/`DisplayName`/`SubassemblyName`/`TargetIds`);
  `target_mapping_set` adivinaba `SetTarget`/`AssignTarget` (→ `GetTargets` + `TargetIds` +
  `SetTargets`; falla con la lista de objetivos si el nombre no coincide). **Todos probados en
  vivo.**

### `civil3d_assembly`

- **`create_subassembly` encadenado:** `name` (legible, con `-Left`/`-Right` si `side: Both`),
  `attachTo` y `attachPointIndex`. Antes usaba siempre `AddSubassembly(id)`, que arranca un grupo
  nuevo en el punto base → todo quedaba encimado en el eje. Ahora `AddSubassembly(id, punto)`. Sin
  `attachPointIndex` usa el punto más exterior (y el más alto si empatan). ⚠️ **Esa regla falla con
  `UrbanCurbGutterGeneral`:** el pie trasero inferior (punto 8, a 0,90 m) sobresale más que el borde
  superior trasero (punto 3 `Back_Curb`, a 0,60 m) → pasar siempre `attachPointIndex` en cordones.
- **Parámetros de subensamblaje:** se asignaban como propiedades .NET y se **ignoraban en silencio**
  (los valores viven en `ParamsDouble/Long/Bool/String`, por `Key`). Ahora se escriben ahí con el
  tipo correcto (los `JsonElement` se desenvuelven); si algún parámetro no existe, la transacción se
  aborta con la lista (antes solo lo hacía `edit`).
- **`subassemblyType`** tiene que ser la clase .NET completa del catálogo (`Subassembly.BasicLane`,
  no `BasicLane`): ver `DotNetClass` en los `.atc` de `C:\ProgramData\Autodesk\C3D 2026\esp\Tool
  Catalogs\Road Catalog`, donde también están las `Key` de los parámetros. `LaneOutsideSuper` no
  está en el catálogo .NET métrico (es VBA).
- **`get` / `edit` devuelven `points`** de cada subensamblaje (índice, desplazamiento, cota,
  códigos). `get` fallaba con ensamblajes sin estilo (`style: null` contra un esquema `string`) →
  nullable.
- **`delete` (nueva):** se niega si algún corredor usa el ensamblaje. **Probada en vivo** (borra y se
  niega con el ensamblaje del corredor).

### Segunda tanda (cierre de la etapa 3)

- **`civil3d_profile view_create`:** probaba por reflexión firmas de `ProfileView.Create` que no
  existen → fallaba siempre ("returned null"). Firmas reales en la metadata 2026:
  `Create(alignmentId, insertPosition)` y `Create(alignmentId, insertPosition, name, bandSetId,
  styleId)`. **Fix:** llamada tipada (la de 5 argumentos si hay estilo y banda; si no, la de 2 + se
  le pone nombre y estilo). Probado en vivo (vista "Calle 3 - Perfil longitudinal").
- **`civil3d_corridor compute_volumes`:** invertía desmonte y terraplén (pasaba el corredor como
  superficie base y el TN como comparación). **Fix:** base = TN, comparación = corredor (convención
  de Civil 3D). Probado en vivo: Datum vs. TN → desmonte 596 / terraplén 18 m³.
- **Menores de corredor, todos probados en vivo:** `surface_delete` devolvía el nombre vacío (se leía
  después de borrar); `region_add` acepta `targetSurface` (asigna el TN a los taludes de la región
  nueva); `corridor get` informaba `frequency: 0` (ahora `AppliedAssemblySetting.FrequencyAlongTangents`);
  `region_edit` renombra la región si tenía el nombre automático del plugin (`<ensamblaje> <inicio>-<fin>`).

### Vista de perfil: ubicación automática y `view_move` (probado en vivo 10-13)

- **Problema:** `view_create` en (5000; 9900) dejó la vista y sus bandas encima del loteo.
- **Hecho:** `view_create` acepta omitir `insertX`/`insertY` → ubica la vista a la derecha de todo
  lo dibujado (+20 m); respuesta con `extents`, `overlapsExisting`, `overlappingCount/Types`.
  Acción nueva `view_move` (a un punto o automática). TS + 3 tests (`tests/profile_view_placement.test.ts`).
- **1er intento fallido en vivo:** mover con `TransformBy` no hace nada en una `ProfileView`, y sus
  bandas son entidades aparte (`ProfileDataBandLabelGroup`). **2º intento (compilado, SIN probar en
  vivo):** mover con `Graph.Location`; límites totales = vista + bandas del mismo ancho; el control de
  superposición excluye sus bandas y los `Profile`.
- **2º intento probado en vivo (10-13): falló a medias.** La vista se movió (X bien: minX 5517,9, a la
  derecha del loteo) pero quedó en Y −31…89, lejos del loteo (10000–10160), y con `overlapsExisting:
  true` (1 `ProfileDataBandLabelGroup`). Causa: las bandas se reconocían como propias por tener el mismo
  ancho que la grilla, y las que tienen título (Superelevation, Vertical/Horizontal Geometry)
  sobresalen a la izquierda → se contaban como "dibujo existente". **3er intento (compilado, SIN
  probar):** la banda es propia si es `ProfileBandLabelGroup` con `ViewId` = la vista (`LabelBase.ViewId`,
  verificado en la metadata). Log `[ProfileView]` con límites de grilla, total, destino y dibujo.
- **3er intento probado en vivo: falló.** Log: dibujo sin la vista = (0; 0)–(5323; 10166) → algo del
  dibujo reporta límites hasta el origen (de ahí la Y = 0); total de la vista = (0; −31)–(5498; 239)
  con la grilla en (5343; 0)–(5480; 120) → una banda propia también llega a X = 0, y la vista se corrió
  a X ≈ 10686. **4º intento (compilado, SIN probar):** `TryGetReliableExtents` descarta los límites
  que llegan al origen y miden más de 1 km (regla defensiva; la causa de fondo sigue sin saberse) y
  loguea tipo, handle, límites y `SubEntityCount` de esas entidades y de cada banda propia, para
  saber si hay que filtrar por tipo.
- **4º intento probado en vivo: el log dio la causa.** Los límites falsos son de tamaño nulo en el
  origen: el `Site` "Loteo" (0; 0)–(0; 0) (bajaba la Y del dibujo a 0) y la banda
  `SuperElevationBandLabelGroup` vacía (0; 0)–(1; 0), `SubEntityCount = 0` (corría la vista en X). La
  regla de 1 km no los atrapaba. **5º intento — probado en vivo ✅:** `TryGetReliableExtents` ignora
  los `Site` y los `LabelGroup` con `SubEntityCount == 0`; se sacó la regla de 1 km.
  `view_move` sin punto dejó la vista en (5343; 9994)–(5498; 10264): 20 m a la derecha del ensamblaje,
  base a la altura del loteo, `overlapsExisting: false`. `etapa3.dwg` guardado.
- **`civil3d_drawing selected_objects_info`:** fallaba con objetos sin nombre ni descripción (el plugin
  manda `null` y el esquema TS pedía `string`) → nullable + test. Compilado, sin probar en vivo.

### Estado

- Compila (0 errores, 0 advertencias), 444/444 tests TS (`tests/corridor_create.test.ts`, 14
  nuevos).
- `dotnet-stack` instalado como tool global para diagnosticar cuelgues
  (`dotnet-stack report -p <pid>`).

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

- **Distribución de actualizaciones (10-19):** hoy cada versión = `npm run kit` + volver a mandar el zip
  a mano (kit 1.2.1 mandado a Joaquín el 10-19). Buscar un canal más cómodo (ideas en el punto 20 de la
  sección 7 de `app-itza2026/docs/itza360-agentica/investigacion-mcp-civil3d.md`). Al publicar una
  versión nueva: subir `version` en `package.json` (el bundle y el `.mcpb` la toman de ahí).

- Prueba del loteo: **etapas 1–6 hechas** (10-13; dibujos en
  `app-itza2026/docs/itza360-agentica/archivos/loteo/mcpitza-loteo/`). Abiertos de la etapa 6: bandas
  de la vista de perfil con el estilo de la plantilla (inglés, punto decimal); acción para mover una
  etiqueta existente; configurar el «estado arrastrado» de las etiquetas; causa de los cierres de
  Civil 3D con ventana a escala de anotación 1:1250 sobre la vista de perfil.
- Faltantes detectados vs. HMK: ejecutor de C# en caliente (Roslyn). (Insertar bloques: agregado y
  probado en vivo 10-13.)
- **Huella del dibujo no ve entidades AutoCAD (10-13):** insertar 148 bloques y definir 2 no cambió
  `drawingFingerprint` (parece contar solo objetos Civil 3D). Revisar qué entra en la huella; sin
  verificar en el código.
- Revisar si `civil3d_surface compute_volume` / `civil3d_quantity_takeoff earthwork_summary` respetan
  la convención base = terreno existente, comparación = diseño (`corridor compute_volumes` la tenía
  invertida; arreglado 10-13).
- **Bugs menores vistos en la etapa 3:** `assembly list` devuelve siempre `usedByCorridors: []` (usar
  `GetCorridorsUsingAssembly`); `assembly get` devuelve en `parameters` todas las propiedades .NET
  (ruido) en vez de los `Params*` reales; `alignment create` deja el eje en capa 0; `profile list`
  informa como `layout` el perfil creado desde superficie.
- **`drawing save` deja `unsavedChanges: true` (10-13):** el archivo se guarda bien (verificado por la
  fecha en disco), pero el `drawing info` siguiente sigue informando cambios sin guardar. Puede ser que
  el save no limpie `DBMOD` o que una lectura posterior lo vuelva a encender; no está verificado.
- Log `[ProfileView]` de `view_move`/`view_create` (10-13): dejarlo o bajarlo a solo los límites
  descartados una vez que se use en otros dibujos.
- `civil3d_drawing selected_objects_info` con `name`/`description` nullable: compilado, sin probar en
  vivo.
- `DataShortcutCommands`/`StmCommands` siguen usando `SendStringToExecute` sin esperar el fin del
  comando (el mismo bug que tenía undo/redo) → pasar a `CivilExecution.RunCommandAsync`.
- **`undo`/`redo` no deshacen operaciones del plugin:** las escrituras (por `Application.Idle`) no
  quedan como un paso propio en el undo de AutoCAD; `UNDO 1` deshace otra cosa o nada. Idea: que cada
  escritura corra dentro de un comando registrado del plugin (encolado con `SendStringToExecute` y
  esperado con `RunCommandAsync`) → un grupo de undo por operación. Cuidado: no volver a depender
  del contexto de comando para los armados diferidos (ver 10-13).
- **`civil3d_pipe get` falla en redes sin estilo (10-13):** `ToPipeNetworkDetail` lee
  `Entity.StyleName` y Civil 3D tira `CivilException: Retrieve attribute failed` (visto en la red de HMK
  "Pluvial Loteo"). Envolver la lectura del estilo y devolver `null`.
- **Detalles menores de `civil3d_pipe` (10-13):** `hydraulic_analysis` compara tapada/pendiente sin
  tolerancia (0,5999 < 0,6 → FAIL); los cabezales informan `sumpElevation`/`rimElevation` sin sentido
  (debería ir `null`); `add_many` devuelve las estructuras con `connectedPipes: []`.
- **Altas/bajas pendientes (opción B, 10-13):** `civil3d_parcel delete` y `delete_site`;
  `civil3d_section
  sample_line_group_delete`. Verificar cada método en la metadata antes de programar.
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
