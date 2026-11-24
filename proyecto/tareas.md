# Tareas — ITZA360 Agéntica (civil3D-mcp-itza)

> Movido desde `app-itza2026/docs/tareas.md` el 11-05. Rutas relativas a `proyecto/`.

## Investigar agentes de IA con control de computadora conectados a Civil 3D / Dynamo ("ITZA360 Agéntica")

**Por qué:** Surgió en la reunión "9-15 Itza Multiagentica" (2026-09-15) como línea de investigación
nueva, en paralelo al proyecto final de Mino/Pablo (visualización y gestión de datos para obras de
infraestructura vial/urbana). La idea es explorar si se puede automatizar el proceso interno de
diseño en Civil 3D (y más adelante Revit) conectando un agente de IA a Civil 3D.

**Detalle completo** en
[`investigacion.md`](investigacion.md)
(secciones 7 y 9) y transcripciones de las reuniones con Joaco en [`reuniones/`](reuniones/).
La vía real no es computer-use ni Dynamo sino **MCP**: un plugin local expone la API de Civil 3D
como tools que el agente invoca en lenguaje natural.

**Estado (2026-09-25):**
- **Herramientas:** HMK CAD Pilot (comercial, $99/año — ITZA **no** lo va a pagar; trial hasta
  ~2026-10-05) y **Civil3D-mcp de Sacred-G** (gratis, 219 operaciones) como base elegida, con fork
  propio a futuro ("sacred-g-itza"). El repo chico que se probó primero quedó descartado.
- **Prueba completa hecha con HMK (2026-09-25):** loteo con datos inventados, en 5 etapas —
  relevamiento, diseño del loteo + 78 parcelas, obra lineal de la calle A, red pluvial, alumbrado y
  arbolado. Todo verificado a mano. Hallazgos: pregunta ante ambigüedades; lo complejo lo resuelve
  programando C# dentro de Civil 3D (una vez colgó el programa); su autoverificación falló una vez
  (informó ✅ un criterio incumplido) → la revisión de un ingeniero sigue siendo necesaria.
- **Enviado a Joaco:** informe `informes/Civil3D_MCP.pdf` + `.rar` con los
  dibujos. Le pareció muy bueno.

**Avance (2026-10-07):**
- **Fork propio creado: sacred-g-itza** — carpeta `Documents\GitHub\sacred-g-itza`, repo privado
  [SantiBargas/sacred-g-itza](https://github.com/SantiBargas/sacred-g-itza) (Sacred-G queda como
  `upstream`). Cada cambio se documenta en [`CAMBIOS_ITZA.md`](../CAMBIOS_ITZA.md) de ese repo.
- **Fix de parcelas verificado en vivo:** la API .NET de Civil 3D 2026 no tiene ningún método para
  crear parcelas → se reescribió por COM (como hace HMK). Nueva acción `create_site`. Arreglado
  también perímetro/estilo de parcelas en `null`.
- **Etapa 1 del loteo hecha con sacred-g-itza:** resultados idénticos a HMK (35 puntos, TIN de 48
  triángulos, 38.400 m²) y sin programar código (HMK necesitó código en 4 pasos). Aparecieron 4
  bugs (import de puntos con N/E invertidos, grupo y capa ignorados, carpetas bloqueadas) →
  arreglados y verificados repitiendo la etapa en limpio.

**Avance (2026-10-08):**
- **Etapa 2 del loteo, primera corrida (descartada para rehacerla en limpio):** sin código y sin bugs. Diseño en texto: frenó con 4
  ambigüedades de la normativa (se usaron las mismas interpretaciones que HMK); misma topología
  de calles que HMK (calle B corrida 5 m) y mismos 76 lotes, pero con el espacio verde (3.900 m²,
  10,16 %, vs. 4.017 de HMK) en el punto bajo NE pensando en el desagüe. Dibujo: Site + 78 parcelas + ejes, áreas exactas (38.400,00 m²).
- **Limitación encontrada y cerrada el mismo día:** no había creación en lote (156 llamadas, ~14 min;
  HMK lo hizo en un bloque de código) → nueva acción `civil3d_parcel create_many` en el fork: 78
  parcelas en 1 llamada, rollback verificado en vivo; la 2b rehecha completa en ~2 min. Queda para
  analizar una tool de lote genérica (`civil3d_batch`, ver [`CAMBIOS_ITZA.md`](../CAMBIOS_ITZA.md)).
- **Permisos:** hubo que agregar la regla `mcp__civil3d-mcp` en la config de Claude Code; el fork
  ahora trae `.mcp.json` + `.claude/settings.json` para el equipo.
- **Etapa 2 rehecha en sesión limpia y cerrada (10-08):** diseño propio (sin ver el de HMK): 3 calles
  (N-S en la misma posición que la A de HMK, 2 E-O interiores) y verde como franja de 240 × 16 m al
  norte (3.840 m², 10,00 %). **80 lotes vs. 76 de HMK** (+417 m² vendibles; lotes de 313,5 m² y
  esquinas de 380,75); contra: el verde lineal es peor plaza que la de HMK. Dibujo con tools propias:
  Site + 85 parcelas en **1 llamada de `create_many`** + 3 ejes, `list` = 38.400,00 m², ~5,5 min y
  18 llamadas. 1 error del MCP (`create_many` dependía del orden: Civil 3D forma parcelas por región
  cerrada) → arreglado en el fork y probado en vivo; misma trampa que HMK (`Parcel.Centroid` = 0,0
  en parcelas recién creadas). Comparación completa en la sección 9. Captura OK. De paso, 4 bugs de
  `civil3d_parcel` arreglados y probados en vivo (`report`: unidades, vértices por COM, CSV con coma
  decimal; `lot_line_adjust` nunca funcionó → ahora `NOT_SUPPORTED`; implementarlo de verdad queda pendiente).

**Input de Fabián (reunión rebranding ITZAweb, 2026-10-09):**
- Las etapas 3 (obra lineal de la calle) y 4 (desagüe pluvial) se podrían unificar: la hidráulica se
  modela sobre la calle, y canales/alcantarillas también son obras lineales. La hoja de ruta actual
  igual está bien.
- Agregar una **6ª etapa: preparación de entregables** (etiquetado y armado de planos: cotas,
  características de los elementos, preparar para impresión). Es la que más retrabajo lleva porque
  cada elemento de Civil 3D (parcela, alineamiento, etc.) tiene su propio sistema de etiquetas, no
  compartido entre elementos. Charlar con él qué elementos involucra.
- Cuando se terminen las etapas con sacred-g-itza: mandarle a Fabián un zip con los archivos del
  pago (HMK) y del gratuito para que los revisen.

**Input de Fabián (reunión página web, 2026-10-28)** — fuente:
`Documents\GitHub\itza-web\docs\fuentes\reuniones\10-28\transcripcion.md` [42:40]–[44:28]:
- Hoy están cargados de proyectos hídricos que no usan Civil 3D, por eso no avanzaron con esto.
- Propone encararlo **al revés**: además de que el agente cree los dibujos iniciales, darle un dibujo
  que ya existe y que le haga las **terminaciones** (sobre todo las etiquetas, «la forma de renegar
  con Civil que existe»). Encaja con lo hecho en la etapa 6 del loteo.
- Juntarse **a principios de la semana que viene** (semana del 11-02) a ver cómo hacerlo y aplicarlo
  en un par de proyectos reales que tienen.
- Sobre Civil 3D 2027: la IA incorporada «es malísima», no aporta nada.

**Reunión con Joaco (2026-11-05)** — [`reuniones/2026-11-05/RESUMEN.md`](reuniones/2026-11-05/RESUMEN.md).
Reunión semanal fija desde ahora. Checklist (orden del [`plan-de-trabajo.md`](plan-de-trabajo.md)):

- [x] **Plan de trabajo** para las reuniones semanales (11-16).
- [x] **Automatizar el ciclo de Civil 3D** (11-16): [`scripts/civil3d.ps1`](../scripts/civil3d.ps1)
  abre/cierra/recompila sin pasos a mano; el plugin carga solo desde el bundle (sin `NETLOAD`).

**Semana 1 — Superficies** (11-16, dibujo `pruebas/superficies/superficies-semana1.dwg`)
- [x] Importar puntos (PNEZD, grupo) y crear TIN — exacto (48 triángulos, 38.400 m²).
- [x] Cotas en un punto, estadísticas, curvas de nivel (`extract_contours`, `contour_interval_set`).
- [x] Línea de quiebre y límite exterior.
- [x] Volúmenes (total, por región, reporte, comparación) — exactos al m³ contra cálculo a mano.
- [x] Flujo de drenaje (recorrido del agua + caudal por método racional).
- [x] Arreglar faltantes (probado en vivo 11-16, detalle en [`CAMBIOS_ITZA.md`](../CAMBIOS_ITZA.md)):
  grupo de puntos, estilo, análisis de cotas/pendientes/orientación, etiquetas de superficie,
  muestreo a lo largo y transecta, capa por defecto, texto del reporte. Cuencas: la API no las
  calcula → error claro en vez de falso éxito.
- [x] Superficie desde nube de puntos `.las` (sin API: se lee el archivo; filtra clases, ralea en
  grilla). Probado con un `.las` sintético; falta uno real de ITZA (y LAZ no se lee).
- [ ] Superficie desde DEM (`create_from_dem`): falta un archivo de prueba.
- [x] Probar `drawing new` desde la pestaña «Inicio» — funciona (11-16).
- [ ] **Versión:** decidido y hecho el 11-20: una DLL por versión (2026 .NET 8 + 2027 .NET 10) en un solo
  bundle; probado en vivo en 2027 (barrido de tools OK, fix de redes). Falta: probar la DLL de 2026 tras
  los cambios, `check_surface`/`check_alignment` en 2027, correr `build-kit.ps1` entero y actualizar la guía. Detalle en
  [`CAMBIOS_ITZA.md`](../CAMBIOS_ITZA.md) (10-26, estado 11-20).

**Semana 2 — Corredores**
- [ ] Obra lineal completa sobre una calle real de ITZA. (11-20: armada de cero la «Calle 3B» del
  loteo — eje, rasante, ensamblaje, corredor, superficies y volúmenes ✅; falta una calle real de ITZA.)
- [x] Ensamblajes: **elige del catálogo** ✅ y **usa uno existente** ✅; **diseñar uno nuevo** sin
  probar (no hay listado de catálogo ni import de `.pkt`). Detalle en [`CAMBIOS_ITZA.md`](../CAMBIOS_ITZA.md) 11-20.
- [ ] Arreglar: `profile create_layout` (perfil vacío), `create_subassembly` ignora `subassemblyName`,
  `assembly get` demasiado verboso.
- [x] Probado 11-20: secciones (líneas + vistas) y `target_mapping` get/set.
- [x] Arreglado 11-20: `get_section_data` (devolvía vacío) y `create_sample_lines` (faltaba la estación final y
  fallaba un 2.º grupo por nombres duplicados). Probado en vivo.
- [ ] `target_mapping_set` por lado (`subassemblyName`): plugin OK (`get` ya lo devuelve), pero falla el set: falta
  declarar `subassemblyName` en los schemas de `targets` de `corridorDomain.ts` (~líneas 267 y 670; zod lo descarta).
  Siguiente: agregarlo → `npm run build` → `/mcp` Reconnect → probar `Talud-Left` → `Calle 3 - Top` y verificar que
  `Talud-Right` queda en `Terreno Natural`; revertir. Dibujo abierto: `corredores\calle3-semana2.dwg` (sin guardar, no se modificó nada).
- [ ] `scripts/civil3d.ps1 -Compilar` aborta con «acad.exe sigue vivo» la primera vez (repetir anda).
- [ ] Probar: regiones (`region_add`/`edit`), estilos del corredor.

**Semana 3 — Comandos empaquetados**
- [ ] Lista de comandos mínimos que sí o sí tienen que andar.
- [ ] Una orden → una sola aprobación (`civil3d_batch` o aprobación por paquete).

**Semana 4 — Estilos y presentación**
- [ ] Crear/editar estilos de objeto y etiqueta (colores, grosores), guardarlos e importarlos.
- [ ] Láminas (layouts).
- [ ] Caso inverso: terminar (etiquetar) un dibujo hecho por ITZA.

**Semana 5 — Tuberías + manual**
- [ ] Red de tuberías sobre un caso real: relevar qué falta.
- [ ] **Manual de comandos v1.**

**Después**
- [ ] PoC motor de cálculo → Civil 3D: SWMM → `civil3d_pipe`; luego HEC-RAS.
- [ ] Plataforma que integre motores + instalable del MCP (idea).

**Organización (Joaco)**
- [ ] Repo central en GitHub de ITZA (espera cuenta de Joaco).
- [ ] Reunión con Pablo.
- [ ] Contrato / resguardo del código en ITZA.
- [ ] Reunión con Mino y Nacho (estructura legal).

**Qué hacer ahora:**
00. **Acciones rotas (11-20):** ✅ `detention stage_storage` arreglada (área/volumen exactos desde el TIN, `seedPoint` opcional; 2026 y 2027). `grading criteria_list` arreglada (4 criterios del «Basic Set»). `grading group_create` **no es viable** por COM ni .NET (no existe `GradingGroup` en ninguna API; solo el diálogo `AeccCreateGradingGroup`), así que todo el dominio de grupos/gradings queda pendiente; ver `CAMBIOS_ITZA.md`, sección 10-26. Vitest: correr desde `C:\` en mayúscula (pasan 472/473; falla `security_boundary` por `LasReader.cs:40`).
0. **Tareas de la reunión del 11-05** (arriba), empezando por plan de trabajo, versión y estilos.
1. **Esperar el caso real de Joaco:** superficie de canales + generar una sección, con las
   secciones de referencia en PDFs viejos.
2. ✅ **Loteo con sacred-g-itza: etapas 1–6 hechas (10-13).** Etapa 5 (alumbrado y arbolado): 20
   columnas + 128 árboles con `block_insert_many`, igual a HMK en resultado y mejor en proceso (fila 5).
   Etapa 6 (entregables, pedido de Fabián; HMK no la hizo): 4 láminas A3 + PDF con etiquetas nativas y
   estilos propios en castellano, programados casi enteros en el fork en la sesión (fila 6). Archivos
   en `pruebas/loteo/mcpitza-loteo/` (los de HMK en `pruebas/loteo/hmk-loteo/`). **El fork se renombró
   `civil3D-mcp-itza` el 10-13** (GitHub `SantiBargas/civil3D-mcp-itza`, carpeta
   `Documents\GitHub\civil3D-mcp-itza`); en lo escrito antes de esa fecha figura como sacred-g-itza.
   El PDF de la etapa 6 ya quedó pulido (coma decimal, etiquetas del borde reubicadas, estilo de
   descarga); lo que queda abierto está en Pendientes de [`CAMBIOS_ITZA.md`](../CAMBIOS_ITZA.md).
3. **Próxima tarea — enviar a Fabián y a Joaco los `.rar`:** `pruebas/loteo/hmk-loteo/` (ya existe
   `Civil3DMCP_loteo_HMK.rar`, el que recibió Joaco) y un `.rar` nuevo de `pruebas/loteo/` entera, que
   incluye `mcpitza-loteo/` (6 etapas + `loteo-prueba-sacredgitza-etapa6.pdf`), los datos comunes
   (`normativa.md`, CSV) y el README. Los `.dwg` no están en git: el `.rar` se arma desde la máquina local.
4. **Siguiente paso (plan del 10-13): exprimir 15 días más de prueba gratuita de HMK y después
   repetir con civil3D-mcp-itza.** Santi activa una prueba nueva de HMK CAD Pilot por 15 días. Antes de
   activarla, dejar todo preparado para no gastar días del trial:
   - **Mensura:** preparar bien los archivos de entrada (relevamiento, datos del título/antecedentes,
     lo que pida el plano reglamentario) en una carpeta `pruebas/mensura/` con su README y consigna,
     igual que se hizo con `pruebas/loteo/`.
   - **Más casos de prueba**, cada uno en su carpeta con datos + consigna escrita de antemano (definir
     con Santi cuáles: candidatos ya mencionados son el caso real de Joaco de canales + sección y la
     6ª etapa de entregables con HMK, que no la hizo).
   - Durante los 15 días: correr con HMK la mayor cantidad posible de pruebas, documentando cada una
     (qué hizo solo, qué resolvió con código, errores, tiempos), con el mismo criterio de la sección 9.
   - Después del trial: repetir las mismas pruebas con **civil3D-mcp-itza** y comparar dato por dato.

**Traspaso para la etapa 5 — ✅ hecha el 10-13, se deja como referencia:**
- **Por qué sesión nueva:** la sesión del 10-13 leyó por accidente el resumen de `etapa5.dwg` de HMK
  (venía en el mismo bloque del informe que la etapa 4), así que no podía proponer a ciegas.
- **Qué es:** alumbrado público y arbolado de las 3 calles según `normativa.md` («Servicios»). Se
  trabaja sobre `loteo-prueba-sacredgitza-etapa4.dwg` y se guarda como
  `loteo-prueba-sacredgitza-etapa5.dwg` (con `civil3d_drawing save` + `saveAs`).
- **Datos ya en el dibujo:** los de la etapa 4 + red "Pluvial" con 13 cámaras (C1-1…C2-5, S-1), 22
  sumideros (SUM-01…22, en el cordón a ±3,5 m del eje) y la descarga D-1; la servidumbre pluvial va por
  x = 5237–5240 entre la Calle 1 y la Calle 2. Calzada 7 m, veredas 4 m, ejes en capa EJES (Calle 3
  x = 5120; Calle 1 N 10064,5; Calle 2 N 10136,5), ochavas de 4 m.
- **Regla de la prueba:** propuesta EN TEXTO (posiciones de columnas y árboles, vereda, distancias al
  cordón, símbolos) y esperar el OK; ante ambigüedades frenar y preguntar. Hasta el OK **no leer** la
  fila 5 de la sección 9 (columna HMK), el apartado `etapa5.dwg` del informe (`informes/Civil3D_MCP.tex`
  — ojo, viene pegado al de `etapa4.dwg`), `pruebas/loteo/README.md`, los `loteo-prueba-hmk-*.dwg` ni el
  `.rar`. Después: comparar dato por dato **midiendo los dos dibujos con las mismas tools** (como en la
  etapa 4) y escribir la fila 5, columna sacred-g-itza.
- **Tools nuevas (10-13, compiladas, SIN probar en vivo):** `civil3d_geometry block_define` (símbolo con
  círculos y líneas), `block_insert_many` (hasta 1000 inserciones con una sola aprobación) y
  `block_list` (cantidades y posiciones para verificar vanos). Detalle en [`CAMBIOS_ITZA.md`](../CAMBIOS_ITZA.md).
- **Procedimiento:** antes de usarlas hay que recompilar (Civil 3D cerrado; matar el `acad.exe`
  huérfano sin ventana; `dotnet build -c Release` + `npm run build`; NETLOAD + `/mcp` → Reconnect).
  **Tras cada reapertura, `civil3d_drawing info` antes de la primera escritura** (el 10-13 se abrió por
  error el dwg de HMK y se borró su red en memoria; no se guardó).

**Traspaso para la etapa 4 (para el próximo agente) — ✅ hecha el 10-13, se deja como referencia:**
- **Qué es:** red pluvial del loteo por gravedad hacia el punto bajo (NE), según `normativa.md`
  ("desagüe por cunetas/conductos siguiendo la pendiente natural"). Se trabaja sobre
  `pruebas/loteo/mcpitza-loteo/loteo-prueba-sacredgitza-etapa3.dwg` y se guarda como
  `loteo-prueba-sacredgitza-etapa4.dwg` en la misma carpeta.
- **Datos ya en el dibujo:** TIN "Terreno Natural"; Site "Loteo" con 85 parcelas; ejes de las 3 calles
  en capa EJES (Calle 3 N-S en x = 5120; Calle 1 E-O en N 10064,5; Calle 2 E-O en N 10136,5); la
  Calle 3 tiene alineamiento, rasante (−1,2 %, a nivel del TN), corredor "Calle 3" (sección de 15 m
  con cordón cuneta) y superficies "Calle 3 - Top"/"Calle 3 - Datum". Las Calles 1 y 2 no tienen
  rasante. Ojo con el tramo este de la Calle 1: termina contra el lindero en zona baja (lo mismo
  complicó la etapa 4 de HMK).
- **Regla de la prueba (igual que en las etapas 2 y 3):** propuesta primero EN TEXTO (trazado de
  colectores, cámaras, sumideros, diámetros, criterio de caudal) y esperar el OK; ante ambigüedades
  **frenar y preguntar**. Hasta que Santi apruebe la propuesta **no leer** la fila 4 de la sección 9
  (columna HMK), el apartado `etapa4.dwg` del informe, `pruebas/loteo/README.md`, los
  `loteo-prueba-hmk-*.dwg` ni el `.rar`. Después: comparar dato por dato con HMK y escribir el
  resultado en la fila 4, columna sacred-g-itza.
- **Tools:** `civil3d_pipe` (create/add_pipe/add_structure/size_network/hydraulic_analysis/
  calculate_hgl) + `civil3d_hydrology`. En el mapa de capacidades es el dominio más fuerte del fork,
  pero **no se probó nunca en vivo**: esperar bugs del mismo tipo que los de corredor (métodos
  adivinados por reflexión). Verificar en la metadata antes de corregir.
- **Procedimiento técnico:** Civil 3D lo abre Santi y carga el plugin con `NETLOAD`. Para recompilar
  hay que cerrarlo; si queda un `acad.exe` **sin ventana**, matarlo sin preguntar (permiso permanente).
  Si se cambia `src/` → `npm run build` + `/mcp` → Reconnect. Toda acción que modifica pide
  `civil3d_request_approval` con `parameters` idéntico (incluye `action`); el token es de un solo
  uso y se invalida si el dibujo cambia. Desde el 10-13 el plugin ejecuta todo por
  `Application.Idle` (ver [`CAMBIOS_ITZA.md`](../CAMBIOS_ITZA.md) 10-13); si una tool se cuelga, diagnosticar con
  `dotnet-stack report -p <pid>` antes de tocar código. Pendientes técnicos del fork al final de
  [`CAMBIOS_ITZA.md`](../CAMBIOS_ITZA.md) (entre ellos: `drawing undo` no deshace operaciones del plugin).
3. Sumarle a sacred-g-itza lo que falte según aparezca en la prueba (ejecutor de C#, insertar
   bloques). El trial de HMK ya venció (~2026-10-05).
4. Queda abierto: comparar planes y modelos pagos (hoy ChatGPT y Claude Pro de USD 20).

**Cuándo:** En uso activo.

**Estado:** Prueba con HMK completa (5 etapas); con sacred-g-itza, etapas 1–3 completas (10-08 y
10-13; en la 2, igual o mejor que HMK en dibujar el loteo sin código, diseño distinto; en la 3, obra
lineal 100 % con tools tras agregarle al fork crear corredor y pasar la ejecución a `Application.Idle`
por cuelgues; vista de perfil reubicada con `view_move` y chequeo final del dibujo OK el 10-13). Etapa 4
completa el 10-13: red pluvial 100 % con tools (13 cámaras, 22 sumideros, 35 conductos, verificada con
`hydraulic_analysis`; las dos redes medidas con las mismas tools), igual a HMK en lo hidráulico con
diseño distinto (más caño y menos profundidad) y mejor en proceso; hubo que arreglar/ampliar `civil3d_pipe`
(incluye `add_many` para altas en lote) — resultado en la fila 4 de la sección 9. Sigue la etapa 5
(alumbrado y arbolado); esperando caso real de Joaco.
