# 09-25 — Audio de Joaco (mañana) (ampliación: por qué no mensura, qué sí)

Texto textual, transcripción del audio (vía TurboScribe), sin editar. Contexto y resumen en
[`investigacion.md`](../../investigacion.md).

## Transcripción completa

> Igual no te sientas presionado, después te mandé para saber en qué estábamos, digamos nada
> más. Sí, lo que decía por ahí es que capaz vos me decías esto de parcela y demás porque por el
> hecho de la mensura, digamos, por ahí, por ese lado, que la mensura por ahí es mucho más
> jurídico, mucho más de etiquetas y de notas y detalles jurídicos más que una cuestión de
> ingeniería. Yo para no andar mucho ahí, por ahí está bueno eso que hiciste, ver que la
> herramienta esa se conecta, funciona y ver qué es lo que puede hacer dentro de Sibyl y cuáles
> son sus posibilidades.
>
> Pero dentro de Sibyl hay como distintos objetos nativos que tiene Sibyl, que son tuberías, obras
> lineales, superficies, parcelas, emplazamientos, alineaciones, líneas características. Son como
> un conjunto, un grupo de objetos nativos de Sibyl, propios de Sibyl, con lo que se pueden hacer
> muchas cosas. Las parcelas son objetos que se pueden derivar en un plano de mensura o una
> mensura, pero es un objeto de ingeniería, o sea, es como diseñar un loteo, diseñar una
> subdivisión y demás, no es tanto la mensura.
>
> La obra lineal se refiere a esas obras lineales, que pueden ser canales, pueden ser calles,
> pueden ser arbolados, pueden ser todo lo que sea alumbrado, todo lo que sea una cosa lineal
> que se pueda hacer una línea sobre una superficie, se trabaja con obras lineales. Las tuberías
> son todo lo que va abajo de la superficie, que pueden ser lineales también, pero que tienen
> forma de tubo, básicamente. Y las superficies son todo lo que es como el manto sobre el cual se
> pueden construir el resto de los objetos, una triangulación, un sistema TIN, digamos.
>
> Y bueno, para no andar tanto ahí, lo que hay que ver es una vez que enchufamos este MSP,
> digamos, a Sibyl, usamos Cloud, y ahí probar hasta dónde puede llegar a automatizar qué
> cosas, qué objetos, qué complejidades y demás. Si jugamos un caso así muy aislado, desde una
> persona, mi cliente te dice, che, mirá, yo tengo este terreno y quiero hacer un loteo. Un loteo
> lleva a hacer un diseño de la división, por dónde van a pasar las calles, cuánto van a ser los
> lotes, cuáles van a ser las manzanas.
>
> Sobre lo que uno proyecta de calle, hay que construir una obra lineal, que sería la calzada de la
> calle, o sea, la obra de la calle. Hay que diseñar el alumbrado público, hay que diseñar el
> arbolado, hay que diseñar el sistema de desagüe. Y después, como parte final del proceso,
> hacer la mensura para dividir todo eso.
>
> Toda esa parte de diseño de infraestructura, parte del diseño de una subdivisión, que vos podés
> tener un lote cuadrado, o sea, 100 por 100 pongámosle metros, vos podés tener un lote
> totalmente deformado, digamos. Lo cual, y vos tenés normativas municipales que tenés que
> cumplir, y decís, bueno, che, por acá la calle de ancho tiene que tener 15 metros, tiene que
> conectarse con otras calles y tiene que tener una manzana que tiene una longitud máxima de
> 120 metros. Y esos parámetros vos tenés que construir una subdivisión en una parcela X. Es
> complejo, digamos, no todo software, hoy la IA, si vos le pedís y le mandás un diseño, puede
> trabajarlo, pero es complejo porque son muchos parámetros a la vez.
>
> Quizás si uno enchufa eso, la IA, puede decir, che, bueno, hacemos un proyecto de división de
> esto. Definime las calles, definime los lotes que saldrían y demás. Y bueno, y sobre eso
> diseñame la calzada de la calle.
>
> Hace la obra lineal. Bueno, y sobre eso haceme el alumbrado público siguiendo la normativa
> tanto. Bueno, hay que ver si esa capacidad tan compleja está a la mano o no, o si queda por
> fuera.
>
> O hay que complementarlo con otra cosa. Si a vos te sirve, yo hice una estructura de cómo sería
> un flujo de trabajo completo de un proyecto de ingeniería integral, que quizás te suma para
> entenderlo un poco más de todos lados. Pero ya con lo que viste de BIM, lo que vieron con tu
> compañero también de BIM, cosas así, yo creo que hay sustancias como para que más o menos
> lo entiendas por dónde pasa la mano.
>
> Pero bueno, la mensura, por eso decía, no meterse en la mensura significa que no nos metamos
> en querer automatizar, no sé, las etiquetas de los lados, de la parcela, o que salga el plano
> automáticamente y cosas así, digamos, con la IA. Sino todo lo que es diseño de ingeniería.
> Diseño de un loteo, trazado de la obra lineal, trazado de las tuberías, trazado del arbolado,
> trazado de... que tienen muchos parámetros de ingeniería dura.
>
> Ver si a eso puede responder bien. La mensura me parece que es un capítulo... Aparte,
> pongámoslo entre comillas, hay que ver si también nos va el cuero con esto o hay que encarar
> por otro lado. Viste que ya tiene el sistema de AT, AFIP y todo eso, aunque haga el plano
> automáticamente la IA, en tema de mensura, la parte grosa está en el sistema de AFIP,
> digamos, de ARCA.
>
> Así que no nos metamos mucho ahí, por eso decía, no meternos ahí, es para profundizar
> bastante, metámonos en la parte de ingeniería y ver qué puede hacer. Pero primero enchufarlo,
> digamos, y ver qué se puede maniobrar adentro.

## Qué se resolvió con esto

Ver sección 7 (punto 9 actualizado) de
[`investigacion.md`](../../investigacion.md). Aclara y amplía lo dicho el
09-22: no es que "mensura" esté prohibida como objeto (parcela sí es un objeto de ingeniería,
para loteos/subdivisiones), sino que **no automatizar la parte jurídica/de trámite** (etiquetas de
lados, plano automático, envío a AFIP/ARCA — eso ya lo cubre el módulo ATER, en pausa). El caso
de prueba concreto que propone: loteo completo — dividir un terreno en calles/lotes/manzanas
cumpliendo normativa municipal (ancho de calle, longitud máxima de manzana), trazar la obra
lineal de la calzada, diseñar alumbrado público y arbolado, sistema de desagüe — y recién al
final, la mensura para dividir. Ofreció compartir un documento propio con el flujo de trabajo
completo de un proyecto de ingeniería integral (pendiente de pedírselo).

---

# 09-25 — Charla con Joaco (tarde, después de la prueba del loteo)

Conversación posterior a todo lo armado y probado ese día: la prueba completa del loteo con HMK
en 5 etapas, el informe `informes/Civil3D_MCP.pdf` y el `.rar` con los dibujos que se le mandaron
(ver sección 9 de [`investigacion.md`](../../investigacion.md)).
Transcripción del audio vía TurboScribe, textual y sin editar ("cloud" = Claude).

## Transcripción completa

> Boludo, boludo, muy sacado eso, ¿no te consumió tantos tokens, o sea, del cloud? Y más o
> menos, dite que vos tenés la sesión actual que se te restablece cada 5 horas y después la
> sesión semanal que se te restablece todos los domingos. En la sesión actual ponele que me
> consumió un 60% o 70% que para todo lo que hizo y el tiempo que llevó no me pareció bastante
> y eso que ni siquiera estaba entrenado ni nada y después en la sesión semanal ponele que me
> llevó Sí, ahí me dice en la sesión actual tipo en la que se restablece cada 5 horas un 60% o 70%
> y en la sesión semanal un 12% o 15% ponele. Muy sacado boludo, muy sacado, muy sacado.
>
> ¿Y qué prompts, entre comillas, o qué órdenes le diste, digamos? Y fueron varios porque era
> como que yo tengo un chat abierto que es como el orquestador, por así decirlo, que lo que me
> hace es mandarle este prompt a la otra gente entonces lo voy mandando y la otra gente que
> sería el constructor es lo que lo va haciendo. Lo tengo todo acá en memoria así que si querés te
> puedo armar ahí un archivo y te lo paso para que veas cómo fue la conversación. No, tranqui,
> tranqui, tranqui.
>
> Pero bueno, fueron prompts así medio ligeros, digamos, o lo elaboraste mucho. Eso que ahí dice
> de la normativa y demás, vaya que fue inventada, digamos, o sea, se podría cargar, che,
> respetar esta normativa, digamos, y la tiene en cuenta para diseñarlo. Si querés, el próximo paso
> sería, tipo, me pasas cosas bien formales de un proyecto real y yo te genero el archivo a ver qué
> sale, a ver si es lo que esperás.
>
> Yo creo que, bueno, con esto de pruebas se hace que funciona bastante bien, después cuando
> veas los archivos te vas a dar cuenta, pero bueno, si probamos con un proyecto real y sale bien,
> pero muy bueno, digamos. Sí, quizás lo que se puede hacer es yo te mando ahora un archivo
> que tiene toda una superficie de los canales y habría que decirle, bueno, generarme una
> sección, el tema es que no sé bien cómo decirle que respete qué cosas, porque hay unos PDFs
> viejos, digamos, que expresan un poco las secciones que deberían, cómo deberían ser y esa fue
> la que usamos para calcular la diferencia de volumen. Pero no sé si lo va a interpretar tan bien
> esos PDFs viejos, o si lo va a, bueno, porque hay varias cosas que tienen que ubicar, entender
> dónde están respecto a lo que está actualmente, y varias cositas que no sé si la vas a llegar a
> entender.
>
> Sí, no sé, vos si querés armate algo, pásamelo y lo pruebo, y después si querés, bueno, no sé si
> tenés alguna ahí, inteligencia paga, y bueno, nos conectamos y probamos a encargarlo y para
> que vos también lo puedas ir probando, si querés. Sí, de una, bueno, ya quiero probarlo. No, sí,
> sí, tema por ahí, sí, sí, sí.
>
> Acá me estaba pensando si está bueno conectarlo, o sea, tenemos chat gpt y cloud power 2,
> digamos, la de 20 dólares cada uno, pero estaba pensando por ahí, si viste que hay una cosa y
> tenés una duda, que hay una, viste, que vos pagás, creo, no sé, 100 dólares, ponele, y tenés
> chat y cloud paga a la vez, digamos, como que es un nucleador de guías, eso nunca me llegó
> tanto, nunca me quité tanto en esa, pero si no, capaz está bueno pagarla de 100 de chat y usarla
> para esto, debe ser muy pro, digamos, muy buena. Aunque cloud en términos numéricos capaz
> que es mejor, digamos, en términos de matemáticas y todo eso. Sí, olvidate, la verdad es poco
> cuál es mejor, y eso en cuanto a términos generales, decir esta mejor la otra, la verdad ni idea,
> porque viste que cada vez sale un modelo nuevo todo el tiempo y es como, bueno, probás este y
> se te anda mejor, para lo que vos haces específicamente, bueno, si es mejor para vos, pero para
> otras cosas no.
>
> Se podría analizar y ver bien qué onda, yo ahora estoy usando el cloud, que sacaron un nuevo
> modelo que es el Opus 5.5 y todo esto del Civil 3D lo hice con ese modelo y la verdad que muy
> bien, pero bueno, viste que también si vos pagás los 100 dólares del chat GPT, te dan el modelo
> de este, el Astra, que ese se me lo cubre también, pero bueno, ahí ya tendría que entrar a
> analizar cuál funciona mejor o no. Nada, sí, olvidate, igual estaría bueno ver qué onda ahí para
> seguir investigando eso de la IA, de los agentes, de los otros modelos, pero bueno, y quedamos
> así, entonces cuando quieras, si querés, armate algo ahí, un RAR o lo que sea, de cosas reales,
> con la normativa o lo que sea, de que vos ya sepas cómo funciona, y mientras tanto yo sigo
> investigando esto que tiene más funcionalidades por ver, así que voy viendo a ver qué más hay,
> y después bueno, probar el gratuito, a ver cómo funciona, y comparar el gratuito con este pago y
> ver qué se puede amoldar, digamos.

## Qué se resolvió con esto

- **Recepción:** a Joaco le pareció muy bueno el resultado de la prueba del loteo.
- **Consumo:** toda la prueba usó entre el 60 y el 70 % de la ventana de 5 horas del plan de
  Claude y entre el 12 y el 15 % del límite semanal.
- **La normativa se puede cargar:** Joaco confirmó que el enfoque sirve; con una normativa real,
  la IA la tendría en cuenta para diseñar.
- **Próximo caso real (lo arma Joaco):** un `.rar` con un proyecto real. Idea inicial: un archivo
  con la **superficie de los canales**, para pedirle que genere una **sección**. Las secciones de
  referencia están en **PDFs viejos**, que fueron los que se usaron para calcular la diferencia de
  volumen. Su duda: si la IA va a interpretar bien esos PDFs y ubicar cada cosa respecto de lo que
  existe hoy.
- **Planes pagos:** hoy tienen ChatGPT y Claude Pro de USD 20 cada uno. Se habló de planes de
  USD 100 o de un servicio que junte varios modelos; queda abierto comparar modelos para este uso
  (la prueba se hizo con Claude Opus 5.5).
- **Mientras tanto, Santi:** sigue explorando más funcionalidades de HMK y después prueba la
  opción gratuita (Sacred-G) para comparar gratuito contra pago.
