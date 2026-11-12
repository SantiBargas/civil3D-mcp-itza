# Loteo — datos de prueba INVENTADOS

Datos sintéticos para probar el caso de loteo que propuso Joaco el 09-25 (ver
[`../../reuniones/2026-09-25/TRANSCRIPCION.md`](../../reuniones/2026-09-25/TRANSCRIPCION.md)) mientras no haya un caso real de ITZA.
**No son datos de ningún proyecto real** — reemplazar por uno real apenas Joaco lo pase.

## Archivos

| Archivo | Qué es |
|---|---|
| `terreno-poligono.csv` | Vértices del perímetro del terreno (rectángulo 240 × 160 m = 3,84 ha), coordenadas locales en metros |
| `puntos-relevamiento.csv` | 35 puntos con cota (grilla de 40 m), formato PNEZD — para armar la superficie TIN |
| `normativa.md` | Parámetros municipales inventados que el loteo tiene que cumplir |
| `hmk-loteo/` | Prueba con **HMK CAD Pilot** (pago), 09-25 — etapas 1 a 5 (ver abajo) + `Civil3DMCP_loteo_HMK.rar` |
| `mcpitza-loteo/` | Prueba con **sacred-g-itza** (fork gratuito del MCP), 10-07 a 10-13 — etapas 1 a 6 + PDF de las láminas |

### `hmk-loteo/`

| Archivo | Qué es |
|---|---|
| `loteo-prueba-hmk-etapa1.dwg` | Etapa 1: puntos + superficie TIN + perímetro (recuperada del `.bak`) |
| `loteo-prueba-hmk-etapa2.dwg` | Etapa 2: + Site "Loteo" con 78 parcelas + ejes de calles |
| `loteo-prueba-hmk-etapa3.dwg` | Etapa 3: + obra lineal de la calle A (alineamiento, rasante, vista de perfil, ensamblaje, corredor y superficies de volumen) |
| `loteo-prueba-hmk-etapa4.dwg` | Etapa 4: + red pluvial "Pluvial Loteo" (7 cámaras, 7 sumideros, descarga y 14 conductos) |
| `loteo-prueba-hmk-etapa5.dwg` | Etapa 5: + 23 columnas de alumbrado y 140 árboles como bloques |

### `mcpitza-loteo/`

| Archivo | Qué es |
|---|---|
| `loteo-prueba-sacredgitza-etapa1.dwg` | Etapa 1: puntos + TIN + perímetro, resultados idénticos a HMK |
| `loteo-prueba-sacredgitza-etapa2.dwg` | Etapa 2: Site "Loteo" con 85 parcelas (80 lotes) + ejes |
| `loteo-prueba-sacredgitza-etapa3.dwg` | Etapa 3: obra lineal de la Calle 3 (eje, rasante, ensamblaje, corredor, vista de perfil) |
| `loteo-prueba-sacredgitza-etapa4.dwg` | Etapa 4: red pluvial "Pluvial" (13 cámaras, 22 sumideros, descarga, 35 conductos) |
| `loteo-prueba-sacredgitza-etapa5.dwg` | Etapa 5: + 20 columnas de alumbrado y 128 árboles como bloques |
| `loteo-prueba-sacredgitza-etapa6.dwg` | Etapa 6: + etiquetas con estilos propios y 4 láminas A3 (L1 loteo, L2 pluvial, L3 alumbrado y arbolado, L4 perfil) |
| `loteo-prueba-sacredgitza-etapa6.pdf` | Las 4 láminas de la etapa 6 en PDF |

Cada etapa se guarda como copia aparte antes de empezar la siguiente, para poder volver atrás.

**Los `.dwg` no están en git** (`.gitignore` excluye `*.dwg`, `*.rar`, `*.bak` y `*.dwl`): son
binarios pesados y quedan solo en la máquina local. Los de HMK se compartieron con Joaco en
`hmk-loteo/Civil3DMCP_loteo_HMK.rar`, junto con el informe `../../informes/Civil3D_MCP.pdf`, que describe qué
contiene cada dibujo.
Resultados detallados en la sección 9 de
[`../../investigacion.md`](../../investigacion.md).

## Características del terreno

- Coordenadas locales: origen del terreno en E 5000 / N 10000 (sin sistema de referencia real).
- Plano inclinado sin irregularidades: cota 100,00 en el vértice SO, baja 0,8 % hacia el E y
  1,2 % hacia el N. Punto más bajo en el vértice NE (96,16) — ahí debería desaguar el sistema
  pluvial.
- Rectangular a propósito, para una primera prueba. Si esta anda, la segunda debería ser un
  terreno irregular ("totalmente deformado", como dijo Joaco), que es donde la normativa se
  vuelve difícil de cumplir.

## Antes de usarlos en Civil 3D

Verificar que el dibujo esté en **metros** (`UNITS` / Configuración de dibujo de Civil 3D). Si se
abre con la plantilla por defecto puede estar en pies, y Sacred-G espera áreas en pies²
(`targetAreaSqFt`) en `civil3d_parcel lot_line_adjust`.
