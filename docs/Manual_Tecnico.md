# Manual Técnico - vmPing

## 1. Visión General de la Arquitectura
vmPing es una aplicación de escritorio de Windows (WPF) basada en **.NET Framework 4.7.2+**. Utiliza el patrón **MVVM (Model-View-ViewModel)** para una clara separación de responsabilidades. La aplicación es multi-hilo, permitiendo monitorear cientos de hosts simultáneamente sin bloquear la interfaz de usuario (UI).

## 2. Estructura del Proyecto

### Directorios Principales
*   `vmPing/`
    *   `App.xaml`: Punto de entrada y gestión de recursos globales.
    *   `MainWindow.xaml`: Lógica principal de la cuadrícula de monitores y orquestación.
    *   `Classes/`: Lógica de negocio y modelos.
        *   `Probe.cs`: **Componente Crítico**. Gestiona el hilo individual de ping/TCP para cada host.
        *   `Configuration.cs`: Gestión de configuración XML y serialización.
    *   `UI/`: Ventanas de la aplicación.
        *   `OptionsWindow.xaml`: Configuración de usuario.
        *   `TraceRouteWindow.xaml`: Herramienta de trazado de rutas.
        *   `FloodHostWindow.xaml`: Herramienta de inyección de paquetes.
        *   `StatusHistoryWindow.xaml`: Registro global de eventos.
    *   `Properties/`:
        *   `Strings.resx`: **Localización**. Contiene todas las cadenas de texto traducidas (Inglés/Español).

## 3. Componentes Clave y Nuevas Funciones

### Motor de Ping (`Probe.cs`)
*   Cada monitor en pantalla es una instancia de la clase `Probe`.
*   Soporta ICMP (System.Net.NetworkInformation.Ping) y TCP (Socket connection).
*   Implementa un bucle de fondo (`ThreadLoop`) que gestiona el intervalo, timeout y registro de estadísticas.
*   **Estado:** Usa un enum `ProbeStatus` (Inactive, Up, Down, Error, Indeterminate).

### Panel de Control (Dashboard)
*   Implementado en `MainWindow.xaml` como un `DockPanel` superior.
*   Utiliza *Data Binding* para mostrar contadores en tiempo real (`Probe.ActiveCount`, `Probe.DownCount`) calculados dinámicamente.

### Ventana Modal de Detalles (`ProbeDetailsModal` en `MainWindow.xaml`)
Se ha implementado una ventana modal interna (simulada con un Grid con ZIndex alto) para mostrar detalles del host sin abrir ventanas adicionales del SO.
*   **Ubicación:** Definido dentro de `MainWindow.xaml` (Grid `AddProbeModal`).
*   **Data Context:** Se vincula dinámicamente al objeto `Probe` seleccionado al hacer clic.
*   **Integración de Herramientas:**
    *   **TestPort_Click:** Utiliza `TcpClient.BeginConnect` asíncrono para probar puertos sin congelar la UI.
    *   **Telnet_Click:** Ejecuta `cmd.exe /c start telnet {host} {port}`.
    *   **RDP_Click:** Ejecuta `mstsc.exe /v:{host}`.
    *   **VNC_Click:** Intenta localizar `tvnviewer.exe` en 3 rutas comunes (Program Files x64/x86 y PATH).
    *   **Explorer_Click:** Abre `explorer.exe` con rutas UNC (`\\host\c$`).

### Ventana de Inundación (`FloodHostWindow.xaml`)
*   Herramienta asíncrona que envía pings en un bucle rápido (`while(isActive)`).
*   **Nota Técnica:** Se ejecuta en un hilo separado para mantener la UI responsiva. Incluye contadores de rendimiento (paquetes/segundo implícitos).

### Notificaciones Emergentes (`PopupNotificationWindow.xaml`)
*   Sistema personalizado de notificaciones "Toast" no nativas.
*   Las ventanas se gestionan en una colección estática para apilarse verticalmente en la esquina de la pantalla.
*   Soportan animaciones de entrada/salida mediante `Storyboard` en XAML.

## 4. Estilos y Temas

### Sistema de Temas Dinámico
*   Los temas se definen en `ResourceDictionaries/Themes.xaml`.
*   Usa `DynamicResource` en XAML para permitir el cambio de colores en caliente sin reiniciar.

### Localización (`Strings.resx`)
*   La aplicación ha sido internacionalizada.
*   Todo el texto visible se obtiene mediante `x:Static resource:Strings.Key` en XAML.

## 5. Compilación y Despliegue

### Requisitos de Desarrollo
*   **IDE:** Visual Studio 2019/2022.
*   **Framework:** .NET Framework 4.7.2 o superior.

### Instrucciones de Build
1.  Abrir la solución `vmPing.sln`.
2.  Restaurar paquetes NuGet (si aplica).
3.  Compilar en modo `Release`.
4.  **Salida:** Un único portable `.exe` en `bin/Release/`.

## 6. Mantenimiento y Extensión

### Agregar una Herramienta Nueva a la Modal
1.  Modificar `MainWindow.xaml` dentro del Grid `ProbeDetailsModal` para agregar el botón.
2.  En `MainWindow.xaml.cs`, agregar el manejador de eventos (Click).
3.  Usar `ProbeDetailsModal.DataContext as Probe` para obtener la IP/Host.
4.  Invocar el proceso externo con `Process.Start`.
