# Informe ITZA360 Agéntica — LaTeX

Carpeta del informe LaTeX del proyecto ITZA360 Agéntica. Compilar con **xelatex**
(no pdflatex — la fuente LuxSans es TTF y requiere fontspec).

## Estructura

```
informes/
├── Civil3D_MCP.tex   ← informe de la prueba de loteo con IA en Civil 3D
├── fonts/            ← fuentes TTF (no editar)
│   ├── LuxSans-Regular.ttf
│   ├── LuxSans-Bold.ttf
│   ├── LuxSans-Light.ttf
│   └── LuxSans-Black.ttf
└── README.md         ← este archivo
```

## Compilar

```powershell
cd proyecto\informes
xelatex Civil3D_MCP.tex   # dos veces si hay TOC o referencias cruzadas
```

En VS Code con LaTeX Workshop: el comentario `% !TEX program = xelatex` en la primera
línea del `.tex` hace que el builder lo compile automáticamente con XeLaTeX.

---

## Referencia de formato

### Portada

```latex
\begin{center}
  {\huge\bfseries\color{azulPrincipal} Título principal \par}
  \vspace{0.3cm}
  {\large Subtítulo del documento \par}
  \vspace{0.2cm}
  {\small Autor --- Organización \par}
  \vspace{0.1cm}
  {\small Fecha \par}
\end{center}
\vspace{0.5cm}
\hrule
\vspace{0.8cm}
\tableofcontents
\newpage
```

### Tabla estándar (booktabs + tabularx + colortbl)

```latex
{\renewcommand{\arraystretch}{1.4}
\setlength{\aboverulesep}{0pt}
\setlength{\belowrulesep}{0pt}
\begin{tabularx}{\textwidth}{>{\raggedright\arraybackslash}p{3cm} >{\raggedright\arraybackslash}X >{\raggedright\arraybackslash}p{2.5cm}}
\toprule
\rowcolor{grisClaro}
\textbf{Columna 1} & \textbf{Columna 2} & \textbf{Columna 3} \\
\hline
Dato A & Descripción larga que ocupa el espacio disponible & Valor \\
\hline
Dato B & Otra descripción & Otro valor \\
\bottomrule
\end{tabularx}}
```

Si la tabla puede no entrar en lo que queda de la página, usar `xltabular` en vez de `tabularx`
(misma sintaxis, ya cargado en el preámbulo): se parte entre páginas en vez de saltar entera a la
siguiente. Poner `\endhead` después de la fila de encabezado para que se repita en cada página.

**Tipos de columna disponibles:**

| Especificador | Comportamiento |
|---|---|
| `p{Xcm}` | Ancho fijo, texto justificado |
| `X` | Ancho flexible (rellena el espacio sobrante) |
| `>{\raggedright\arraybackslash}X` | Flexible, texto alineado a la izquierda |
| `>{\centering\arraybackslash}p{Xcm}` | Fijo, centrado |

### Tabla con monto resaltado (recuadro al pie)

```latex
\begin{flushright}
{\setlength{\fboxrule}{0.5pt}\setlength{\fboxsep}{12pt}
\fcolorbox{grisClaro}{white}{%
  \begin{minipage}{0.4\textwidth}
    \centering
    {\huge\bfseries\color{azulPrincipal} USD 2.350} \\[0.3cm]
    {\small\itshape Válido desde el 01/10/2026}
  \end{minipage}%
}}
\end{flushright}
```

### Listas

```latex
% Lista simple
\begin{itemize}[itemsep=4pt]
  \item Primer ítem
  \item Segundo ítem
\end{itemize}

% Lista numerada
\begin{enumerate}[itemsep=6pt]
  \item Primer paso
  \item Segundo paso
\end{enumerate}
```

### Colores disponibles

| Nombre | RGB | Uso |
|---|---|---|
| `azulPrincipal` | 28, 61, 90 | Títulos, totales, acentos |
| `grisClaro` | 230, 230, 230 | Encabezados de tabla, bordes |

Agregar colores nuevos en el preámbulo:

```latex
\definecolor{verdeOk}{RGB}{40, 167, 69}
\definecolor{rojoBug}{RGB}{220, 53, 69}
```

Usarlos en texto: `{\color{verdeOk}\textbf{Completa}}`

### Código inline y rutas

```latex
\texttt{nombre\_de\_comando}   % fuente monoespaciada
\url{https://ejemplo.com}      % link clickeable (requiere hyperref)
```

### Espaciado entre secciones

El preámbulo ya configura `\titlespacing` para que no haya espacio antes de cada sección.
Para ajustar manualmente:

```latex
\vspace{0.5cm}    % espacio vertical
\newpage          % salto de página forzado
\hrule            % línea horizontal
```

### Notas tipográficas

- **Fuente**: LuxSans no tiene itálica real — se simula con `FakeSlant=0.2` sobre Regular/Bold.
  Funciona bien para énfasis suave; evitar itálicas en bloques largos.
- **Compilador**: siempre xelatex. Si se compila con pdflatex, falla el `\setmainfont`.
- **Caracteres especiales en español**: el preámbulo carga `babel[spanish]` + `inputenc[utf8]`,
  así que las tildes y la ñ se escriben directo (`á`, `ñ`, etc.), sin escapes como `\'a`.
