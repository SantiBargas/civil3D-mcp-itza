# 11-05 — Resumen de la reunión con Joaco (plan de trabajo MCP)

Transcripción completa en [`TRANSCRIPCION.md`](TRANSCRIPCION.md).

## Organización
- **Reunión semanal fija** (más alguna extra si surge algo) para ir testeando y llevar un avance progresivo.
- Joaco pide un **pequeño plan de trabajo / hoja de ruta**.

## Primer paso («primer paquetazo»)
1. **Presentación / estilos (layouts):** ver hasta dónde llega el MCP en láminas, estilos de
   presentación y etiquetas: cambiar colores, grosores de línea, **crear y guardar estilos**. Los
   estilos son 100 % importables entre dibujos → si el MCP no puede, los hace una persona; si puede,
   Claude «calcula y genera estilos a la vez». Hacer un **manual de lo que se puede hacer**.
   Menciona que Claude ya puede manejar el mouse (computer use) como posible complemento visual.
2. **Empaquetado de comandos:** hoy cada micro-comando pide autorización; HMK recibía una orden y
   resolvía todo sin preguntar. Objetivo: **lista de comandos mínimos generales que sí o sí funcionen,
   empaquetados y sin pedir tantos permisos** («le serviría a una banda»).
3. **Foco en 3 objetos de Civil 3D:**
   - **Superficies** — lo más maduro; «en un día lo comprobamos».
   - **Corredores** (obra lineal) + **ensamblajes/subensamblajes** — ver si el MCP sabe elegirlos,
     seleccionarlos o diseñarlos; «en una semana».
   - **Tuberías** — «seguro le va a faltar», es lo más complejo y de nicho.
4. **Manual de comandos** utilizables en Civil (los de ITZA, los de Santi y lo general).

## Versión de Civil 3D
- ITZA ya pasó todo el equipo a **2026** (no les afecta 2026 vs. 2027). Seguir en 2026 y actualizar a
  2027 al final.
- 2026 = .NET 8, 2027 = .NET 10. Santi va a analizar si se puede compilar compatible con las dos; si
  es factible, se cambia de una. Santi ya tiene licencia de estudiante de 2027 para probar.

## Contexto que mostró Joaco: nube de puntos (Aceitera Vicentin)
- Relevamiento con drone (fotogrametría, 247 M de puntos) vs. LiDAR (medición directa, más precisa);
  puntos de ajuste en el terreno; `.las` → ReCap (formato propio) → superficie en Civil 3D.
- Filtran/clasifican puntos de terreno, «relevan digitalmente» sobre el modelo y combinan con puntos
  relevados a pie para sacar pendientes. Uso: proyecto hídrico; también serviría para calles/loteos.

## Segundo paso: motores de cálculo abiertos → Civil 3D
- Civil 3D es un **modelador**: el cálculo (ej. tamaño de una alcantarilla) se hace antes en otros
  programas y después se «vuelca» a Civil. Hoy no hay conexión: calculan y pasan valores a mano.
- Idea: que Claude haga de **puente cálculo → diseño**. Primera prueba: **SWMM** (open source;
  Joaco dijo «del Ministerio de Agricultura de EE.UU.», en realidad es de la EPA). Después,
  **HEC-RAS** (cerrado pero gran parte abierta; fuerte en modelado 2D, validado en escenarios
  extremos, puede procesar 3 días). Otras disciplinas usan desde motores complejos hasta planillas
  de Excel con 3–4 parámetros (ej. estructuras).
- Propuesta de Santi: una **plataforma** que integre estos motores + el instalable del MCP, que
  detecte Civil 3D y se la pase a Claude ya integrada. A Joaco le gustó.

## Equipo, código y negocio
- **Pablo** se suma una vez que termine otra materia.
- Armar una reunión con los involucrados (Joaco, Pablo, Santi) y **algún contrato** para resguardar
  dentro de ITZA el código (MCP y demás); ver cómo se trabaja el **visualizador**.
- **Repositorio central de ITZA en GitHub** (web, app, MCP): Santi se ofrece a armarlo si Joaco crea
  la cuenta y le da acceso (o se lo delega).
- Joaco: posible búsqueda de **inversión** a futuro (reunión con una desarrolladora grande, programa
  de BIM/innovación); antes definir **qué problema genuino de la construcción** se ataca.
- Pendiente: reunión con **Mino y Nacho** para ordenar la estructura legal; café/birra con Pablo.
- Santi: la única restricción real hoy es pagar algo de infraestructura y cuidar licencias de
  librerías (ej. una que obliga a abrir el código si se comercializa).
