# Manual de Usuario - vmPing (Visual Multi Ping)

## 1. Introducción
vmPing (Visual Multi Ping) es una utilidad gráfica de ping para monitorear múltiples hosts. Se pueden agregar y eliminar numerosos monitores de host, y cada monitor cambia de tamaño dinámicamente con la ventana de la aplicación. La codificación por colores le permite ver el estado de cada host de un vistazo.

Esta versión incluye soporte completo en **Español** y nuevas características visuales.

## 2. Puesta en Marcha
vmPing es una aplicación portátil. No requiere instalación.
*   **Inicio:** Simplemente descargue el archivo ejecutable (`vmPing.exe`) y ejecútelo.
*   **Configuración:** Al guardar cambios, se puede crear un archivo `vmPing.xml`. Si selecciona "Modo Portátil", este archivo se guardará junto al ejecutable.

## 3. Interfaz Principal
La interfaz está diseñada para ser intuitiva y rica visualmente.
*   **Barra de Menú:** Acceso rápido a funciones como Agregar Host, Iniciar/Detener todo, y Opciones.
*   **Panel de Control (Dashboard):** [NUEVO] Un panel superior que muestra estadísticas en tiempo real:
    *   **Total:** Nodos monitoreados.
    *   **Activos (Verde):** Hosts respondiendo.
    *   **Caídos (Rojo):** Hosts que no responden.
    *   **Latencia:** Promedio de latencia global.
*   **Control Deslizante de Columnas:** Permite ajustar cuántas columnas de monitores se muestran en la cuadrícula.

## 4. Uso Básico

### Agregar Hosts
1.  **Entrada Rápida:** Escriba una dirección IP (ej. `192.168.1.1`), un nombre de host (ej. `google.com`) o una dirección con puerto (ej. `google.com:80`) en el cuadro de texto.
2.  **Entrada Múltiple:** Use el menú *Entrada Múltiple* (F2) para agregar varios hosts a la vez.

### Interpretación Visual (Estado)
*   **Verde (Activo/Up):** El host responde correctamente.
*   **Rojo (Caído/Down):** El host no responde al ping o el puerto está cerrado.
*   **Naranja (Error/Indeterminado):** Problema de resolución DNS o error de red.
*   **Animaciones:** Los monitores "pulsan" sutilmente al actualizarse.

### Gestionar Monitores
*   **Alias:** Puede asignar un nombre amigable a cualquier IP (ej. "Servidor DNS" para 8.8.8.8) haciendo clic en el icono del lápiz.
*   **Vista Aislada:** Haga clic en el icono de ventana en un monitor para abrirlo en una ventana separada flotante.
*   **Historial:** Haga clic en cualquier monitor para ver un gráfico detallado de latencia y un historial de eventos.
*   **Ventana Modal de Detalles:** Haga clic en el cuerpo de la tarjeta de un host para abrir una ventana modal con estadísticas ampliadas y herramientas.

## 5. Herramientas Rápidas de Diagnóstico (Ventana Modal)

Al hacer clic en cualquier tarjeta de host se abre una **Ventana Modal de Detalles**. Esta ventana incluye contadores grandes y botones de acceso rápido para herramientas externas:

### Herramientas de Red:
*   **Test Port (Prueba de Puerto):**
    *   **Función:** Verifica si un puerto TCP específico está abierto en el host remoto.
    *   **Uso:** Ingrese el número de puerto (por defecto 80) y haga clic en "Test".
    *   **Resultado:** Muestra "OPEN" (Verde), "CLOSED" (Rojo) o "TIMEOUT".

*   **Traceroute (Traza de Ruta):**
    *   **Función:** Inicia una traza de ruta visual hacia ese host.
    *   **Uso:** Muestra cada salto de red hasta llegar al destino.

### Herramientas de Acceso Remoto:
*   **Telnet:**
    *   **Función:** Abre una ventana de comandos (`cmd`) e intenta iniciar una sesión Telnet al puerto especificado.
    *   **Requisito:** El cliente Telnet debe estar habilitado en Windows (`Funciones de Windows -> Cliente Telnet`).

*   **RDP (Escritorio Remoto):**
    *   **Función:** Lanza la herramienta nativa de conexión a Escritorio Remoto (`mstsc.exe`).
    *   **Uso:** Se conecta automáticamente al host/IP seleccionado.

*   **VNC (Virtual Network Computing):**
    *   **Función:** Intenta lanzar `TightVNC Viewer`.
    *   **Lógica:** Busca `tvnviewer.exe` en las rutas de instalación estándar (`Program Files\TightVNC` o `Program Files (x86)\TightVNC`) o en el PATH del sistema.

*   **Web:**
    *   **Función:** Abre el navegador predeterminado del sistema.
    *   **Uso:** Intenta navegar a `http://<host>`.

### Herramientas de Archivos:
*   **Disco C (Admin Share):**
    *   **Función:** Abre el explorador de archivos en la ruta administrativa oculta `\\host\c$`.
    *   **Requisito:** Requiere credenciales administrativas en el equipo remoto.

*   **Compartido (Shares):**
    *   **Función:** Abre el explorador de archivos en la ruta raíz `\\host\`.
    *   **Uso:** Muestra todas las carpetas compartidas visibles del equipo remoto.

## 6. Herramientas Avanzadas (Menú Principal)

### Traza de Ruta (Trace Route)
Accesible desde el menú **Trazar Ruta**.
*   Herramienta independiente para realizar traceroutes a cualquier destino, no solo a los monitoreados.

### Inundación de Host (Flood Host)
Accesible desde el menú **Inundar Host**.
*   **Efecto:** Envía pings continuos de alta frecuencia.
*   **Uso:** Pruebas de pérdida de paquetes bajo carga o verificación de reglas de firewall.

### Ping a Puertos (Modo TCP)
*   Si agrega un host con el formato `host:puerto` (ej. `192.168.1.50:3389`), vmPing funcionará exclusivamente en modo TCP Ping, verificando la conectividad al puerto en lugar de respuestas ICMP.

## 7. Configuración y Personalización

### Temas Visuales
Personalice la apariencia en **Opciones -> Interface**. Temas nuevos incluidos:
*   **Professional Light/Dark:** Estilos limpios y corporativos.
*   **Ocean Blue:** Tonos azules relajantes.
*   **Sunset Orange:** Tonos cálidos.
*   **Hacking (Matrix):** Tema retro verde sobre negro para entusiastas.

### Notificaciones y Alertas
*   **Popups:** Tarjetas flotantes modernas en la esquina de la pantalla. Configure si aparecen "Siempre", "Nunca" o "Al Minimizar".
*   **Email:** Configure alertas por correo SMTP para servidores críticos.
*   **Audio:** Asigne sonidos personalizados para eventos de caída y recuperación.

## 8. Atajos de Teclado
*   **F1:** Ayuda
*   **F2:** Entrada Múltiple
*   **F5:** Iniciar/Detener Todo
*   **F10:** Opciones
*   **F11:** Pantalla Completa (Toggle)
*   **F12:** Historial de Estado Global
*   **Ctrl+N:** Nueva Instancia
*   **Ctrl+T:** Trazar Ruta
*   **Ctrl+F:** Inundar Host

## 9. Solución de Problemas
*   **Firewall:** Si todos los pings fallan, verifique que el Firewall de Windows no esté bloqueando vmPing.exe.
*   **Ejecución:** No requiere permisos de administrador para pings básicos, pero sí para algunas funciones avanzadas de red (como Flood Host).
*   **VNC falla:** Si VNC no abre, asegúrese de tener TightVNC instalado en la ruta por defecto o que el ejecutable esté en las variables de entorno (PATH).
