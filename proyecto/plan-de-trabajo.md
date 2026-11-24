# Plan de trabajo — MCP Civil 3D (ITZA)

Armado el 2026-11-16 a partir de la reunión del 11-05 ([resumen](reuniones/2026-11-05/RESUMEN.md)).
Se revisa y ajusta en cada reunión semanal.

## Objetivo del primer paso

Que el MCP haga **de punta a punta y sin pedir permiso en cada paso** el trabajo diario con los tres
objetos de Civil 3D que más usa ITZA (**superficies, corredores y tuberías**) y que pueda dejar la
**presentación lista**, con estilos y etiquetas. Todo queda documentado en un manual de comandos.

## Reunión semanal

- **Duración:** 30–45 min, día fijo a definir.
- **Orden:** (1) demo en vivo de lo que se hizo en la semana, (2) qué no anduvo o qué faltó,
  (3) se ajusta el plan y se confirma lo de la semana siguiente.
- **Registro:** cada reunión queda en `reuniones/aaaa-mm-dd/` (transcripción y resumen).

## Cronograma

| Semana | Tema | Qué se hace | Entregable / cuándo está hecho |
|---|---|---|---|
| **1** · 11-16 | **Superficies** + versión | Crear superficie desde puntos y desde nube de puntos, curvas de nivel, pendientes, volúmenes y etiquetas. En paralelo, ver si el plugin puede andar en 2026 y 2027 a la vez. | Lista de qué anda y qué no con superficies. **Decisión 2026 / 2027.** |
| **2** · 11-23 | **Corredores** | Obra lineal completa: eje, rasante, ensamblaje, corredor, volúmenes. Ver si el MCP **elige** ensamblajes y subensamblajes del catálogo, **usa** uno que ya está en el dibujo o **diseña** uno nuevo. | Una calle real de ITZA armada con el MCP. Lista de qué anda y qué no. |
| **3** · 11-30 | **Comandos empaquetados** | Con lo visto en las semanas 1 y 2, definir la lista de comandos mínimos que sí o sí tienen que andar y agruparlos para que una orden se resuelva entera con **una sola aprobación**. | Demo: una orden → resultado, sin preguntas de por medio. |
| **4** · 12-07 | **Estilos y presentación** | Crear y editar estilos (colores, grosores, etiquetas), guardarlos e importarlos en otro dibujo; láminas (layouts). Probar el «caso inverso»: darle un dibujo hecho por ITZA y que lo termine (etiquetas). | Lista de qué se puede y qué no. Un dibujo de ITZA terminado por el MCP. |
| **5** · 12-14 | **Tuberías** + manual | Red de tuberías sobre un caso real: relevar qué le falta. Juntar todo lo probado en el manual. | **Manual de comandos v1** (qué hace cada uno, ejemplo de pedido y límites). |

Joaco estimó superficies en un día y corredores en una semana. Si un tema se termina antes, se
adelanta el siguiente.

## Segundo paso (después del primero, ~enero)

- **Puente cálculo → diseño:** prueba de concepto con **SWMM** (open source): Claude corre el cálculo
  y vuelca el resultado a Civil 3D (por ejemplo, la red pluvial). Después, **HEC-RAS**.
- Idea a madurar: una **plataforma** que integre los motores de cálculo y el instalable del MCP.
- Si en la semana 1 se decidió 2027, se migra acá.

## Qué se necesita de ITZA

- **Dibujos reales** para probar: una superficie (ideal: nube de puntos o puntos de un relevamiento),
  una calle con corredor, una red de tuberías.
- **Plantilla y estilos propios** de ITZA (`.dwt`), para las semanas 4 y 5.
- **Cuenta de GitHub de ITZA**, para subir el código del MCP (y web y app) a un repositorio central.

## En paralelo (organización, a cargo de Joaco)

- Reunión con Pablo y los involucrados.
- Contrato o resguardo del código dentro de ITZA.
- Reunión con Mino y Nacho por la estructura legal.
- Definir qué problema de la construcción se quiere atacar, antes de pensar en inversión.
