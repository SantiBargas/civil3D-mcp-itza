# Archivos de prueba — Mensura / Subdivisión / Propiedad Horizontal / Tasaciones

Datos **reales** (no inventados), extraídos del plano de mensura real de ITZA
`docs/afip api joaco/ejemplo-ph-rocio/Plano_PH Ester-Plano.pdf` (propietaria Acosta, Blanca
Marcela — Calle 3 de Febrero, Paraná, Entre Ríos). Se usan acá porque ya están relevados y
confirmados — no hace falta pedirle nada nuevo a nadie para probar.

## Archivos

- **`001-mensura.csv`** — los 11 puntos del perímetro del lote, en formato PNEZD
  (`PointNumber,Northing,Easting,Elevation,Description`) — el formato estándar de importación de
  puntos de Civil 3D. Numerados 2001-2011 para no chocar con otros puntos de prueba que ya existan
  en el dibujo. La elevación quedó en 0.00 porque el plano original es un plano de mensura de
  lote (sin cota relevada) — si en algún momento se prueba con datos que sí tengan cota real,
  reemplazar esa columna.
- **`002-rumbos-distancias.csv`** — la tabla de rumbos y distancias tal cual figura en el plano
  (los mismos 11 lados, en el formato original: rumbo cuadrante + distancia en metros). Sirve para
  probar que la IA pueda hacer el cálculo de traverse **ella misma** vía `civil3d_geometry`
  (`cogo_traverse`), en vez de que se lo demos ya calculado como en `001-mensura.csv`.
- **`003-unidades-funcionales.csv`** — la Planilla de Unidades Funcionales del mismo plano (3
  unidades, superficies cubierta/semicubierta/descubierta y porcentual) — para probar el ángulo de
  propiedad horizontal/tasación (las superficies son la base de cualquier tasación o registro de
  PH).

**Verificado hoy (2026-09-21):** el cierre del polígono calculado a partir de
`002-rumbos-distancias.csv` da un error de **1.7cm en ~85m de perímetro** contra el punto de
partida — la tabla es correcta, no tiene errores de tipeo.

## Prompts de prueba (con Civil 3D 2026 abierto y el plugin de Sacred-G cargado)

Probar en este orden, de más simple a más completo:

1. **Importar puntos:**
   > "Importame los puntos de `proyecto/pruebas/mensura/001-mensura.csv` en el
   > dibujo, están en formato PNEZD."

2. **Traverse desde cero (más ambicioso, sin coordenadas pre-calculadas):**
   > "Con los rumbos y distancias de `002-rumbos-distancias.csv`, partiendo del punto 2001 en
   > (3000, 3000), calculame las coordenadas de cada vértice del polígono."

3. **Armar la parcela** (venía fallando hoy — usar para confirmar si sigue el bug o ya se
   arregló):
   > "Con esos puntos, armame el polígono cerrado de la parcela — es el Lote Acosta."

4. **Superficie/cómputo:**
   > "Decime la superficie y el perímetro de esa parcela."

5. **Propiedad horizontal / tasación:**
   > "Con los datos de `003-unidades-funcionales.csv`, decime qué porcentaje del total representa
   > cada unidad funcional y cuál es la superficie total del edificio."

6. **Etiquetas (sin probar todavía si funciona):**
   > "Etiquetá cada punto del lote con su número."

## Fuente y trazabilidad

Todo sale de un único documento real ya usado en otra parte del proyecto (Fase 2 de Trámites
ATER, `docs/afip api joaco/fase2-analisis.md`) — no son datos de prueba sintéticos. Si hace falta
un segundo caso real para comparar, ahí mismo hay otro material de referencia
(`docs/afip api joaco/ejemplo-ph-rocio/`) con los mismos 4 PDF (DJ, Ficha, plano, planilla).
