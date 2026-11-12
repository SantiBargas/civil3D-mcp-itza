# Investigación — Agentes de IA conectados a Civil 3D / Dynamo ("ITZA360 Agéntica")

> Investigación de la tarea "Investigar agentes de IA con control de computadora conectados a
> Civil 3D / Dynamo" ([`tareas.md`](tareas.md)), surgida de la reunión "9-15 Itza Multiagentica". Responde
> la duda concreta de esa reunión: ¿un agente conectado a Civil 3D es un automatismo completo que
> entiende cualquier orden, o solo un asistente dentro de un "plumbing" de Dynamo ya armado a mano?
> Actualizado el 2026-09-21 con pruebas reales contra Civil 3D 2026 en una máquina de ITZA.

## 1. Respuesta corta: ni una cosa ni la otra — existe una tercera vía, y ya está madura

La dicotomía de la reunión (agente total vs. asistente dentro de un plumbing fijo) no es cómo
resolvió esto la industria en 2026. El camino real es **MCP (Model Context Protocol)** — el mismo
protocolo que ya usan los clientes de Claude (Claude Desktop, Claude Code) — aplicado a Civil 3D/
AutoCAD/Revit:

- Se instala un **servidor MCP local** (un plugin/add-in dentro de Civil 3D o AutoCAD) que expone
  operaciones reales de la API del programa (crear alineación, perfil, corredor, superficie TIN,
  red de tuberías, punto COGO, capa, bloque, texto, exportar a PDF/LandXML, etc.) como "tools"
  invocables.
- El agente de IA (Claude u otro) recibe el pedido en lenguaje natural, decide qué tools llamar y
  con qué parámetros, y el servidor MCP los ejecuta directo contra el dibujo abierto vía la API
  **.NET real del programa** (Civil 3D API / AutoCAD .NET API) — no mueve el mouse, no interpreta
  píxeles de pantalla.

Esto es estructuralmente el mismo patrón que ya usa `app-itza2026` con Odoo (un helper central que
llama la API real vía XML-RPC, nunca UI-scraping) — la diferencia es que acá el "cliente" que arma
las llamadas es un LLM en vez de código determinístico nuestro.

## 2. Por qué NO es "computer-use" (controlar mouse/teclado)

Es la opción explícitamente descartada por la industria como fallback lento y frágil: *"any app
with a UI is an API, but slow & brittle"* — se usa solo cuando no hay ninguna integración nativa
disponible (ver [arch-control.ifnotfor.com](https://arch-control.ifnotfor.com/)). Para Civil 3D/
AutoCAD/Revit **sí existe** integración nativa vía API — no hace falta caer al fallback de
screenshot + click.

## 3. Qué rol juega Dynamo en todo esto (no es el vehículo del agente)

Dynamo (el lenguaje visual de Civil 3D, hoy sobre CPython nativo desde Civil 3D 2022, antes
IronPython) **sí es automatizable sin abrir la UI** — se puede correr un script `.dyn` ya armado
vía el comando `AECCRUNDYNAMOSCRRIPT` o en batch contra múltiples dibujos (Civil 3D Batch Save
Utility, mejorado en Civil 3D 2025.1+). Pero Dynamo es una herramienta de automatización de tareas
de dibujo en lote (correr un flujo ya diseñado), **no** el canal por el que un LLM controla Civil
3D hoy — ese rol lo cumple MCP + la API .NET directamente, sin pasar por Dynamo.

## 4. Opciones evaluadas

### a) HMK CAD Pilot — producto comercial, $99/año

- MCP server local, **140 acciones reales** (87 generales de AutoCAD + 53 específicas de Civil 3D:
  alineaciones, perfiles, vistas de perfil, superficies TIN/volumen, corredores desde
  alineación+perfil+ensamblaje, líneas de característica, puntos COGO, redes de tuberías).
- Compatible con **AutoCAD/Civil 3D 2023–2027** (no LT). Funciona con Claude Desktop, Cursor,
  ChatGPT/Codex, Google Antigravity.
- Todo corre local — "los dibujos nunca abandonan el equipo del usuario", nada se exporta/
  reimporta.
- **14 días de prueba gratis, sin tarjeta.** Requiere licencia válida de Autodesk (que ITZA ya
  tiene).
- Sitio: [hmktools.com/autocad-mcp](https://hmktools.com/autocad-mcp)
- **ITZA decidió no pagarlo** (sección 7) — se mantiene documentado acá como referencia/fallback y
  porque ya está probado funcionando de punta a punta (ver sección 6).

### b) Civil3D-mcp (Sacred-G) — open source (MIT), el que se adoptó

- Repo: [github.com/Sacred-G/Civil3D-mcp](https://github.com/Sacred-G/Civil3D-mcp)
- **219 operaciones totales**, 34 públicas por default (el resto disponible como alias opt-in).
  Cubre alineaciones (incluida **creación** — con curvas/espirales, no solo polilíneas), superficies,
  corredores, puntos COGO/grupos de puntos, redes de tuberías (gravedad y **a presión**), perfiles,
  parcelas, grading, hidrología, cómputo de cantidades, QC, producción de planos y **estilos de
  etiqueta** — justo el dolor que citó Joaco en la reunión (sin confirmar todavía si lo resuelve
  bien, ver sección 7).
- Repo real y activo: 36 estrellas, 9 forks, MIT, 61 commits, con `CHANGELOG.md` y `ROADMAP.md`.
- Arquitectura: plugin C# .NET 8 (`Civil3DMcpPlugin.dll`, se carga con `NETLOAD`) + servidor
  Node.js/TypeScript, comunicados por **JSON-RPC sobre TCP** (puerto 8080; hay también un bridge
  HTTP opcional en el puerto 3000, no necesario para MCP por stdio).
- **Valida oficialmente contra Civil 3D 2026** — la versión exacta que tiene ITZA.
- Incluye `civil3d_help`: busca en la ayuda de Autodesk instalada localmente y devuelve temas
  citados + videos tutoriales relacionados, directo en el chat.
- Tiene un flujo de aprobación integrado para acciones destructivas: antes de `create`/`delete`/
  etc. hay que pedir un token corto con `civil3d_request_approval` (mismo `toolName`+`action`+
  `parameters`) y recién ahí reintentar con `approvalToken` — bien pensado para no dejar que un
  agente modifique el dibujo sin un paso de confirmación explícito.
- **Probado en vivo el 2026-09-21, funcionó a la primera, sin bugs** (ver sección 6) — muy
  distinto a la experiencia con la otra alternativa gratis que se probó antes (un repo mucho más
  chico, con 35 tools y 4 bugs reales que hubo que arreglar a mano; se descartó como base y no se
  sigue documentando acá).

## 5. Sobre la "divisoria de aguas" que planteó Joaco en la reunión

Preguntaba si conviene separar presentación/documentación (planos, etiquetas — lo más engorroso
hoy) de modelado geométrico (más rígido, límite más claro). Tanto HMK como Sacred-G cubren buena
parte de **ambos** lados en una sola herramienta: modelado (alineaciones, perfiles, corredores,
superficies) y documentación/dibujo (capas, bloques, texto/anotaciones, export a PDF). Sacred-G es
la única de las dos con un módulo de comandos dedicado a **etiquetas** (`LabelCommands.cs`) y
**producción de planos** (`PlanProductionCommands.cs`) — el dolor puntual que citó Joaco ("horas y
horas solo en etiquetas") — pero no se probó todavía si resuelve el caso real; hay que verificarlo,
no asumirlo (ver sección 7).

## 6. Pruebas reales hechas (2026-09-21) — CONFIRMADO: ambas opciones funcionan de punta a punta

Entorno de prueba: Civil 3D 2026 (build 2026.2, AutoCAD core real R25.1.164.0.0 — ojo, el "2026"
comercial no es R25.0 sino R25.1), Windows 11, Node v24.11.0, .NET 8 SDK instalado.

### Intento A — HMK CAD Pilot (comercial, trial 14 días): funcionó

1. Signup en [hmktools.com/signup](https://hmktools.com/signup) (mail, sin tarjeta) → descarga del
   instalador unificado desde `/install` → instalado en `C:\Program Files\HMK Tools\` +
   bundle en `%APPDATA%\Autodesk\ApplicationPlugins\HMKCadPilot.bundle`.
2. Al abrir Civil 3D cargó sin ningún problema de seguridad/confianza — apareció un panel
   "Pilot AI" nuevo en la cinta de opciones con botones Connector/Settings/Chat.
3. "Connector" levanta un listener HTTP local (`HMK_CADCONNECT` → `http://localhost:7900/`). Log en
   `%LOCALAPPDATA%\HMKTools\CadPilot\poc.log` confirmó **140 tools registradas, `Civil=True`**.
4. **Gotcha de Claude Desktop:** la versión instalada de Claude Desktop **ya no lee**
   `mcpServers` del clásico `claude_desktop_config.json` (se probó, no tuvo efecto — el chat
   respondía "no tengo acceso a Civil 3D"). Esta versión usa el sistema nuevo de **Extensiones**
   (ex-DXT, ahora con manifest `"manifest_version"` en vez de `"dxt_version"`/`"mcpb_version"`).
   Solución: Settings → Extensiones → "Instalar extensión sin empaquetar", apuntando a
   `%LOCALAPPDATA%\HMKTools\CadPilot\` con un `manifest.json` armado a mano ahí mismo (reusando el
   `bridge.js` que ya instaló HMK, sin copiar nada):
   ```json
   {
     "manifest_version": "0.1",
     "name": "hmk-cad-pilot",
     "display_name": "HMK CAD Pilot (Civil 3D)",
     "version": "1.0.0",
     "description": "Conector local a HMK CAD Pilot para controlar Civil 3D/AutoCAD desde Claude.",
     "author": { "name": "ITZA" },
     "server": {
       "type": "node",
       "entry_point": "bridge.js",
       "mcp_config": {
         "command": "node",
         "args": ["${__dirname}/bridge.js"],
         "env": { "HMK_CAD_URL": "http://localhost:7900/" }
       }
     },
     "compatibility": { "platforms": ["win32"], "runtimes": { "node": ">=16.0.0" } }
   }
   ```
   Instaló sin problemas, mostró **141 herramientas** con permisos configurables (quedó en
   "Requiere aprobación" por tool — cada acción pide confirmación antes de tocar el dibujo).
5. **Prueba real en el chat de Claude Desktop**, sin ninguna instrucción técnica de por medio: se le
   pidió armar "un lote rectangular, un eje de calle convertido en alineamiento, y una pequeña
   superficie de terreno con curvas de nivel". Claude solo, vía las tools de HMK, creó en el dibujo
   real de Civil 3D: un lote de 100×60 ft, un alineamiento "Calle Principal" a partir de una
   polilínea, 9 puntos COGO en grilla 3×3 con elevaciones (agrupados en un point-group), una
   superficie TIN "Superficie Existente" armada a partir de esos puntos, y un rótulo de texto — todo
   visible y correcto en el dibujo. **Confirma empíricamente la hipótesis central de la sección 1**:
   el agente arma y ejecuta llamadas a la API real de Civil 3D en lenguaje natural, sin plumbing de
   Dynamo pre-armado y sin control de mouse/teclado.

### Intento B — Civil3D-mcp (Sacred-G, gratis): funcionó a la primera

Instalación completa (ver sección 8 para el paso a paso reproducible):

1. `git clone https://github.com/Sacred-G/Civil3D-mcp.git` en `Documents\GitHub\Civil3D-mcp` (renombrado a `sacred-g-itza` el 10-07 y a `civil3D-mcp-itza` el 10-13; fork privado en
   [SantiBargas/civil3D-mcp-itza](https://github.com/SantiBargas/civil3D-mcp-itza)),
   `npm install`, `npm run build` — sin errores.
2. Compilar el plugin C# necesitó armar a mano una carpeta `C_References\` con 6 DLL de Civil 3D
   que el proyecto no incluye (por licencia) — están repartidas en **tres carpetas distintas** de
   la instalación (`accoremgd.dll`/`acmgd.dll`/`AcDbMgd.dll` en la raíz de AutoCAD,
   `AecBaseMgd.dll` en `ACA\`, `AeccDbMgd.dll`/`AeccPressurePipesMgd.dll` en `C3D\`) — el csproj
   valida que las 6 estén juntas en una sola carpeta antes de compilar. Una vez armada esa carpeta,
   compiló sin errores ni warnings.
3. Cargado en Civil 3D con `NETLOAD` apuntando al DLL compilado (no usa bundle de auto-carga como
   otras opciones — hay que cargarlo cada sesión, o configurar el Startup Suite de `APPLOAD`).
   Confirmado: puerto TCP 8080 quedó escuchando en el proceso de Civil 3D.
4. Registrado como servidor MCP en Claude Code con `claude mcp add --scope user --transport stdio
   civil3d-mcp -- powershell -NoProfile -ExecutionPolicy Bypass -File
   "...\Civil3D-mcp\scripts\claude-bootstrap-and-run.ps1"` (scope `user` = config global, no
   queda pegado a ningún repo — el script propio del proyecto (`npm run claude:add`) intenta hacer
   esto solo pero falló por un bug de escaping de `-NoProfile` al correr en cadena `npm → powershell
   → claude`; corriendo el comando `claude mcp add` directo, sin pasar por el script, funcionó
   bien).
5. **Probado con un handshake MCP real** (`initialize` → `tools/list` → `tools/call`) simulando el
   cliente: protocolo `2025-11-25`, capabilities con `tools`+`resources`+`completions`. Los schemas
   de cada tool declaran `"$schema": "http://json-schema.org/draft-07/schema#"` (no draft-04, así
   que no tiene el bug de validación Ajv que sí tenía la otra alternativa gratis).
6. **`civil3d_health`** devolvió `"connected": true`, `"civil3dVersion": "25.1s (LMS Tech)"`,
   `"drawingLoaded": true` — conexión real confirmada sin tocar código.
7. **Creación real de un alineamiento**, con el flujo de aprobación de por medio: se llamó
   `civil3d_request_approval` (`toolName: "civil3d_alignment"`, `action: "create"`, los mismos
   parámetros) → devolvió un `approvalToken` de un solo uso → se reintentó `civil3d_alignment` con
   `action: "create"` + ese token → `"created": true"`, con `handle` real. `civil3d_alignment` con
   `action: "list"` lo confirmó después. **Verificación visual:** se vio en pantalla un alineamiento
   real de Civil 3D — no una polilínea genérica, sino con la curva ajustada automáticamente en el
   vértice intermedio (símbolos de tangente/PI/curva, como cualquier alineamiento armado a mano por
   la UI).

**Nada de esto necesitó ningún parche de código** — a diferencia de la otra alternativa gratis
evaluada antes (35 tools, 4 bugs reales encontrados y arreglados a mano para lograr crear una sola
red de tuberías), Sacred-G conectó y creó geometría real en el primer intento.

### Conclusión práctica

**Las dos opciones funcionan hoy, con creación real confirmada visualmente en ambas.** HMK tiene
fricción de setup mínima (instalador gráfico) pero cuesta $99/año — ITZA decidió no pagarlo.
Sacred-G es gratis, requiere más pasos de instalación (compilar el plugin, armar la carpeta de
referencias, `NETLOAD` cada sesión) pero conectó sin bugs y ya probó crear geometría real
(alineamiento con curva). Es la base sobre la que sigue ITZA (sección 7).

## 7. Próximos pasos

1. **Decisión de estrategia (2026-09-21):** ITZA no va a pagar HMK ($99/año). Se mantiene un fork
   propio de **Civil3D-mcp (Sacred-G)** y se le agrega funcionalidad de a poco, priorizada por
   casos de uso reales de ITZA — no hace falta reinventar nada de lo que Sacred-G ya resuelve (219
   operaciones cubren mucho más que lo que HMK ofrece). No contar con que la comunidad del proyecto
   original contribuya activamente — tratarlo como que lo mantiene ITZA, cualquier aporte externo
   es bonus.
2. Probar el caso de uso más citado en la reunión: generar el plano reglamentario de una mensura a
   partir de datos ya relevados/encarpetados (ver companion en ClickUp "Definir casos de uso
   concretos para Agéntica") — usando `PlanProductionCommands`/`LabelCommands` de Sacred-G.
3. Probar puntualmente si Sacred-G resuelve el dolor de estilos de etiqueta que citó Joaco
   (sección 5) — el módulo existe (`LabelCommands.cs`) pero no se probó todavía.
4. Seguir probando la superficie de Sacred-G más allá de alineaciones: superficies, puntos COGO,
   redes de tuberías (incluida **a presión**, que la otra alternativa no soportaba).
5. Si en algún momento se reconsidera pagar HMK, ya está documentado el setup funcionando
   (Intento A arriba) — solo falta la licencia paga.
6. **HECHO (2026-09-22):** se probó Sacred-G con datos reales de mensura — se extrajeron los
   rumbos y distancias del plano real de `docs/afip api joaco/ejemplo-ph-rocio/Plano_PH
   Ester-Plano.pdf` (11 lados, propietaria Acosta Blanca Marcela), se calculó el traverse
   (cierre de 1.7cm en ~85m de perímetro — correcto) y se intentó crear la parcela real con
   `civil3d_parcel`. Encontrados en el camino: (a) las parcelas necesitan un "Emplazamiento"
   (Site) ya creado en el dibujo — ninguna tool puede crearlo, hay que hacerlo una vez a mano en
   Civil 3D (Prospector → clic derecho en "Emplazamientos" → Nuevo); (b) con el site ya creado,
   `civil3d_parcel create` sigue fallando ("Parcel was not created..."). **Causa raíz confirmada**
   con logging de diagnóstico en vivo (no solo hipótesis): el helper central de reflexión
   `Civil3DCompatibility.ResolveProperty`/`TryInvokeCandidates` (usado por 6 archivos del plugin,
   incluido `ParcelEditingCommands.cs`) solo busca miembros declarados directamente en la clase
   concreta — pero Civil 3D expone propiedades/métodos de dominio (`Parcels`, `Name`,
   `CreateParcelFromEntity`) vía **interfaces implementadas explícitamente**, que `Type.GetProperty`/
   `GetMethods` sin buscar en `GetInterfaces()` no encuentra. Mismo bug de raíz que el de
   `civil3d_standards` (ver abajo). Se aplicó un fix (búsqueda en interfaces como fallback) en
   `Civil3DCompatibility.cs` — **⚠️ hipótesis descartada el 2026-10-07 (ver punto 16):** la causa
   real era que la API .NET de Civil 3D 2026 no tiene ningún método para crear parcelas.
   También se confirmó un segundo bug en `civil3d_standards` (`label_list_styles`/`style_list`): el
   campo `name` de los estilos viene siempre `null` — mismo bug de raíz (reflection sin fallback a
   interfaces), mismo fix aplicable.
7. **RESPONDIDO (2026-09-22):** Santi le escribió a Joaco (WhatsApp) contándole el hallazgo de los
   dos repos (HMK pago + Sacred-G gratis) y le pidió que él mismo tire ejemplos concretos de qué le
   pediría a la IA en el día a día. Joaco respondió: **mensura/parcela NO es prioridad**, lo que sí
   quiere probar es **diseño geométrico: "obra lineal"** (alineamiento → movimiento de suelo →
   triangulación, objeto propio de Civil 3D). Texto completo (mensajes + transcripción del audio)
   en [`reuniones/2026-09-22/TRANSCRIPCION.md`](reuniones/2026-09-22/TRANSCRIPCION.md).
8. ~~Reportar el bug de `civil3d_parcel`/`civil3d_standards` al repo de Sacred-G una vez que el fix
   de interfaces se verifique en vivo~~ — **superado (2026-10-07):** el fix de interfaces no era la
   causa; el fix real (COM) vive en sacred-g-itza (punto 16). Reportar upstream queda opcional.
9. **Nueva prioridad (2026-09-22, por pedido directo de Joaco):** dejar en pausa el bug de parcela
   y armar/probar el flujo de **obra lineal** — `civil3d_alignment` (ya probado sin bugs, crea
   alineamientos con curvas) + `civil3d_profile` + `civil3d_assembly` + `civil3d_corridor` +
   `civil3d_quantity_takeoff` (movimiento de suelo) + `civil3d_surface` (triangulación). Antes de
   armar prompts de prueba, conseguir un caso real de obra lineal de ITZA (hablar con Vino o
   pedirle a Joaco) en vez de inventar datos — mismo criterio que se usó con la mensura real de
   Acosta.
10. **Ampliación (2026-09-25, audio de Joaco):** aclara por qué mensura no es prioridad — es más
    jurídico/de trámite (etiquetas, notas, detalles legales, y termina en el sistema de AFIP/ARCA
    que ya cubre el módulo ATER en pausa) que ingeniería. La parcela **sí** es un objeto de
    ingeniería válido — se usa para diseñar loteos/subdivisiones, no la mensura en sí. Objetos
    nativos de Civil 3D que sí quiere explorar: tuberías, obras lineales, superficies,
    parcelas (como objeto de loteo), emplazamientos, alineaciones, líneas características.
    **Caso de prueba concreto que propuso:** un cliente con un terreno pide un loteo → diseñar la
    división (calles/lotes/manzanas cumpliendo normativa municipal: ancho de calle, longitud
    máxima de manzana) → trazar la obra lineal de la calzada → diseñar alumbrado público,
    arbolado y desagüe → recién al final, la mensura para dividir. Ofreció compartir un documento
    propio con el flujo de trabajo completo de un proyecto de ingeniería integral — **pendiente
    pedírselo**. Texto completo en [`reuniones/2026-09-25/TRANSCRIPCION.md`](reuniones/2026-09-25/TRANSCRIPCION.md).
11. **HECHO (2026-09-25):** mapa de capacidades de HMK y Sacred-G contra el caso loteo + datos de
    prueba inventados (Joaco no tiene caso real a mano) — ver sección 9. De paso, se reinstaló la
    extensión de HMK en Claude Desktop (se había sacado por error). Muestra 88 tools con el
    Connector apagado y 141 con Civil 3D abierto y el Connector prendido: no es una falla.
    Trial vence alrededor del 2026-10-05.
12. **HECHO (2026-09-25):** prueba completa del loteo con HMK en 5 etapas (resultados en la
    sección 9). Se le mandó a Joaco el informe `informes/Civil3D_MCP.pdf` + un `.rar` con los
    dibujos y los datos de entrada.
13. **Charla con Joaco después de la prueba (2026-09-25, tarde):** le pareció muy bueno; la
    prueba usó ~60–70 % de la ventana de 5 h y ~12–15 % del límite semanal del plan de Claude.
    **Próximo caso real:** Joaco arma un `.rar` con un proyecto real — idea inicial: superficie de
    canales + generar una sección, con las secciones de referencia en PDFs viejos (duda: si la IA
    los interpreta bien y ubica todo respecto de lo existente). Queda abierto comparar planes y
    modelos pagos. Mientras tanto: seguir explorando HMK (trial hasta ~2026-10-05) y después
    probar Sacred-G (gratis) para comparar. Transcripción en
    [`reuniones/2026-09-25/TRANSCRIPCION.md`](reuniones/2026-09-25/TRANSCRIPCION.md) (segunda parte).
14. **HECHO (2026-09-25): subido al git** — docs, charlas, datos de entrada e informe. Los `.dwg`,
    `.rar`, `.bak`, `.dwl` y auxiliares de LaTeX quedan fuera por `.gitignore` (solo en la máquina
    local; los dibujos se compartieron con Joaco en el `.rar`). `.vscode/settings.json` hace que
    LaTeX Workshop respete `% !TEX program = xelatex`.
15. ~~Próxima sesión (09-25): repetir el loteo con Sacred-G, explorar HMK, arrancar
    sacred-g-itza~~ — hecho/vencido, ver punto 16.
16. **HECHO (2026-10-07): fork sacred-g-itza + etapa 1 con él.** Repo propio en
    `Documents\GitHub\sacred-g-itza` (privado: SantiBargas/sacred-g-itza; Sacred-G = `upstream`);
    cada cambio en su `CAMBIOS_ITZA.md`. Fixes verificados en vivo: crear parcelas por COM (la API
    .NET no lo permite), acción nueva `create_site`, perímetro/estilo de parcelas, import de puntos
    `pnezd` (N/E invertidos) + grupo, capas que no se creaban, carpetas permitidas (Documents +
    Escritorio + plantillas de Autodesk). Etapa 1 del loteo idéntica a HMK y sin código (sección 9).
    El trial de HMK venció (~2026-10-05).
17. **Próxima sesión:** ver "Qué hacer ahora" de la tarea en [`tareas.md`](tareas.md) — en orden: caso real
    de Joaco (cuando llegue); si no, **etapa 2 del loteo con sacred-g-itza** (primero la propuesta
    de subdivisión en texto contra `pruebas/loteo/normativa.md` y revisarla a mano, después
    dibujar las parcelas con `civil3d_parcel create_site` + `create`), y seguir con 3–5. Los
    pendientes técnicos del fork están al final de `CAMBIOS_ITZA.md`.
18. **2026-10-08: etapa 2 del loteo con sacred-g-itza — a rehacer.** Primera corrida completa (sin
    código, sin bugs) y nueva acción `create_many` probada en vivo, pero se descartó y se borraron sus
    dibujos para repetirla de cero en una sesión limpia (sin conocer el diseño previo ni el de HMK) y
    compararla en serio con HMK antes de cerrar la etapa. Después: etapa 3 (el fork no puede crear
    corredores desde cero — primer hueco real contra HMK).
19. **2026-10-13: etapa 3 del loteo con sacred-g-itza — completa.** Obra lineal de la Calle 3 (eje,
    rasante, ensamblaje de 15 m, corredor con superficies Top/Datum, volúmenes) 100 % con tools,
    después de agregarle al fork `corridor create` y las bajas/ediciones de corredor, encadenar
    subensamblajes y varios arreglos de API. El hallazgo técnico más importante: el plugin se colgaba
    al aparecer superficies de corredor (contexto de comando de AutoCAD que nunca se cerraba) → toda
    la ejecución pasó a `Application.Idle`. Resultado y comparación con HMK en la sección 9 (fila 3).
    Sigue la etapa 4 (red pluvial).
20. **2026-10-19: kit de instalación para usuarios sin perfil de programador + guía.** Para que
    Joaquín pruebe el fork sin Git/.NET/terminal ni acceso al repo (privado): `npm run kit` en
    civil3D-mcp-itza genera `dist\kit\civil3D-mcp-itza-kit-<versión>.zip` con el plugin precompilado
    como bundle de Autodesk (`CivilMcpItza.bundle`, se copia a `%APPDATA%\Autodesk\ApplicationPlugins`
    y Civil 3D lo carga solo, sin `NETLOAD`) + la extensión de Claude Desktop (`.mcpb`, trae su Node).
    Probado de punta a punta en la máquina de Santi (detalle y trampas en `CAMBIOS_ITZA.md` 10-19:
    SECURELOAD pide «Cargar siempre», «Guardar» gris en Configurar → puerto 3001, prender el
    interruptor). Guía con capturas: [`informes/GuiaInstalacion.pdf`](informes/GuiaInstalacion.pdf)
    (LuaLaTeX, estilo del Informe Final del PFC). Mandados a Joaquín el 10-19 (kit 1.2.1).
    **Provisorio:** cada versión nueva obliga a regenerar y volver a mandar el zip. Pendiente
    encontrar un canal de actualización más cómodo. Ideas sin evaluar todavía: carpeta compartida
    de Drive con el kit más reciente + script de actualización con doble clic; organización gratis
    de GitHub con rol Read (`git pull` + recompilar, requiere herramientas de desarrollo); releases
    de GitHub (en repo privado igual requieren acceso); autoactualización de la extensión de Claude
    Desktop (solo aplica a extensiones publicadas en su directorio).
21. **2026-10-26: hay que soportar Civil 3D 2027.** Joaquín avisó que en ITZA usan 2027, así que el kit
    1.2.1 (solo 2026) no le sirve. Civil 3D 2027 = serie R26.0 sobre .NET 10 (2026 = R25.1 sobre .NET 8)
    → hace falta una segunda compilación del plugin. Objetivo: **un solo kit para 2026 y 2027** (un
    bundle con una DLL por versión; el `.mcpb` es el mismo). Santi instala Civil 3D 2027 en su PC para
    sacar las DLL de referencia y probar. Plan detallado y estado en `CAMBIOS_ITZA.md` (10-26).
22. **Reunión con Joaco (2026-10-26).** Propone a futuro un **plugin dentro de Civil 3D** que funcione
    como «enchufe 220»: que saque los objetos (superficies, etc., vía API de Autodesk / acceso directo
    a datos, con el sistema de coordenadas) y los ponga a disposición de un **visualizador** externo
    que funcione como repositorio de objetos. Ejemplo: uno modifica una superficie, guarda, y el
    visualizador se actualiza solo, sin abrir los 5-7 dwg de cada disciplina, que hoy vuelven todo
    pesado. El MCP y el plugin quedan por fuera del visualizador y solo se conectan.
    Aclara que no pide modificar el proyecto del visualizador. **Por ahora: paso a paso, primero el
    MCP**. Joaco quiere instalarlo para probarlo y tiene un conjunto normativo para cargarle a Claude.
    Va a mandar **dos casos de prueba**: uno «muy legal, formalito» y otro de «ingeniería cruda, de
    cálculo». Contexto comercial: ITZA quedó afiliada a la Cámara de la Construcción (Entre Ríos/Santa
    Fe), que ofrece espacio para presentar herramientas. El jueves 10-29, reunión del RIGI en Rosario.
    Idea de demo a futuro: diseñar una calle escribiendo y verla en el visualizador. Texto completo en
    [`reuniones/2026-10-26/TRANSCRIPCION.md`](reuniones/2026-10-26/TRANSCRIPCION.md).
23. **Próximos pasos tras la reunión del 10-26 (pendientes, en orden):**
    1. **Terminar el kit multiversión 2026+2027** (en curso; bloquea todo lo demás): instalar Civil 3D
       2027, compilar la segunda DLL, probar de punta a punta y mandarle el kit nuevo a Joaco.
    2. **Pedirle a Joaco el conjunto normativo** que mencionó, para cargarlo como contexto (al estilo
       del `normativa.md` del loteo). Conviene pedirlo ya para que llegue mientras se termina el 1.
    3. **Correr sus dos casos de prueba** («formalito» e «ingeniería cruda») con el fork cuando lleguen y
       compararlos contra HMK, como se hizo con el loteo.
    4. **Resolver el canal de actualización** del kit (ver punto 20). Con Joaco usándolo va a haber
       versiones seguido.
    5. **Investigar el «enchufe 220»**, sin desarrollar todavía: qué permite la API de Autodesk para
       sacar objetos de Civil 3D al guardar (eventos de guardado, acceso directo a datos, LandXML/IFC).
       Va después del MCP («paso a paso»).
    6. **Demo para presentar** (más adelante): diseñar una calle escribiéndola, para las cámaras de la
       construcción.

## 8. Instalación paso a paso — Civil3D-mcp (Sacred-G) en cualquier máquina

Guía reproducible basada en la instalación real hecha el 2026-09-21.

### Requisitos previos

- Windows 10/11, Civil 3D 2026 (el repo solo valida oficialmente contra esta versión).
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (no alcanza con el runtime).
- [Node.js](https://nodejs.org/) 18+.
- [Git](https://git-scm.com/download/win).
- Claude Code, Claude Desktop, Cursor o Cline como cliente MCP.

### 1. Clonar e instalar el servidor Node

```powershell
cd C:\Users\<usuario>\Documents\GitHub
git clone https://github.com/Sacred-G/Civil3D-mcp.git
cd Civil3D-mcp
npm install
npm run build
```

### 2. Armar la carpeta de referencias de Civil 3D

El proyecto no incluye (por licencia) los ensamblados administrados de Civil 3D — hay que
copiarlos a mano desde la instalación local a una carpeta `C_References\` (default, relativa al
repo) antes de compilar el plugin. Están repartidos en tres carpetas distintas:

```powershell
$refDir = "C:\Users\<usuario>\Documents\GitHub\civil3D-mcp-itza\C_References"
New-Item -ItemType Directory -Force $refDir | Out-Null
$acadRoot = "C:\Program Files\Autodesk\AutoCAD 2026"
Copy-Item "$acadRoot\accoremgd.dll" $refDir
Copy-Item "$acadRoot\acmgd.dll" $refDir
Copy-Item "$acadRoot\AcDbMgd.dll" $refDir
Copy-Item "$acadRoot\ACA\AecBaseMgd.dll" $refDir
Copy-Item "$acadRoot\C3D\AeccDbMgd.dll" $refDir
Copy-Item "$acadRoot\C3D\AeccPressurePipesMgd.dll" $refDir
```

(ajustar `$acadRoot` si Civil 3D está instalado en otra ruta).

### 3. Compilar el plugin

```powershell
dotnet build .\Civil3D-MCP-Plugin\Civil3DMcpPlugin.csproj -c Release
```

Sale en `Civil3D-MCP-Plugin\bin\Release\net8.0-windows\Civil3DMcpPlugin.dll`.

### 4. Cargar el plugin en Civil 3D

En la línea de comandos de Civil 3D: `NETLOAD` → seleccionar el DLL del paso 3. Confirmar que el
puerto TCP 8080 quedó escuchando (`Get-NetTCPConnection -LocalPort 8080` desde PowerShell debería
mostrar el proceso `acad`). No hay auto-carga por bundle — hay que repetir esto cada vez que se
abre Civil 3D, o configurar el Startup Suite de `APPLOAD` para que lo haga solo.

### 5. Registrar el servidor MCP en el cliente

**Claude Code** (scope `user`, config global — no queda atado a ningún repo):

```powershell
claude mcp add --scope user --transport stdio civil3d-mcp -- powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Users\<usuario>\Documents\GitHub\civil3D-mcp-itza\scripts\claude-bootstrap-and-run.ps1"
```

(Correrlo desde Bash, no desde PowerShell: PowerShell se come el `--` y `claude` falla con
`unknown option '-NoProfile'`.)

(el script propio `npm run claude:add` intenta hacer esto automático, pero falló acá por un bug de
escaping al encadenar `npm → powershell → claude`; correr el comando `claude mcp add` directo
funciona bien). Hace falta reiniciar Claude Code para que las tools queden disponibles en la
sesión.

**Claude Desktop:** el repo trae un instalador `.mcpb` (Settings → Extensions → Advanced settings →
Install Extension) — ver el README del repo para el link de la release.

**Permisos (Claude Code, desde 10-08):** agregar `"mcp__civil3d-mcp"` al `permissions.allow` de
`~/.claude/settings.json`. Sin esa regla, en modo auto el clasificador bloquea
`civil3d_request_approval` y no se puede modificar el dibujo. Alternativa sin configurar nada: abrir
Claude Code dentro del repo sacred-g-itza, que trae `.mcp.json` y `.claude/settings.json` (ver
`CAMBIOS_ITZA.md` 10-08).

### 6. Verificar

Con Civil 3D abierto y el plugin cargado, pedirle al agente que llame `civil3d_health` — debería
devolver `"connected": true` y la versión real de Civil 3D.

## 9. Mapa de capacidades — caso loteo (2026-09-25)

Relevado leyendo el catálogo real de cada herramienta (HMK: `%LOCALAPPDATA%\HMKTools\CadPilot\tools-cache.json`,
140 tools; Sacred-G: `src/tools/domains/*.ts` del repo local), contra los pasos del caso que
propuso Joaco ([`reuniones/2026-09-25/TRANSCRIPCION.md`](reuniones/2026-09-25/TRANSCRIPCION.md)). **Todavía no se probó nada en vivo:** esto
dice qué herramienta *existe*, no qué *funciona*. Datos inventados para la prueba en
[`pruebas/loteo/`](pruebas/loteo/).

Leyenda: ✅ hay herramienta directa · ⚠️ parcial o con bug conocido · ❌ no hay herramienta ·
🔧 solo vía el escape hatch de HMK (`execute_civil_code`: C# arbitrario contra la API de Civil 3D).

| Paso del loteo | HMK CAD Pilot | Sacred-G (fork ITZA) |
|---|---|---|
| 0. Terreno: puntos + superficie TIN + curvas de nivel | ✅ `import_points_from_file`, `create_tin_surface`, `add_points_to_surface`, `add_surface_contours` | ✅ `civil3d_point` (import PNEZD), `civil3d_surface` (create, add_points, extract_contours) |
| 1a. Emplazamiento (Site) | ⚠️ solo `list_sites` · 🔧 | ❌ hay que crearlo a mano (ver punto 6 de la sección 7) |
| 1b. Subdivisión: manzanas y lotes (parcelas) | ⚠️ solo `list_parcels`/`get_parcel_info`; crear parcelas 🔧 | ⚠️ `civil3d_parcel` create / `lot_line_adjust` (con área objetivo) / report — **pero `create` falla** (bug de interfaces, fix sin verificar) |
| 1c. Diseñar la subdivisión cumpliendo normativa | ❌ ninguna — la geometría la tiene que calcular la IA | ❌ ídem |
| 2. Eje de calle (alineamiento) | ✅ `create_alignment_from_polyline`, `create_offset_alignment` | ✅ `civil3d_alignment` (con curvas — ya probado sin bugs) |
| 3. Rasante (perfil) | ✅ `create_surface_profile`, `create_layout_profile`, `add_profile_pvi`, `create_profile_view` | ✅ `civil3d_profile` |
| 4. Sección tipo (ensamblaje) | ❌ tiene que existir de antemano (`list_assemblies`) · 🔧 | ✅ `civil3d_assembly` create + subensamblajes |
| 5. Obra lineal (corredor) | ✅ `create_corridor` + `rebuild_corridor` | ✅ desde 10-13: `civil3d_corridor create` (con objetivo de taludes y superficies Top/Datum), `delete`, `surface_add/delete`, `region_add/edit/delete` — antes ❌ no podía crear un corredor desde cero |
| 6. Movimiento de suelo | ✅ `create_volume_surface`, `get_surface_statistics` | ✅ `civil3d_surface compute_volume`, `civil3d_quantity_takeoff earthwork_summary`, `civil3d_corridor compute_volumes` |
| 7. Intersecciones de calles | ❌ · 🔧 | ❌ solo list/get |
| 8. Desagüe pluvial (red de tuberías) | ⚠️ `create_pipe_network` crea la red **vacía**, no agrega caños ni cámaras · 🔧 | ✅ `civil3d_pipe` create/add_pipe/add_structure, `size_network`, `hydraulic_analysis`, `calculate_hgl` + `civil3d_hydrology` — **el más fuerte de los dos acá** |
| 9. Alumbrado y arbolado | ✅ vía bloques: `insert_block`, `array_*`, `offset_entities` (los bloques tienen que existir en el dibujo) | ❌ no puede insertar bloques — solo polilíneas y texto |
| 10. Verificar normativa | ❌ la IA sola, leyendo las medidas con `get_*` | ❌ ídem (`civil3d_qc`/`standards` chequean estilos del dibujo, no ordenanzas) |

### Lectura

1. **Son complementarias, no sustitutas.** HMK cubre la obra lineal (corredor) y el
   equipamiento con bloques; Sacred-G cubre la sección tipo, las parcelas y, sobre todo, el
   desagüe. Ninguna hace el loteo completo sola.
2. **El escape hatch de HMK es su gran diferencia.** Donde le falta una tool, la IA puede
   escribir C# contra la API completa de Civil 3D. Tapa casi cualquier hueco, pero es más frágil
   y riesgoso (código arbitrario sobre el dibujo). Sacred-G no tiene nada equivalente: cada hueco
   es una tool que hay que programar en el fork.
3. **El paso más difícil no es de herramientas, es de razonamiento (1c).** Ninguna "subdivide
   según normativa": la IA tiene que calcular por su cuenta la geometría de calles, manzanas y
   lotes que cumpla todos los parámetros a la vez, y después dibujarla. Es exactamente la duda de
   Joaco ("son muchos parámetros a la vez") y es lo primero que hay que probar.
4. **Backlog para el fork de Sacred-G**, si la prueba confirma que vale la pena, por prioridad:
   (a) verificar el fix de parcelas; (b) `corridor create`; (c) crear el emplazamiento (Site);
   (d) insertar bloques (alumbrado/arbolado).

### Prueba propuesta, por etapas y con las dos herramientas

Con los datos de [`pruebas/loteo/`](pruebas/loteo/), en un dibujo en metros:

1. Superficie TIN desde `puntos-relevamiento.csv` + perímetro del terreno.
2. **Diseño de la subdivisión** según `normativa.md`: primero pedirle la propuesta en texto
   (calles, manzanas, lotes con medidas y áreas) y revisarla a mano contra la normativa; recién
   después, que la dibuje.
3. Eje y rasante de la calle principal → ensamblaje → corredor → movimiento de suelo.
4. Red pluvial hacia el punto bajo (NE).
5. Alumbrado y arbolado con bloques.
6. Entregables: etiquetas y láminas (agregada el 10-13 a pedido de Fabián; HMK no la hizo).

En cada etapa, anotar: si la hizo sola, si necesitó ayuda o si no pudo, y con qué herramienta.

### Resultados en vivo

| Etapa | HMK (Claude Desktop) | Sacred-G (Claude Code) |
|---|---|---|
| 1. Superficie TIN + perímetro | ✅ **Completa (2026-09-25), resultados exactos:** 35 puntos en grupo "Relevamiento", TIN "Terreno Natural" de 48 triángulos (= 6×4×2, correcto), cotas 96,16–100,00, área 38.400 m², perímetro de 800 m como borde exterior. Tomó una decisión propia y la explicitó: no había estilo de curvas cada 0,25 m, así que copió uno existente y le puso principales cada 1 m (avisando que ese valor lo eligió él). **4 pasos sin tool propia, resueltos con `execute_civil_code`:** abrir dibujo nuevo con plantilla, encontrar el nombre del formato de puntos en Civil 3D en español ("PNEZD (delimitado por comas)"), crear el estilo de curvas, cambiar la capa de la polilínea. Extra: detectó que el `QNEW` del usuario apunta a la plantilla imperial. **Chequeo de unidades previo: ✅ bien resuelto** — leyó `INSUNITS`=pies y `MEASUREMENT`=imperial, identificó que el dibujo venía de `_Autodesk Civil 3D (Imperial) NCS.dwt` (y que el problema no es solo la variable, también estilos/etiquetas/escalas), frenó sin tocar nada y propuso abrir uno nuevo con `_Autodesk Civil 3D (Metric) NCS.dwt` en vez del parche rápido. Decisión: seguir en un dibujo nuevo con la plantilla métrica. | ✅ **Completa con sacred-g-itza (2026-10-07), resultados idénticos a HMK:** 35 puntos en grupo "Relevamiento", TIN "Terreno Natural" de 48 triángulos, cotas 96,16–100,00, área 38.400 m², perímetro como polilínea + borde exterior. **100 % tools propias, sin código** (HMK necesitó `execute_civil_code` en 4 pasos). **Pero aparecieron 4 bugs/limitaciones del fork:** (1) `import` en formato `pnezd` **invierte N y E** (lo detectó el control de un punto; se reimportó como `xyzd`); (2) `import` ignora `groupName` → grupo creado aparte con `group_create`; (3) `create_polyline` ignora `layer` (quedó en capa 0); (4) abrir/guardar dibujos fuera de `Documents` está bloqueado (`CIVIL3D_FILE_ROOTS`) → el usuario abrió el dibujo métrico a mano y se guardó en `Documents` + copia acá. No se creó estilo de curvas de nivel (HMK sí, por código). **Los 4 bugs se arreglaron el mismo día y la etapa se repitió en limpio sin ningún workaround** (abrir plantilla, `import pnezd` con encabezado + grupo, polilínea en su capa, guardar acá directo) — mismos resultados. Detalle en `CAMBIOS_ITZA.md` del repo sacred-g-itza. Dibujo: `pruebas/loteo/mcpitza-loteo/loteo-prueba-sacredgitza-etapa1.dwg`. |
| 2a. Diseño de la subdivisión (en texto) | ✅ **Completa (2026-09-25), cuentas verificadas a mano una por una — todas cierran:** 76 lotes en 4 manzanas de 112,5 m (≤120), 3 calles nuevas de 15 m (1 N-S conectora + 2 E-O), plaza de 4.017 m² (10,46 %) en una fracción con frente a 3 calles. Balance lotes 25.201 + verde 4.017 + calles/ochavas 9.182 = 38.400 m². Lotes mínimos de 10 m × 300 m² exactos; los de esquina se agrandan a 12,5 m para absorber la ochava. **Lo más valioso: frenó y marcó 3 ambigüedades reales de la normativa** (conexión directa vs. por red, calles sin salida, cómo se mide la ochava) en vez de asumir, y justificó por qué la interpretación literal hacía la normativa imposible. Sin verificar: su afirmación de que la alternativa con calles N-S da como máximo ~70 lotes. | ✅ **Completa (10-08, sesión limpia), sin tools (razonamiento puro), leyendo solo `normativa.md` + `terreno-poligono.csv`** y con los 5 criterios de HMK fijados de antemano (por eso no hubo ambigüedades que frenar; no encontró otras — la forma del espacio verde no está regulada y se declaró como decisión de diseño). **Diseño propio:** 3 calles nuevas de 15 m — Calle 3 N-S en x 112,5–127,5 (igual que la A de HMK), Calle 1 E-O en y 57–72 y **Calle 2 E-O en y 129–144, interior** (la C de HMK va sobre el lindero norte, de un solo frente) — y el **espacio verde como franja de 240 × 16 m al norte de la Calle 2** (3.840 m² = 10,00 % exacto, una fracción, frente a calle en todo su largo, incluye el punto bajo NE). 4 manzanas iguales de 112,5 × 57 m (≤ 120), todas con lotes, 2 filas de 28,5 m. **80 lotes** = 72 de 11 × 28,5 = 313,5 m² + 8 de esquina de 13,5 × 28,5 − 4 = 380,75 m² → 25.618 m². Calles 3.600 + 3.600 + 1.710 + 8 ochavas × 4 = 8.942 m². Balance 25.618 + 3.840 + 8.942 = 38.400 m². **Vs. HMK (cotejado contra esta tabla y el informe, `etapa2.dwg`):** misma estructura (1 N-S conectora + 2 E-O, 4 manzanas de 112,5 m, 8 ochavas, esquinas ≥ 12,5 m); **+4 lotes (80 vs. 76)** y +417 m² vendibles (25.618 vs. 25.201, 66,7 % vs. 65,6 %); calles 8.942 vs. 9.182 m² (−240: manzanas de 57 m en vez de 60/70); verde 3.840 (10,00 %) vs. 4.017 (10,46 %). Lotes uniformes de 313,5 m² (HMK: 300/350/385, más chicos en el mínimo exacto). **Contra:** el verde es una franja lineal de 16 m, peor como plaza que la de HMK (57,5 × 70 con frente a 3 calles); y el tramo este de la Calle 1 termina contra el lindero en zona baja, igual que la B de HMK que complicó la etapa 4 (la Calle 2 sí drena al verde/NE). Revisado y aprobado por Santi para dibujar. |
| 2b. Dibujar la subdivisión (parcelas) | ✅ **Completa (2026-09-25):** Site "Loteo" + **78 parcelas reales de Civil 3D** (76 lotes nombrados M1-L01…, EV-01, CALLES como un solo polígono de dominio público) + ejes en capa EJES. Áreas según Civil 3D idénticas a la propuesta (total 38.400,00 m², diferencias < 0,00001 m² por redondeo de las ochavas). Captura confirma: parcelas numeradas y con sombreado de estilo, ochavas visibles, distribución igual a la 2a. **Casi todo con `execute_civil_code`:** tools propias solo para capa, polilíneas de ejes, cambio de capa y `list_parcels`. Site con `Site.Create`; parcelas **vía COM** (`ParcelSegments.AddFromEntity` sobre 78 entidades auxiliares en capa LOTEO-SEG, que Civil 3D consume al convertirlas) porque, según afirma, **la API .NET de Civil 3D 2026 no tiene método para crear parcelas** — sin verificar, pero consistente con el bug de `civil3d_parcel create` en Sacred-G (punto 6 de la sección 7) y es la pista principal para arreglarlo. **Se autocorrigió:** el primer nombrado dejó todo como M1 (centroides sin actualizar recién creadas), lo detectó, cambió a clasificar por extensión geométrica y verificó con `list_parcels`. Detalle menor: las etiquetas de la plantilla muestran número, no área. | ✅ **Completa (10-08, sesión limpia), 100 % tools propias, sin código:** `civil3d_parcel create_site` (Site "Loteo"), **`create_many` con las 85 parcelas en una llamada** (80 lotes M1-01…M4-20, EV, Calle 1, Calle 2, Calle 3 S, Calle 3 N — las calles separadas, no un solo polígono), 3 ejes con `civil3d_geometry create_polyline` en capa EJES, `civil3d_drawing save`. Coordenadas calculadas antes con un script (áreas chequeadas: 85 polígonos = 38.400). **`civil3d_parcel list` confirma:** 85 parcelas, 72 × 313,50 + 8 × 380,75 + EV 3.840,00 + Calles 3.600 + 3.600 + 871,00 × 2 = **38.400,00 m²** (diferencia < 0,001 m² por redondear las ochavas a 4 decimales). **~5,5 min y 18 llamadas al MCP** (HMK: tiempo y llamadas no registrados). **1 error del MCP:** el primer `create_many` falló en la parcela 85 (Calle 3 N) — Civil 3D forma una parcela por *región cerrada*, no por polilínea, y la región de Calle 3 N ya la había cerrado Calle 2; rollback OK (0 parcelas). Se resolvió reordenando (Calle 3 N antes que Calle 2) y **después se arregló en el fork** (cada ítem reclama la parcela que coincide con su polígono por área + caja envolvente; probado en vivo con el orden que fallaba → 85/85). En el camino apareció la **misma trampa que tuvo HMK**: `Parcel.Centroid` vale (0, 0) en parcelas recién creadas — HMK la resolvió con la extensión geométrica, y el fix terminó usando lo mismo. **Vs. HMK:** HMK hizo 78 parcelas casi todo con `execute_civil_code` (tools propias solo para capa, ejes y `list_parcels`) y se autocorrigió un nombrado erróneo; acá 85 parcelas con tools propias y nombres correctos verificados. **Captura confirma** la distribución de la 2a: 4 manzanas de 2 × 10 lotes (1–80, esquina más ancha contra la Calle 3), 8 ochavas, EV (81) como franja norte, calles 82–85 y los 3 ejes por el centro; igual que en HMK, las etiquetas muestran número, no área. **De paso se arreglaron 4 bugs de `civil3d_parcel`** (probados en vivo sobre este dibujo): `report` convertía `sqm` como si el dibujo fuera en pies, no devolvía vértices (ahora por COM) y escribía el CSV con coma decimal; `lot_line_adjust` nunca funcionó (la API de Civil 3D no tiene el método) y ahora lo dice con `NOT_SUPPORTED` en vez de «attempted». Dibujo: `pruebas/loteo/mcpitza-loteo/loteo-prueba-sacredgitza-etapa2.dwg`. |
| 3. Calle A: eje → rasante → ensamblaje → corredor → volúmenes | ❌ **Intento 1 (2026-09-25): Civil 3D se cerró.** Antes de arrancar hizo **3 preguntas de ambigüedad reales** (dónde va el cordón si 7+4+4 ya suman 15 m, dimensiones del cordón cuneta, si los taludes pueden invadir lotes). Logró: alineamiento "Calle A" de 152,5 m (con código), perfil de terreno y vista de perfil (tools propias), rasante de una tangente al −1,2 % con desvío 0,00 m del TN (tools propias; cumple 0,5–6 %). Al llegar al **ensamblaje (sin tool en HMK)** se puso a explorar la API en vivo: listó métodos por reflexión e importó subensamblajes de prueba (`BasicLane`, `UrbanCurbGutterGeneral`, `UrbanSidewalk`, `LinkSlopeToSurface`, daylight) para leer sus parámetros, deshaciendo con `tr.Abort()`. **A las 11:58:12 ejecutó código que recorría por reflexión todas las propiedades de un `SubassemblyPoint` → Civil 3D no respondió en 30 s (timeout de HMK) → en la llamada siguiente se cayó el proceso entero** (cartel "AutoCAD no puede continuar", se ofreció `_recover.dwg`). Es el riesgo concreto del escape hatch: el código corre dentro del proceso de Civil 3D. Diagnóstico reconstruido con `%LOCALAPPDATA%\HMKTools\CadPilot\poc.log` + **`execute_audit.log`** (HMK registra todo el código ejecutado con fecha, duración y resultado — idea a replicar en sacred-g-itza). **Recuperación: ✅** en el mismo chat se atribuyó el cierre a su propio script, desaconsejó trabajar sobre el `_recover` (posible inconsistencia + subensamblaje de prueba suelto), propuso reabrir el `.dwg` limpio y rehacer lo perdido evitando la reflexión a ciegas. **Intento 2: ✅ Completa.** Resultado: alineamiento 152,5 m (sin emplazamiento, para no cortar parcelas), rasante −1,20 % pegada al TN; **ensamblaje de 8 subensamblajes estándar creado por código** (`BasicLane` 3,00 m −2 % + `UrbanCurbGutterGeneral` cuneta 0,50 + cordón + `UrbanSidewalk` + `DaylightGeneral` 1:2, por lado) — sección exacta de 15,00 m, interpretando bien "7 m hasta cara de cordón"; **avisó** que el cordón estándar mide 0,20 (no 0,15) y achicó la vereda a 3,80 m para no pasarse de la línea municipal. Corredor con secciones cada 5 m y TN como objetivo de taludes. **Detectó y resolvió** que las superficies del corredor quedaban vacías tras el rebuild (Top 416 pts, Datum 352). Volúmenes: vs. rasante terminada 37,7 m³ desmonte / 149,4 m³ terraplén; vs. subrasante 556,8 / 29,8 — **verificado a mano en orden de magnitud** (≈38 m³ de desmonte en calzada y 90–150 m³ de terraplén en veredas por el bombeo + cordón + pendiente transversal del TN de 0,8 %); no se pudo confirmar su afirmación de que el talud oeste sea desmonte (según la cuenta a mano, ambos lados serían terraplén, el oeste casi nulo). Taludes invaden 0,19–0,44 m los lotes (coherente). Declaró sus límites: cruces con B y C sin modelar, paquete estructural con espesores por defecto (no especificados en la consigna). **Tools propias:** perfiles, PVIs, vista de perfil, rebuild, superficies de volumen. **Código:** alineamiento, ensamblaje entero, corredor (para fijar targets y frecuencia), rebuild de superficies del corredor, controles. | ✅ **Completa (10-13), 100 % tools propias, sin código — pero tras ampliar bastante el fork en la misma sesión.** **Antes de dibujar:** propuesta en texto (eje, rasante, sección tipo con subensamblajes, corredor, volúmenes) + **5 preguntas** (cota de la calle existente en el empalme, rasante sobre o a nivel del TN, si va cordón cuneta, si se modela el paquete, estado de Civil 3D); Santi dejó 4 a criterio → rasante a nivel del TN (empalme sin escalón), cordón cuneta sí, paquete con espesores por defecto. **Resultado:** eje "Calle 3" de 136,5 m (x = 5120, N 10000 → 10136,5) **sin emplazamiento, para no cortar las parcelas** (misma decisión que HMK); perfil del TN + rasante de una tangente al −1,20 % (99,040 → 97,402), desvío 0,000 m del TN en todas las muestras. Ensamblaje "Calle 15 m" de 8 subensamblajes por lado: `BasicLane` 3,50 m −2 % (paquete 0,20) + `UrbanCurbGutterGeneral` (cuneta 0,40 + cordón hasta `Back_Curb` a 0,60) + `UrbanSidewalk` 3,40 m +2 % hacia el cordón + `BasicSideSlopeCutDitch` como talud 1:2 puro (cuneta en ancho 0) → **línea municipal exacta a 7,50 m (15,00 m)**, verificada midiendo la superficie Top en la progresiva 30 (eje 98,680 · borde de calzada 98,610 · cordón 98,752 · vereda 98,753 → 98,820 a 7,50 m · talud hacia el TN). Corredor cada 10 m con TN como objetivo de los 2 taludes y superficies Top y Datum. **Volúmenes vs. subrasante (Datum): 596 m³ desmonte / 18 m³ terraplén; vs. rasante terminada (Top): 42 / 108** (`compute_volumes` los devolvía invertidos; se detectó comprobando que la vereda queda sobre el TN y se arregló en el fork el mismo día). **Vista de perfil** "Calle 3 - Perfil longitudinal" creada con `profile view_create` (también hubo que arreglarla); quedó encima del loteo y se reubicó con la acción nueva `profile view_move` (ubicación automática a la derecha de lo dibujado, sin superponer: 5343–5498 × 9994–10264), tras arreglar en el fork cómo mide los límites de la vista y sus bandas. **Invasión de taludes sobre los lotes: 0,41 m al este y 0,16 m al oeste, constante en toda la calle, los dos en terraplén** — calculada (TN plano, rasante paralela) y verificada muestreando Top vs. TN en las progresivas 30 y 100; **HMK midió 0,19–0,44 m**, y esto confirma la cuenta a mano de su fila (HMK afirmaba que el oeste era desmonte). **Vs. HMK (cotejado con esta tabla):** misma estrategia de rasante (−1,2 % pegada al TN) y misma lectura de la sección (7 m de calzada hasta cara de cordón + cordón + vereda achicada para no pasar la línea municipal; HMK: carril 3,00 + cuneta 0,50 + cordón 0,20 + vereda 3,80; acá: 3,50 + cordón cuneta 0,60 + 3,40). Calle más corta (136,5 vs. 152,5 m, por el diseño de la etapa 2). Por metro: desmonte en subrasante 4,37 vs. 3,65 m³/m, terraplén en rasante 0,79 vs. 0,98 m³/m — mismo orden y mismo signo; la diferencia es coherente con el cordón cuneta más ancho y profundo (base a −0,45). Frecuencia 10 m (HMK 5 m). **HMK escribió el ensamblaje y el corredor con código** (y antes se le cayó Civil 3D explorando la API por reflexión); acá todo con tools — pero el fork **no las tenía o andaban mal y hubo que programarlas en la sesión**: `corridor create/delete/surface_add/surface_delete/region_edit`, encadenar subensamblajes (`attachTo`/`attachPointIndex`; antes todo quedaba encimado en el eje), parámetros de subensamblaje (se ignoraban en silencio), `target_mapping_get/set`, `region_add/delete` y puntos de subensamblaje en `assembly get`. **El problema más serio:** al aparecer las superficies del corredor el plugin se colgaba entero (el contexto de comando de AutoCAD nunca se cerraba); se diagnosticó con log paso a paso + `dotnet-stack` y se resolvió pasando toda la ejecución a `Application.Idle` + armado explícito de las TIN del corredor (**el mismo síntoma de superficies vacías que tuvo HMK**). **Errores propios en el camino:** el primer enganche de la vereda tomó el pie inferior del cordón (regla "punto más exterior") → vereda 0,33 m bajo el cordón; se detectó midiendo la Top, se leyeron los puntos reales y se rehízo con `Back_Curb`. **Costo:** una sesión larga con ~12 reinicios de Civil 3D para recompilar (HMK: una sesión, con un cierre forzado). Sin modelar (igual que HMK): cruce con la Calle 1 (el corredor la atraviesa de largo). **Veredicto: resultado igual a HMK** (misma rasante, misma lectura de la sección, volúmenes del mismo orden, vista de perfil e invasión de taludes equivalentes) **y mejor en proceso** (todo con tools, sin código ni cierres de Civil 3D); **peor en costo** de esta primera vez (programar el fork), que no se repite en la próxima obra lineal. Detalle técnico en `CAMBIOS_ITZA.md` 10-13 del repo sacred-g-itza. Dibujo: `pruebas/loteo/mcpitza-loteo/loteo-prueba-sacredgitza-etapa3.dwg`. |
| 3b. Obras lineales en calles B y C + intersecciones con A | 🚫 **Descartada (2026-09-25, decisión de Santi):** no suma respecto de lo que ya mostró la etapa 3 con la calle A. Origen: **pedido de Joaco** al ver la captura con los ejes: "probá decirle que genere una obra lineal sobre las líneas rosadas". La etapa 3 solo modeló la calle A; B y C se cruzan con A y hay que resolver las intersecciones (la propia IA avisó que no las había modelado). Se hace después de la etapa 5, sobre una copia de la etapa 3 (`loteo-prueba-hmk-etapa3b.dwg`), sin la red pluvial. | ⏳ pendiente |
| 4. Red pluvial | ✅ **Completa (2026-09-25).** Respuestas dadas: opción a) para B-este, sumideros en ambos puntos bajos, descarga libre, hormigón n = 0,013. Resultado: red de gravedad "Pluvial Loteo" (lista de piezas "Storm Sewer", TN como superficie de referencia) con **7 cámaras, 7 sumideros, 1 cabecera de descarga y 14 conductos** (7 troncales de Ø 0,50 → 0,60 → 0,70 → 0,80 m + 7 conexiones de Ø 0,40), por B-este → A → C-este → descarga NE; tramo máximo sin cámara 60 m. **Verificado a mano:** capacidades Manning de los 5 diámetros/pendientes, acumulación de caudales (P-03 0,300, P-05 0,481, P-07 0,640 m³/s) y suma de áreas = 38.400 m², largos consistentes con el loteo, invertida de descarga 1,99 m bajo TN. Usos máximos 89 % (P-03/P-04). Tapadas: 0 conductos en falta según `IsMinCoverViolated` de Civil 3D; mínima 1,00 m, máxima 2,32 m (CI-3, por correr B-este contra pendiente). Buena decisión: en A dejó 0,3 % aunque el TN baja 1,2 %, aprovechando la profundidad que traía de B-este. **Se autocorrigió:** primero midió tapada a la clave interior, Civil 3D marcó 11 conductos en falta (mide a cara exterior), rediseñó todas las cotas. Declaró límites: tapada verificada contra TN (falta rasante de B y C), sumideros esquemáticos sin modelar captación de rejas, descarga profunda que exige receptor a cota ≤ 94,175. **100 % con código:** HMK no tiene tools para agregar caños/estructuras, y su `create_pipe_network` no deja fijar lista de piezas ni superficie; reflexión usada solo para listar nombres, sin cuelgues. **Captura en planta confirma** la disposición descripta (7 cámaras en B-este, cruces A–B y A–C, mitad de A y C-este; troncal B-este → A → C-este → NE). **Arranque:** El conector se cayó al arrancar (no tocó nada, lo avisó). Mientras tanto, **analizó el diseño en texto y frenó con 4 preguntas de ingeniería reales:** (1) el tramo este de la calle B termina en un punto bajo contra el lindero, sin calle que siga hacia la descarga NE — propuso 3 opciones (conducto contra pendiente hasta A con invertida a ~2,7 m / conducto por el borde de M4 con servidumbre / sin conducto); (2) sumidero en el fondo de la calle C; (3) qué recibe el agua en la descarga (cota de invertida mínima ~1,4 m bajo terreno); (4) material/Manning y reparto de áreas de aporte. Esquema preliminar: colector por A (desde B hacia el norte) y por C hacia el este hasta la descarga, 5 sumideros, 5 cámaras. Q de diseño = 0,6 × 100 × 3,84 / 360 ≈ 0,64 m³/s → conducto de ~0,70 m en C (**verificado a mano con Manning, n = 0,013 y pendiente 0,8 %:** 0,60 m da 0,55 m³/s < 0,64; 0,70 m da 0,83 m³/s ✓). **Hallazgo:** el punto (1) muestra que el diseño del loteo de la etapa 2 no consideró el drenaje — en un proyecto real se habría ajustado el trazado de calles. | ✅ **Completa (10-13), 100 % tools propias, sin código — tras ampliar `civil3d_pipe` en la misma sesión.** **Antes de dibujar:** leyó el TN con tools (plano: −0,8 % hacia el E, −1,2 % hacia el N, punto bajo NE 96,16) y preguntó lluvia/C, diámetro mínimo/tapada/material y alcance; Santi dejó todo a criterio → supuestos declarados: racional, R = 5 años, i = 120 mm/h (tc 15 min), C 0,85 calles / 0,50 lotes / 0,25 verde (pond. 0,56), hormigón n = 0,013, Ø mín. 0,40, tapada 1,0 m, 0,3 % mín., 0,9–5 m/s, cámaras ≤ 100 m. **Frenó con la misma ambigüedad que HMK** (tramo este de la Calle 1 contra el lindero bajo, sin calle hacia el NE) y propuso 3 opciones; Santi eligió la **servidumbre pluvial de 3 m por el borde este de la manzana NE** (HMK fue contra pendiente). **Resultado:** red "Pluvial" (Storm Sewer, TN de referencia) con 13 cámaras, 22 sumideros, 1 cabecera de descarga y 35 conductos: 13 colectores (Ø 0,40 en ramales, Calle 3 y servidumbre; Ø 0,60 en Calle 2 este; Ø 0,70 a la descarga) + 22 conexiones de sumidero Ø 0,40 al 2 % (en el cordón, ambos lados, en todas las esquinas aguas arriba y en las cámaras intermedias). Áreas de aporte verificadas contra las parcelas (lotes 25.618 + verde 3.840 + calles 8.942 = 38.400 m²; el verde descarga en superficie al NE). **Mismo error y misma corrección que HMK:** la primera versión midió la tapada a la clave interior (0,89–0,93 m al exterior); se borró la red y se rehízo. **Comparación medida con las mismas tools en los dos dibujos** (`hydraulic_analysis`/`calculate_hgl` con el caudal de diseño de cada uno; `get_structure_properties` para ubicaciones; el dwg de HMK abierto solo para leer, sin guardar): caudal en la descarga 0,640 (HMK) vs. 0,665 m³/s · piezas 7 cámaras + 7 sumideros + 14 conductos vs. 13 + 22 + 35 · **largo de colector 332,5 vs. 612,5 m** · diámetros 0,50–0,80 vs. 0,40–0,70 · pendientes 0,3–1,0 % vs. 0,8–1,2 % · uso máx. 89 % (P-03/04) vs. 93 % (P-8) · velocidad 1,2–2,6 vs. 1,4–2,8 m/s · ambos 14/14 y 35/35 a superficie libre, 0 en carga · **cámara más profunda 3,05 (CI-3) vs. 1,93 m (C2-5)** · **tapada máx. 2,32 vs. 1,085 m** · invertida de descarga 1,985 vs. 1,93 m bajo TN. **Diseños distintos, no uno mejor:** HMK lleva todo por un único troncal B-este → A → C-este, solo con conductos en esas 3 ramas — B-oeste y C-oeste no tienen caños ni sumideros y escurren por cuneta hasta ~112 m hasta el cruce con A; sumideros S1–S3 en el cruce A–B, S4–S5 en A–C y S6–S7 en los fondos de B-este y C-este (estos dos sobre el eje, no en el cordón) → **mitad de caño, más barato, pero más profundo** (por correr B-este contra pendiente). Acá la servidumbre parte el caudal → **cañerías más chicas y poco profundas en las 6 ramas**, a costa de casi el doble de caño y de gravar los 2 lotes del extremo este de la manzana NE. **Hallazgo en el dibujo de HMK (no estaba en su informe):** CI-1, CI-7 y la descarga están **sobre el lindero (x = 5240)** y DESC en el vértice NE → las cámaras de Ø 1,50 sobresalen 0,75 m fuera del terreno; las de acá quedan adentro (x = 5238,5, Ø 1,20). **HMK lo hizo 100 % con código** (no tiene tools para caños ni estructuras); acá todo con tools — pero **hubo que arreglar y ampliar el fork**: `catalog_list`/`create` no encontraban la lista de piezas (nombre `null` en todo estilo: `StyleBase` solo sobrescribe `set_Name`), `add_structure` fallaba con cabezales, `hydraulic_analysis`/`calculate_hgl` daban todo FAIL en métrico (pendiente en fracción leída como %, unidades imperiales fijas, HGL por cota y no por red) y se reescribieron con la API tipada, `size_network` leía "1,000 mm" como 1; nuevas: `name` en altas, `delete`/`delete_pipe`/`delete_structure` y **`add_many`** (22 sumideros + 22 conexiones en 1 aprobación y 1 llamada). Bug visto al medir HMK: `pipe get` falla en redes sin estilo (`StyleName`). **Incidente:** tras un reinicio se abrió por error el `etapa4.dwg` de HMK y se borró su red en memoria antes de verificar el dibujo activo; no se guardó (archivo de HMK intacto, fecha 09-25) → regla nueva: `drawing info` antes de la primera escritura. **Costo:** 4 recompilaciones. **Veredicto: igual a HMK en lo hidráulico** (mismo método, caudal, uso máximo y descarga; las dos redes cumplen) **con un diseño distinto** (más caño y menos profundidad vs. menos caño y más profundidad), **mejor en proceso** (todo con tools, verificación hidráulica automática y repetible, sin piezas fuera del terreno); peor en costo de esta primera vez. Detalle técnico en `CAMBIOS_ITZA.md` 10-13 (etapa 4) del repo sacred-g-itza. Dibujo: `pruebas/loteo/mcpitza-loteo/loteo-prueba-sacredgitza-etapa4.dwg`. |
| 5. Alumbrado y arbolado | ✅ **Completa (2026-09-25), tras 2 rondas de corrección.** Estado final verificado a mano: A con vanos 23,83 × 3 + 28,33 × 3, B 30,00 × 7 + 26,00, C 30,00 × 8, mínima entre columnas 25,14 m = √(23,83² + 8²), 23 columnas y 140 árboles, 5 árboles corridos, cero violaciones. Correcciones hechas con `move_entities` (tool propia). Historia: Antes de arrancar, **3 preguntas de ambigüedad reales** (vanos de A no múltiplos de 30 → se eligió 30 m como máximo con reparto parejo; si el cruce con la calle sur lleva columna → sí; cómo leer "cada 30 m" en tresbolillo → alternando, 60 m por vereda). Resultado: **23 columnas** (bloque COLUMNA_AP, capa ALUMBRADO, a 4,0 m del eje) y **140 árboles** (ARBOL_2M, capa ARBOLADO, a 5,0 m del eje); 4 árboles corridos por quedar a < 1,5 m de una columna, ninguna columna movida. **Verificado a mano:** 9 columnas en B y en C (240 m / 30), 5 propias en A (vanos 22,5 y 28,33), mínima entre columnas 23,88 m = √(22,5² + 8²), 30 árboles en la vereda norte de C (única sin cruce) vs. 28 en las demás. **⚠️ Observación:** afirma que en B todos los vanos son de 30,00 m, pero ubicó la columna del cruce A–B en x = 5124 (no 5120) → con la serie de B cada 30 m desde 5000, los vanos vecinos serían ~34 y ~26 m (34 > máximo). **Se le preguntó y lo reconoció:** midió en el dibujo (34,00 y 26,00 m sobre el eje), admitió que su informe estaba mal y encontró la causa — su **script de verificación usaba la posición nominal (x = 5120) en vez de la real (x = 5124)**, que había corrido por los sumideros de las otras 3 esquinas; delimitó el alcance (C y A no afectadas) y propuso 2 arreglos sin tocar nada. Se eligió correr toda la serie de B 4 m al este (vanos de 30 m salvo uno de 26 m, tresbolillo intacto) y reverificar midiendo posiciones reales. **Lección clave de toda la auditoría: su autoverificación informó ✅ en un criterio incumplido.** Las tablas de "cumple" que entrega son convincentes pero no reemplazan la revisión de un ingeniero — el error solo apareció por un control externo. **Reverificación con posiciones reales:** B quedó bien (30,00 × 7 + 26,00; 2 árboles nuevos corridos, 1 devuelto) y **encontró solo el mismo error en la calle A** (último vano 32,33 m, porque la columna de A–C está 4 m al norte del cruce de ejes), más un segundo error propio (medía distancias a estructuras en 3D, contra su cota, en vez de en planta — lo repitió en 2D). Propuso emparejar A en 3 × 28,33 (norte) y 3 × 23,83 (sur) — verificado a mano; se aplicaron las dos. **Dato clave:** pedirle explícitamente "verificá midiendo las posiciones reales, no las calculadas" hizo aparecer un error que antes había informado como ✅ — la forma de pedir la verificación cambia mucho la calidad del control. **Primera etapa con mayoría de tools propias:** `create_layer`, `create_circle`/`draw_line`/`create_block`, `insert_block` (13), `array_rectangular` (11 series), `move_entities`, `erase_entities`; código solo de lectura para verificar criterios. Sin reflexión. | ✅ **Completa (10-13), 100 % tools propias, sin código y sin correcciones.** **Antes de dibujar** (sesión nueva, sin ver nada de HMK): leyó la geometría con tools (`parcel report` de calles y verde con vértices; `pipe get_structure` de los 22 sumideros) y propuso en texto posiciones, distancias al cordón y símbolos + **3 ambigüedades** (cómo leer "cada 30 m alternando" → zigzag, 60 m por vereda, igual que HMK; si las puntas de las Calles 1 y 2 contra el lindero cuentan como esquina → sí, retiro de 5 m; qué hacer con árboles que chocan con columnas/sumideros → correr solo ese árbol hasta 2 m, sin bajar de 6 m con los vecinos); Santi aprobó las tres. **Resultado:** **20 columnas** (`COLUMNA-AP` con brazo rotado hacia la calzada, capa ALUMBRADO, a 4,1 m del eje = 0,6 del cordón) — Calles 1 y 2: 8 c/u cada 30 m desde x = 5015 en zigzag (la Calle 2 empieza del lado del verde); Calle 3: 4 cada 30 m desde N 10015 — y **128 árboles** (`ARBOL`, capa ARBOLADO, a 4,5 m del eje = 1,0 del cordón), 13 por vereda en cada tramo de las Calles 1 y 2 y 6 por vereda en cada tramo de la Calle 3, centrados en el tramo útil; 14 corridos por choque. **Dibujo:** `block_define` ×2 + **`block_insert_many` ×2 (20 y 128 en 1 aprobación y 1 llamada cada uno)**, 10 llamadas en total; primer uso en vivo de las 3 tools de bloques (agregadas al fork el mismo día, andaron a la primera). **Verificado con las posiciones reales** (`block_list`, no las calculadas — la lección de HMK): 128/128 y 20/20 exactos; retiro mínimo a esquina/lindero 8,25 m; vanos de columnas 30,00 m sobre el eje; columna–árbol ≥ 2,04 m y sumidero–árbol ≥ 2,25 m en planta. **Incumplimiento declarado:** donde se corrió un árbol el vano supera los 8 m (máx. **10,00 m**, x 5056,25 → 5066,25 junto al sumidero de x = 5064, en las 4 veredas de las Calles 1 y 2) — la regla aprobada fijaba mínimo pero no tope; además no hay columna en los cruces, así que de la última de la Calle 3 a la más cercana de la Calle 2 hay 33,3 m. **Comparación medida con la misma tool en los dos dibujos** (`block_list` con posiciones; dwg de HMK abierto solo para leer, sin guardar): columnas **23 vs. 20** · árboles **140 vs. 128** · columna a 4,0 vs. 4,1 m del eje · árbol a 5,0 vs. 4,5 m · vanos de columnas: HMK reparte para poner columna en cada cruce y en las puntas (A 23,83/28,33, B 30 × 7 + 26, C 30 × 8, columnas sobre el lindero en x = 5000/5240), acá 30,00 fijos sin columna en cruces · **vano máximo entre árboles 9,2 (HMK, vereda norte de C) vs. 10,0 m** — **HMK tampoco cumple "cada 8 m"** donde corrió árboles y no lo informó · **columna–árbol mínimo 1,56 m en HMK** (x = 5064/5065,2 en B; su umbral era 1,5) vs. 2,04 · retiro en las puntas contra el lindero: HMK pone árboles a 0,67 m del lindero (no las tomó como esquina), acá 8,25. Las diferencias de cantidad salen del trazado de la etapa 2 (calle C de HMK sobre el lindero, con vereda norte continua de 30 árboles) y de los criterios de cruces/puntas. **Vs. HMK en proceso:** HMK necesitó 2 rondas de corrección y su autoverificación informó ✅ un criterio incumplido (columna del cruce A–B); acá 0 correcciones y el desvío (vano > 8 m) lo encontró y lo declaró el propio agente al verificar con posiciones reales. **Veredicto: igual a HMK en resultado** (misma lectura de la normativa, mismo esquema de zigzag, mismos símbolos y capas; los dos con vanos de árboles > 8 m donde hubo choques) **y mejor en proceso** (2 llamadas de inserción en lote, sin correcciones, verificación contra el dibujo real); HMK mejor en criterio de iluminación (columnas en los cruces). Dibujo: `pruebas/loteo/mcpitza-loteo/loteo-prueba-sacredgitza-etapa5.dwg`. |
| 6. Entregables (etiquetas + láminas) | — **No se hizo con HMK** (etapa agregada el 10-13 por el input de Fabián: etiquetado y armado de planos es lo que más retrabajo lleva). | ✅ **Completa (10-13), 100 % tools propias, sin código — pero casi todo hubo que programarlo en el fork en la misma sesión** (5 recompilaciones). **Alcance aprobado por Santi:** 4 láminas A3 a 1:1000 con rótulo genérico (no hay rótulo de ITZA): L1 loteo, L2 red pluvial, L3 alumbrado y arbolado, L4 perfil longitudinal de la Calle 3 (H 1:1000 / V 1:100). **Resultado:** `loteo-prueba-sacredgitza-etapa6.dwg` + `loteo-prueba-sacredgitza-etapa6.pdf` (4 páginas). **Etiquetas nativas de Civil 3D con estilos propios en castellano** creados por tool (copia de un estilo de la plantilla + edición de contenido, altura y visibilidad): «ITZA Lote» (número + m², vertical, 1,8 mm), «ITZA Superficie» (calles y verde), «ITZA Lado» (solo distancia), «ITZA Colector» (`P-n Ø L= i=`), «ITZA Cámara» (nombre + cota de tapa + cota de fondo). Aplicadas en lote: 80 lotes + 5 calles/verde, 228 lados (192 lados duplicados descartados solos), 13 colectores, 14 cámaras — 1 llamada por tanda. Láminas con ventanas a escala exacta y capas congeladas por lámina; rótulo, cuadros (balance de superficies, resumen pluvial, cantidades, datos del perfil), notas, norte y escala gráfica dibujados por tool (60–84 entidades por lámina, 1 llamada); PDF de 4 láminas en 1 llamada, verificado leyéndolo. **Lo que hubo que agregar al fork:** `label_add_many`/`label_delete_many`/`label_style_get`/`label_style_create` (estándar), `layout_create`/`layout_add_entities`/`layout_viewport_scale`/`layout_delete`/`layer_list` y `sheet_publish_pdf` real (antes devolvía error a propósito). **Hallazgos que valen para cualquier plano:** (1) las etiquetas de Civil 3D se dibujan con la *escala de anotación* de la ventana — una ventana nueva queda en 1:1 y las etiquetas no se ven; (2) el texto de una etiqueta va en la capa que fija el *estilo*, no en la de la etiqueta → para ocultarlas por lámina hay que congelar `C-PROP-TEXT`/`C-STRM-*`; (3) los códigos de campo vienen localizados (`<[Área de parcela(...)]>`); el del diámetro («Anchura o diámetro interior de tubería») no está documentado y se sacó leyendo el estilo de cámara de la plantilla, después de 6 intentos fallidos; (4) un lado compartido entre dos lotes existe dos veces; (5) `ParcelSegmentLabel.Create` no sirve para ubicar la etiqueta (solo acepta ratio 0) → se usó COM por punto. **Incidentes:** Civil 3D se cerró solo 2 veces, las dos con una ventana a 1:1250 sobre la vista de perfil (causa de fondo sin aislar; a 1:1000 anduvo) — se perdió trabajo una vez y se rehízo; desde ahí, guardar después de cada paso. **Pulido (misma sesión):** con la acción nueva `label_style_edit` (editar un estilo actualiza todas sus etiquetas al instante) se pasó a coma decimal y se achicó el texto de los lotes; etiquetas del borde este reubicadas, estilo propio para la descarga, ensamblaje rotulado como «Sección tipo» en L4. **Queda:** las bandas del perfil con el estilo de la plantilla (inglés, punto decimal) y no hay acción para mover una etiqueta existente. **Sin referencia HMK para comparar.** Detalle técnico en `CAMBIOS_ITZA.md` 10-13 (etapa 6). |

## Fuentes

- [MCP to Connect Claude to C3D — Autodesk Community idea](https://forums.autodesk.com/t5/civil-3d-ideas/mcp-to-connect-claude-to-c3d-ai-to-generate-infrastructure-parts/idi-p/14138155)
- [Controlling Architecture Software with Claude — arch-control.ifnotfor.com](https://arch-control.ifnotfor.com/)
- [HMK CAD Pilot — AutoCAD & Civil 3D MCP Server](https://hmktools.com/autocad-mcp)
- [HMK Revit MCP](https://hmktools.com/revit-mcp)
- [Civil3D-mcp (Sacred-G)](https://github.com/Sacred-G/Civil3D-mcp)
- [Autodesk Platform Services — sample MCP server for Revit Automation API](https://github.com/autodesk-platform-services/aps-sample-mcp-server-revit-automation/)
- [Autodesk Platform Services — Building for Agentic AI blog](https://aps.autodesk.com/blog/building-agentic-ai-whats-new-autodesk-platform-services)
- [Autodesk — What You Need to Know About MCP Servers in Construction](https://www.autodesk.com/blogs/construction/mcp-servers-in-construction/)
- [Revit MCP with Claude, Cursor, and ChatGPT: AI Client Guide](https://goto.archi/blog/post/revit-mcp-claude-cursor-chatgpt)
- [Civil 3D API — Autodesk Platform Services](https://aps.autodesk.com/developer/overview/civil-3d)
- [Dynamo Primer — Python and Civil 3D](https://primer2.dynamobim.org/dynamo-for-civil-3d/advanced-topics/python-and-civil-3d)
- [Autodesk Community — Run Dynamo player command](https://forums.autodesk.com/t5/civil-3d-forum/run-dynamo-player-command/td-p/9328592)
