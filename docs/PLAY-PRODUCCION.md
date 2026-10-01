# Solicitud de acceso a producción en Google Play — Music Player

Respuestas para el cuestionario de Play Console › **Panel › Solicitar acceso a producción**, en
catalán (el idioma de la consola). Cada texto cabe en los 300 caracteres del formulario; el número
entre paréntesis es su longitud. Constitución Mobile §11. **Última actualización: 2026-10-01**
(versión 2026.09.30.0). Estado en Play: prueba cerrada (alpha 2026.09.14.1 publicada; en la pista desde el 2026-09-07).

> Lo marcado con ⚠ no lo puedo saber yo: compruébalo en la consola antes de enviarlo y cámbialo si
> no es así.
>
> - ⚠ En alpha está la **2026.09.14.1**; la 2026.09.28.0 (atrás y errores) y la 2026.09.30.0 (grupos duplicados, red lenta, 181 pruebas) no están subidas: súbelas antes o quita esa parte de los cambios.
> - ⚠ Tiene un quinto grupo de verificadores (`closedtestproapp`) además de los cuatro estándar.

---

## Informació sobre la prova tancada

**Com has reclutat usuaris per a la prova tancada?** (291)

```
He afegit a la prova tancada quatre grups públics de Google de verificadors voluntaris (comunitats d'intercanvi de proves de 12 persones durant 14 dies). No he fet servir cap proveïdor de pagament. També l'he provada en un mòbil real amb Android 16 i en un simulador de cotxe d'Android Auto.
```

**Fins a quin punt t'ha resultat fàcil reclutar verificadors?** — Propuesta: **Ni fàcil ni difícil** (los grupos públicos dan el número, pero participan poco).

**Descriu la implicació dels verificadors** (249) ⚠ *comprueba en Estadísticas / Prova tancada que de verdad la abrieron; si no hay datos, quita la parte de las funciones.*

```
Els verificadors han instal·lat l'app i l'han feta servir amb la música del seu mòbil: biblioteca per grups, llistes, preferides i reproducció en segon pla. Un usuari real també l'usaria al cotxe amb Android Auto, que pocs verificadors deuen provar.
```

**Resum dels suggeriments i com els has recollit** (250) ⚠ *si algún verificador dejó comentarios (en la consola o por correo), menciónalos.*

```
Pocs comentaris escrits dels verificadors; els he recollit des de la consola de Play i GitHub. Les millores han sortit de la revisió de Google per a Android Auto, de les meves proves al mòbil i al simulador de cotxe i del banc de proves automàtiques.
```

## Informació sobre l'aplicació

**A quin públic objectiu va dirigida?** (212)

```
Persones que tenen la seva música en fitxers al mòbil (MP3, FLAC, OGG...) i la volen escoltar sense subscripcions, també al cotxe amb Android Auto. Sense anuncis, sense compte i sense enviar res si no ho activen.
```

**Com proporciona valor als usuaris?** (248)

```
Reprodueix la música del mòbil agrupada per grup o compositor, amb llistes, preferides, lletra sincronitzada i caràtules, i tota la biblioteca al cotxe amb Android Auto i els controls del volant. La cerca de fotos de grups és opcional i ve apagada.
```

**Instal·lacions esperades el primer any** — Propuesta: **0 - 10.000** (app nueva, sin promoción).

## Preparació per a la producció

**Quins canvis has fet en funció de la prova tancada?** (249) ⚠ *atrás, errores, grupos y 181 pruebas son de la 2026.09.28.0 y la 2026.09.30.0, que no están en alpha.*

```
Android Auto ja explica què cal fer sense permís o sense música (motiu d'un rebuig de Google), surten les caràtules al cotxe, hi ha preferides, el botó enrere a Android 16 amb la música sonant, un gestor d'errors, grups sense duplicats i 181 proves.
```

**Com has decidit que està preparada per a producció?** (242) ⚠ *comprueba en Qualitat › Android Vitals que no hay fallos; si los hay, quita «sense tancaments a la consola».*

```
Les 181 proves automàtiques passen totes, l'he provada en un mòbil real amb Android 16 sense errors, els verificadors l'han fet servir 14 dies sense tancaments a la consola i la fitxa, la privadesa i la seguretat de les dades estan completes.
```
