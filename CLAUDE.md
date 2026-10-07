# Contexto ITZA — por qué este repo está acá

Este repo **no es un proyecto propio de ITZA construido desde cero** — es un **fork/clon de
[Sacred-G/Civil3D-mcp](https://github.com/Sacred-G/Civil3D-mcp)**, un servidor MCP open source
(MIT) que conecta clientes de IA (Claude, Cursor, Cline) a una sesión real y abierta de Civil 3D
vía un plugin C# + un servidor Node/TypeScript. ITZA lo adoptó como base para su estrategia de
automatización de Civil 3D con IA, en vez de pagar la alternativa comercial (HMK CAD Pilot,
$99/año) o construir algo propio desde cero.

## Repo hermano — ahí vive el resto del contexto

Todo el resto de la investigación, decisiones, bugs encontrados y pruebas reales vive en el repo
**`app-itza2026`** (la PWA operativa de ITZA), no acá:

- Ruta local: `C:\Users\santi\Desktop\app-itza2026`
- Doc principal: [`docs/itza360-agentica/investigacion-mcp-civil3d.md`](../../../Desktop/app-itza2026/docs/itza360-agentica/investigacion-mcp-civil3d.md)
  — investigación completa: por qué MCP y no computer-use/Dynamo, comparación HMK vs. este repo,
  instalación paso a paso reproducible (sección 8), y todo lo probado en vivo.
- `docs/tareas.md` de ese repo tiene el puntero de una línea a esta tarea.

**Si abrís una sesión de Claude Code acá, para tener el contexto completo conviene también abrir o
leer esos archivos del otro repo** — acá no se duplica nada de esa documentación a propósito, para
no tener dos fuentes de verdad desincronizadas.

## Objetivo de por qué se clonó este repo específicamente (2026-09-21)

1. Se probó primero otra alternativa gratis (hjlrosales/Civil3D_MCP, 35 tools) — funcionaba pero
   necesitó arreglar 4 bugs de código reales a mano para lograr crear una sola red de tuberías. El
   fix se mandó como [PR #1](https://github.com/hjlrosales/Civil3D_MCP/pull/1) al repo original,
   pero se descartó como base para seguir construyendo.
2. Se encontró este repo (Sacred-G) después — **219 operaciones** (34 tools públicas por default,
   cada una agrupando varias acciones), cubre mucho más que el anterior: alineaciones (con
   creación real), superficies, parcelas, puntos COGO, redes de tuberías a gravedad **y a
   presión**, hidrología, QC, producción de planos y **estilos de etiqueta** (el dolor puntual que
   citó Joaco en la reunión "9-15 Itza Multiagentica" — "horas y horas solo en etiquetas").
3. Conectó y creó geometría real (un alineamiento con curva) **a la primera, sin ningún bug** —
   muy distinto a la experiencia con el repo anterior.

## Decisión de estrategia (ITZA, 2026-09-21)

ITZA no va a pagar HMK. Se mantiene este fork (a renombrar **sacred-g-itza**) y se le agrega funcionalidad de a poco, priorizada
por casos de uso reales de ITZA (mensura/plano reglamentario, estilos de etiqueta) — no se intenta
replicar HMK entero de una. No se cuenta con que la comunidad del proyecto original (Sacred-G)
contribuya activamente — se trata como que lo mantiene ITZA, cualquier aporte externo es bonus.

## Instalación real ya hecha en esta máquina (referencia rápida)

- Clonado en `C:\Users\santi\Documents\GitHub\sacred-g-itza` (esta carpeta; se llamaba
  `Civil3D-mcp` hasta el 10-07). Remotes: `origin` = [SantiBargas/sacred-g-itza](https://github.com/SantiBargas/sacred-g-itza)
  (privado), `upstream` = Sacred-G (para traer novedades del original).
- `npm install && npm run build` — sin errores.
- Plugin compilado con `dotnet build .\Civil3D-MCP-Plugin\Civil3DMcpPlugin.csproj -c Release`,
  usando una carpeta local `C_References\` armada a mano con 6 DLL de Civil 3D repartidas en 3
  carpetas distintas de la instalación (no se commitea, es local — ver detalle exacto en el doc
  principal, sección 8).
- Se carga en Civil 3D con `NETLOAD` (no hay bundle de auto-carga — hay que repetirlo cada sesión,
  o configurar el Startup Suite de `APPLOAD`).
- Registrado en Claude Code con `claude mcp add --scope user --transport stdio civil3d-mcp -- ...`
  (scope `user` = config global, disponible en cualquier sesión de Claude Code, no solo acá).

## Registro de cambios ITZA

**Todo cambio respecto del upstream se documenta en [`CAMBIOS_ITZA.md`](CAMBIOS_ITZA.md)** (qué,
por qué, estado de verificación), en el mismo turno en que se hace el cambio. Ese archivo es la
fuente de verdad del estado de fixes y bugs abiertos — no duplicarlo acá.

## Cómo trabajar en este repo

- **Dos mitades:** plugin C# .NET 8 (`Civil3D-MCP-Plugin/`, corre dentro de `acad.exe`, escucha
  JSON-RPC en TCP `localhost:8080`) + servidor MCP Node/TypeScript (`src/`, habla stdio con Claude
  y reenvía al plugin). Una acción nueva necesita las dos: handler C# + ruta en
  `CommandDispatcher.cs`, y acción en `src/tools/domains/<dominio>Domain.ts` (`pluginMethods` +
  `sendCommand` con el mismo nombre de método).
- **Compilar plugin:** `dotnet build .\Civil3D-MCP-Plugin\Civil3DMcpPlugin.csproj -c Release`.
  **Civil 3D tiene que estar cerrado** — si no, compila pero falla la copia a `bin\` por lock
  (`acad.exe` a veces queda vivo sin ventana: revisar el Administrador de tareas).
- **Cargar:** en Civil 3D, `NETLOAD` con la **ruta absoluta**
  `C:\Users\santi\Documents\GitHub\sacred-g-itza\Civil3D-MCP-Plugin\bin\Release\net8.0-windows\Civil3DMcpPlugin.dll`
  (cada sesión de Civil 3D).
- **Servidor TS:** `npm run build`; tests `npx vitest run`. Después de cambiar `src/`, reconectar
  el MCP en Claude Code (`/mcp` → civil3d-mcp → Reconnect) para que tome el schema nuevo.
- **Probar en dibujo métrico** (`_Autodesk Civil 3D (Metric) NCS.dwt`) — la plantilla por defecto
  es Imperial (pies).
- **Aprobaciones:** acciones `create`/`edit`/destructivas exigen `civil3d_request_approval` antes;
  su objeto `parameters` **debe incluir `action`** y ser idéntico al de la llamada real.
- **Log del plugin:** `%LOCALAPPDATA%\Civil3DMcpPlugin\plugin.log`.
- **Archivos con CRLF** — al editar con scripts, preservar los finales de línea.
- **API de Civil 3D:** varias operaciones de creación no existen en la API .NET (ej. parcelas) y
  solo están en COM (`obj.AcadObject` + `dynamic`). Antes de asumir que un método existe,
  verificarlo leyendo la metadata de `C_References\AeccDbMgd.dll` (`System.Reflection.Metadata`),
  no por prueba y error con reflexión.
- **HMK CAD Pilot es comercial:** se documenta su comportamiento y se reimplementa; nunca copiar ni
  descompilar sus DLLs.

## Tareas / ClickUp

Este repo **no tiene su propia lista de ClickUp ni `docs/tareas.md` propio** — se trackea bajo la
lista de `app-itza2026` (convención de ITZA: cada repo sincroniza solo contra su lista
correspondiente; esto es un sub-tema de la de `app-itza2026`, no un repo independiente). Workspace
ClickUp `90171542241`, lista **"ITZA App"** (dentro del space "ITZA Dev"):

- [Investigar "Agéntica": IA conectada a Civil 3D/Revit vía Dynamo/MCP](https://app.clickup.com/t/86e39meu0)
  — la tarea madre de toda esta investigación.
- [Definir casos de uso concretos para Agéntica](https://app.clickup.com/t/86e39meu9) — para el
  caso de uso de mensura (generar plano reglamentario) y el de estilos de etiqueta, ninguno
  probado todavía a fondo.

## Próximos pasos

- **Qué hacer en la próxima sesión:** "Qué hacer ahora" de la tarea ITZA360 en
  `C:\Users\santi\Desktop\app-itza2026\docs\tareas.md` (y el último punto de la sección 7 del doc
  principal). A 2026-10-07: seguir la prueba del loteo desde la **etapa 2** — datos en
  `app-itza2026\docs\itza360-agentica\archivos\loteo\`, resultados etapa por etapa en la sección 9
  del doc principal (columna sacred-g-itza vs. HMK).
- **Pendientes técnicos del fork:** al final de [`CAMBIOS_ITZA.md`](CAMBIOS_ITZA.md).
- Se mantiene actualizado allá, no acá, para no duplicar.
