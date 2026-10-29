# Manual de usuario de Windows Vault

Windows Vault tiene tres páginas: **Vault** bloquea aplicaciones nativas, **Clasificador** etiqueta contenido compatible del navegador y **Actividad** muestra el uso registrado. La extensión recopila contenido compatible y aplica el bloqueo del navegador. Instálela y conéctela en el navegador que utiliza.

## Primeros pasos

1. En **Vault**, añada un grupo de bloqueo y un objetivo de aplicaciones; después seleccione aplicaciones con el selector +.
2. Elija el comportamiento de bloqueo del grupo y actívelo.
3. En **Clasificador**, cree un grupo, elija sus plataformas y añada etiquetas con descripciones.
4. Elija un nivel de modelo local y descárguelo si es necesario. Active el etiquetado en los ajustes del Clasificador y reanude el grupo.
5. Abra contenido compatible en el navegador conectado. Configure un filtro de etiquetas en un grupo de bloqueo del navegador si desea que las etiquetas controlen el bloqueo.

## Grupos de bloqueo

Un **grupo de bloqueo** aplica una política de bloqueo. Un **grupo del Clasificador** asigna etiquetas al contenido; por sí mismo no bloquea nada.

1. Añada un grupo de bloqueo y asígnele un nombre.
2. Elija objetivos en **Se aplica a**.
3. Elija cuándo se aplica el bloqueo y configure los horarios o el tiempo permitido necesarios.
4. Active el grupo. Sus objetivos comparten la política del grupo.

Los cambios habituales se guardan automáticamente. Un error significa que el cambio no se ha aceptado; corrija el campo e inténtelo de nuevo. Desactive un grupo para detener su política y conservar su configuración. **Eliminar grupo** lo elimina. Arrastre los grupos para reordenarlos. Varios grupos pueden aplicarse a un objetivo; posponer uno no levanta el bloqueo de otro.

**Exportar** copia la configuración de un grupo. **Importar** sustituye la configuración del grupo seleccionado tras la confirmación.

### Tiempo permitido y horario

**Bloquear inmediatamente** se aplica siempre que el grupo activado coincida y su horario esté activo. **Bloquear al agotar el tiempo permitido** permite el uso correspondiente hasta que se agota el tiempo disponible.

Configure el tiempo permitido en minutos y el intervalo de restablecimiento en horas. Un límite móvil cuenta el uso dentro de la ventana anterior. Restablecer a medianoche inicia un nuevo período a la medianoche local, también con un límite móvil.

Elija los días activos de la semana y, opcionalmente, intervalos de hora local, uno por línea, como **09:00-12:00**. Una lista de intervalos vacía se aplica durante todo el día en los días seleccionados. Un intervalo debe terminar después de empezar en el mismo día; divida un horario nocturno entre días separados.

### Posponer

Configure la opción de posponer en cada grupo de bloqueo. **Pausar bloqueo** suspende la política de ese grupo durante la duración de la pausa. **Añadir al tiempo permitido** añade minutos utilizables a un grupo con límite de tiempo. Solo el tiempo adicional consumido cuenta como tiempo pospuesto. El tiempo adicional sin usar caduca en el siguiente restablecimiento; con un límite móvil, después de una ventana, o antes a medianoche si esa opción está activada.

**Retraso de activación** retrasa la pausa mientras continúa el bloqueo. **Tiempo de espera** es la espera tras finalizar la pausa antes de otra solicitud. **Confirmaciones requeridas** establece el número de pasos de confirmación. Posponer está disponible en un grupo congelado solo si se permitió antes de congelarlo.

### Bloqueo de edición y PIN

**Bloquear edición** impide los cambios habituales. Desbloquear la edición requiere diez confirmaciones separadas por cinco segundos, además de la espera configurada y el PIN de seis dígitos, si existen. **Espera antes de desbloquear la edición** acepta 0–72 horas; 0 no añade espera.

Mientras el grupo está congelado, se puede ampliar la espera y añadir un PIN si no existe ninguno. Estas condiciones no se pueden debilitar hasta descongelar el grupo. La eliminación también respeta la espera restante y el PIN.

### Grupos vinculados

Use **Vincular** para conectar grupos seleccionados explícitamente en otros programas Vault. Los grupos vinculados comparten nombre, ajustes de política compatibles, objetivos, uso y condiciones de congelación. Cada programa edita y aplica los tipos de objetivo que admite; las demás entradas siguen disponibles para los programas vinculados. Al desvincular, cada grupo y sus ajustes se conservan.

Si un miembro vinculado está desconectado, la edición puede no estar disponible. Abra Windows Vault y el navegador vinculado para reconectarlos. Una política guardada localmente puede seguir aplicándose mientras un miembro está desconectado.

## Obtener ayuda

Pulse la pequeña **i** junto a un campo para ver su explicación. Pulse fuera o presione Escape para cerrarla. Las listas permanecen en cuadros desplazables; desplácese dentro del cuadro para acceder a más entradas. La búsqueda filtra la lista visible sin eliminar entradas.

Las reglas personalizadas tienen su propio [Manual de código](../code-manual/es.md). Explica el editor, la activación, los registros, el acceso a archivos y la API compatible.

## Aplicaciones nativas

Use el selector + de un objetivo de aplicaciones para seleccionar aplicaciones instaladas. **Bloquear todas las aplicaciones excepto estas** convierte la lista en una lista de permitidos. Las aplicaciones del sistema, los navegadores y el propio Vault se excluyen del bloqueo de aplicaciones nativas.

Se solicita a una aplicación bloqueada que se cierre. **Volver a pedir el cierre de una aplicación bloqueada cada (minutos)** controla los reintentos. La extensión del navegador aplica las redirecciones de sitios, las pausas de página y la ocultación de fuentes; estas no se convierten en acciones de aplicaciones nativas.

## Etiquetado de contenido

Un grupo Etiquetado de contenido etiqueta contenido de sus plataformas asignadas con su árbol de tags y modelos. Cada plataforma pertenece a un grupo; elígelas al crearlo, pues no se cambian después. Los horarios y filtros de bloqueo no controlan el etiquetado. Actívalo en Ajustes y pausa/reanuda cada grupo por separado. Desactivar el registro de un feed en **Actividad → Grabación** también detiene su etiquetado.

### Tags y ajustes del modelo

Crea tags, describe su significado y define o quita sus padres. Arrastrar mueve la rama. **Confianza mínima de la etiqueta** fija el grado de evidencia de 1 a 5; los grupos nuevos empiezan en 4 (apoyo sólido). No hay cantidad objetivo; un elemento puede no recibir tags. **Instrucciones de etiquetado** guarda las preferencias. Los cambios se guardan automáticamente; cada grupo usa una conexión, modelo y esfuerzo de razonamiento elegidos expresamente.

### Conexiones de IA

En **Conexiones de IA** hay ChatGPT subscription, OpenAI API, Gemini API y Grok API. La suscripción usa el inicio de sesión de Vault y un plan válido; las claves API siguen la facturación y límites del proveedor. Elige proveedor e introduce la clave debajo del menú compartido; se guarda automáticamente y borrarla elimina la clave. **Obtener lista de modelos** obtiene modelos pero no selecciona uno. **Nivel de razonamiento** ofrece predeterminado, Low, Medium o High según compatibilidad; nuevas opciones comienzan en Medium y se conservan las existentes. Más esfuerzo puede tardar y usar tokens. La búsqueda web siempre está habilitada para etiquetar; el modelo decide cuándo usarla. Si falla, se conservan resultados válidos en caché; nuevos elementos quedan pendientes hasta **Reintentar clasificación en la nube**. No hay cambio automático de proveedor; cada conexión tiene intervalo y límite de intentos propios. En la extensión corrige con **+ tag**; usa el control de quitar o Delete una vez. **Sin etiqueta** significa terminado sin tags; **Etiquetando**, pendiente. Tu corrección prevalece.


## Actividad

Actividad registra localmente el uso de apps habilitadas, visitas web y **Contenido visto** compatible. Los gráficos reflejan datos registrados; un área vacía no demuestra que el PC estuviera inactivo. Elige un intervalo de fechas. **Cronología** muestra el uso por hora del día; **Totales** suma duración; **Intervalo de tiempo** agrupa el uso de cada intervalo en bloques verticales. **Colores** es una leyenda interactiva: selecciona una fuente para enfocar los gráficos. Elige un día para ver el uso desde esa fecha.

### Grupos de Actividad

Crea un grupo para mostrar juntas en Usage las apps y sitios seleccionados. **Merge** usa un nombre y color para sus miembros en todo Actividad. El grupo organiza el uso registrado y es distinto de los grupos de bloqueo y Etiquetado de contenido. Guarda el editor explícitamente con **Save**.

### Grabación y retención

En **Grabación**, activa o desactiva la grabación por categoría o fuente individual. **Conservar** determina cuánto se conserva el historial; **Siempre** evita el vencimiento automático. Las opciones individuales pueden seguir el ajuste general. Desactivar la grabación detiene nuevos registros; borrar el historial elimina las entradas registradas. Los feeds de plataforma recopilan contenido mostrado en páginas compatibles, se hayan abierto o no. Un feed **Etiquetado compatible** puede suministrar contenido a Etiquetado de contenido mientras la grabación esté activa. Su retención es independiente del uso de apps y sitios. Pausar un grupo de etiquetado no detiene por sí solo la grabación.

## Ajustes

Elige el idioma de interfaz en Ajustes. Los botones Info muestran explicaciones en el idioma seleccionado.

## Guardado y solución de problemas

Los cambios habituales de Vault y del Clasificador se guardan automáticamente. La edición de grupos de actividad usa **Guardar**. Añadir, eliminar, descargar un modelo, probar una conexión y obtener una lista de modelos siguen siendo acciones explícitas.

Si faltan etiquetas, compruebe la conexión del navegador, el interruptor global de etiquetado, la pausa del grupo, el registro de la plataforma y la descarga del modelo. Si no se ejecuta la investigación, compruebe el consentimiento, la opción del grupo, las credenciales del proveedor, el modelo de investigación y el estado de investigación. Si no se puede editar un grupo vinculado, reconecte sus programas o desbloquee su edición según lo indicado.
