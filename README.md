# 📡 VM-Ping GLR Edition

![Estado](https://img.shields.io/badge/Estado-Activo-success) ![Plataforma](https://img.shields.io/badge/Plataforma-Windows-blue) ![Licencia](https://img.shields.io/badge/Licencia-MIT-green)

> **Tu Centro de Mando de Red Personalizado.**
> Monitoreo visual de alto rendimiento y caja de herramientas de diagnóstico en una sola ventana.

---

## 🧐 ¿Qué es VM-Ping GLR?

**VM-Ping GLR** es una evolución de la utilidad clásica de ping gráfico. No solo te dice si un host está activo; te da el control total sobre tus conexiones.

Diseñado para administradores de TI, soporte técnico y entusiastas de redes que necesitan vigilar docenas de servidores y, al mismo tiempo, tener acceso rápido a ellos sin llenar su barra de tareas de ventanas.

## 🚀 Características Principales

### 👁️ Monitoreo Visual Intuitivo
- **Código de Colores:** Verde (Activo), Rojo (Caído), Naranja (Error). Identifica problemas en milisegundos.
- **Grid Dinámico:** La ventana se adapta automáticamente, ya sea que monitorees 2 servidores o 50.
- **Pings TCP:** ¿El servidor responde al ping pero el servicio web no? Monitorea puertos específicos (ej. `Servidor:80`).

### 🛠️ Caja de Herramientas (Command Center)
Olvídate de abrir consolas por separado. Desde la misma interfaz:
- **Diagnóstico de Red:** Ejecuta **Traceroute** y **Flooding** para pruebas de estrés.
- **Utilidades Integradas:** Acceso directo a Telnet, Web, recursos compartidos, inventario y salud manual.
- **Consultas Manuales:** El inventario y la salud WMI/SNMP solo se ejecutan cuando el usuario lo solicita.

### 🔔 Alertas Inteligentes
- **Notificaciones Popup:** Entérate al instante si algo cambia de estado.
- **Alertas por Correo:** Configura avisos automáticos para cuando no estés frente a la pantalla.
- **Registro de Eventos:** Log de texto para auditoría de caídas.

---

## 📥 Instalación

1. Ve a la sección de [Releases](https://github.com/GabrielLop3z/vmPing-GLR/releases) (próximamente).
2. Descarga el ejecutable `vmPing-GLR.exe`.
3. ¡Listo! Es portable, no requiere instalación.

---

## 🎮 Controles Rápidos

| Acción | Comando / Atajo |
| :--- | :--- |
| **Añadir Host** | Escribe la IP/Host y presiona `Enter` |
| **Panel de Herramientas** | `F12` o Clic en el icono de menú |
| **Modo Compacto** | `F11` |
| **Ping a Puerto** | Escribe `HOST:PUERTO` (ej. `192.168.1.10:3389`) |

---

## 🤝 Créditos y Licencia

Este proyecto es un fork personalizado basado en el excelente trabajo de [Ryan Smith (vmPing)](https://github.com/r-smith/vmPing).
Distribuido bajo la licencia **MIT**. Siéntete libre de usarlo, modificarlo y compartirlo.

---
*Desarrollado y personalizado por Gabriel Lopez Reyes (GLR).*