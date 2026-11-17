# API HTTP de vmPing

vmPing expone sus datos de monitoreo por HTTP para que otras aplicaciones (bitácoras, paneles, inventarios, scripts) consulten el estado de un equipo, hagan un ping bajo demanda o recuperen el historial de días anteriores.

La API corre **dentro de vmPing** y escucha por defecto en `http://127.0.0.1:8099`. No requiere permisos de administrador, ni IIS, ni reservas `netsh`/URL ACL.

---

## 1. ¿Dónde están los datos?

Desde la versión 1.8.9.7 el historial se guarda en **SQLite**:

| Modo | Archivo |
|---|---|
| Normal | `%LOCALAPPDATA%\vmPing\vmPing.db` |
| Portable (hay `vmPing.xml` junto al exe) | `<carpeta de vmPing>\vmPing.db` |

Tablas principales:

| Tabla | Contenido |
|---|---|
| `hosts` | Hosts vistos, alias y primera/última vez |
| `probe_samples` | Cada muestra de ping: fecha UTC, éxito, latencia, error |
| `status_changes` | Cambios de estado (Up, Down, Error, Start, Stop…) |
| `audit_log` | Auditoría de la aplicación |
| `report_runs` | Envíos de reportes |

La retención se configura en **Opciones → Reportes → Retención (días)**. Por defecto son 90 días. Mientras el dato no se haya purgado, se puede consultar cualquier día anterior.

> La API **solo lee** el historial. Para escribir datos nuevos use la aplicación o `GET /api/ping?...&save=true`.

---

## 2. Activar la API

1. Abra vmPing → **Herramientas → Opciones → API**.
2. Marque **Habilitar API HTTP**.
3. Puerto: `8099` (puede cambiarlo).
4. Escuchar en:
   - **Solo este equipo (127.0.0.1)**: recomendado si la bitácora corre en el mismo equipo o si un backend hará la consulta.
   - **Red local (0.0.0.0)**: para que otros equipos/servidores consulten directamente. **Exige clave API**.
5. Configure una **Clave API** con el botón **Generar**.
6. Opcional: **Permitir CORS** solo si una página web (JavaScript del navegador) llamará a la API desde otro origen.
7. **Importante:** marque **Guardar como predeterminados de vmPing** antes de aceptar, para que la API y la clave se conserven al reiniciar.
8. Acepte y abra **Herramientas → API / Integraciones** para ver la URL y el estado.

También puede probar abriendo en el navegador:

```text
http://127.0.0.1:8099
```

---

## 3. Autenticación

Si configuró una clave, envíe una de estas formas:

```http
X-Api-Key: SU_CLAVE
```

```http
Authorization: Bearer SU_CLAVE
```

```text
http://127.0.0.1:8099/api/ping?host=10.1.1.50&key=SU_CLAVE
```

Sin clave, la API funciona únicamente en `127.0.0.1`.

> Para publicar en la red local la clave es obligatoria. Use una clave distinta por instalación.

---

## 4. Endpoints

### `GET /api/health`

Estado de la API, versión, ruta de la base de datos y hosts monitoreados.

```json
{
  "status": "ok",
  "app": "vmPing",
  "version": "1.8.9.8",
  "machine": "MI-PC",
  "timeUtc": "2026-11-16T14:00:00.0000000Z",
  "uptimeSeconds": 3600,
  "listen": "127.0.0.1",
  "port": 8099,
  "corsEnabled": false,
  "database": "C:\\Users\\usuario\\AppData\\Local\\vmPing\\vmPing.db",
  "historyEnabled": true,
  "hostsMonitored": 12
}
```

### `GET /api/hosts`

Hosts abiertos y monitoreados en vivo en la instancia actual.

```json
{
  "count": 1,
  "hosts": [{
    "hostname": "10.1.1.50",
    "alias": "Tienda Centro",
    "status": "Up",
    "active": true,
    "sent": 120,
    "received": 118,
    "lost": 2,
    "error": 0,
    "lossPercent": 1.67,
    "rttMs": { "current": 12, "min": 8, "avg": 13.4, "max": 60 }
  }]
}
```

### `GET /api/hosts/{host}`

Detalle en vivo + últimas muestras y eventos almacenados en SQLite.

Parámetros:

| Parámetro | Default | Descripción |
|---|---|---|
| `from` | `-24h` | Inicio del rango |
| `to` | ahora | Fin del rango |
| `samples` | `100` | Máximo de muestras (1–5000) |
| `events` | `50` | Máximo de eventos (1–2000) |

```text
GET /api/hosts/10.1.1.50?from=yesterday&to=today&samples=200
```

### `GET /api/ping` — el endpoint para bitácoras

Hace un ping **en el momento** al host indicado, aunque no esté abierto en la interfaz.

| Parámetro | Default | Descripción |
|---|---|---|
| `host` | — | IP, nombre o `host:puerto` (obligatorio) |
| `count` | `1` | Intentos 1–20 |
| `timeout` | `2000` | Timeout por intento en ms (200–10000) |
| `type` | auto | `icmp` o `tcp`. Si el host trae `:puerto`, usa TCP |
| `save` | `false` | `true` guarda la muestra en SQLite |

ICMP:

```text
GET /api/ping?host=10.1.1.50&count=3
```

TCP (útil para servicios, impresoras, POS):

```text
GET /api/ping?host=10.1.1.50:443&count=1
GET /api/ping?host=10.1.1.50&type=tcp&...   (requiere puerto en host)
```

Respuesta:

```json
{
  "target": "10.1.1.50",
  "host": "10.1.1.50",
  "port": null,
  "type": "icmp",
  "resolved": "10.1.1.50",
  "reachable": true,
  "count": 3,
  "sent": 3,
  "received": 3,
  "lost": 0,
  "lossPercent": 0,
  "rttMinMs": 9,
  "rttAvgMs": 11.33,
  "rttMaxMs": 14,
  "error": "",
  "attempts": [
    { "seq": 1, "success": true, "rttMs": 9, "status": "Success", "error": "" }
  ],
  "timestampUtc": "2026-11-16T14:00:00.0000000Z"
}
```

### `GET /api/samples`

Muestras históricas (día, rango o últimos N).

```text
GET /api/samples?host=10.1.1.50&from=2026-11-01&to=2026-11-16&limit=1000
GET /api/samples?from=-7d
```

### `GET /api/events`

Cambios de estado (caídas, recuperaciones, inicio/parada).

```text
GET /api/events?host=10.1.1.50&from=-30d&limit=500
```

### `GET /api/stats`

Disponibilidad y latencia agregadas por host. Es el endpoint ideal para “¿cómo estuvo ayer?”.

```text
GET /api/stats?from=yesterday&to=yesterday
GET /api/stats?from=2026-11-01&to=2026-11-16
GET /api/stats?host=10.1.1.50&from=-7d
```

Respuesta:

```json
{
  "range": { "fromUtc": "...", "toUtc": "..." },
  "hosts": [{
    "hostname": "10.1.1.50",
    "alias": "Tienda Centro",
    "samples": 2880,
    "ok": 2850,
    "failed": 30,
    "availabilityPercent": 98.96,
    "rttMinMs": 8,
    "rttAvgMs": 13.42,
    "rttMaxMs": 120
  }]
}
```

### `GET /api/daily`

Resumen por hora de un día local.

```text
GET /api/daily?host=10.1.1.50&date=2026-11-16
```

Devuelve 24 bloques con `samples`, `ok`, `failed`, `availabilityPercent` y `rttAvgMs`.

### `GET /api/inventory`

Inventario de equipos guardado por vmPing (`inventory.xml`).

```text
GET /api/inventory?host=10.1.1.50
```

### `GET /api/openapi.json`

Especificación OpenAPI 3.0 para importar en Postman, Swagger, n8n, etc.

### `GET /`

Página HTML de prueba con formulario de ping. Útil para validar sin escribir código.

---

## 5. Filtros de fecha

Los parámetros `from` y `to` aceptan:

| Valor | Significado |
|---|---|
| `2026-11-16` | Día completo (hora local) |
| `2026-11-16T08:00:00Z` | Fecha/hora ISO-8601 |
| `today`, `hoy` | Día actual |
| `yesterday`, `ayer` | Día anterior |
| `-30m`, `-24h`, `-7d`, `-4w` | Relativo a ahora |
| `now`, `ahora` | Momento actual |

Si `from` es mayor que `to`, se intercambian automáticamente.

---

## 6. Ejemplos de integración

### PowerShell

```powershell
$r = Invoke-RestMethod "http://127.0.0.1:8099/api/ping?host=10.1.1.50&count=1"
if ($r.reachable) { "OK $($r.rttAvgMs) ms" } else { "CAIDO: $($r.error)" }
```

### PHP (bitácora)

```php
$url = 'http://127.0.0.1:8099/api/ping?host=' . urlencode($ip) . '&count=1&key=' . urlencode($apiKey);
$json = @file_get_contents($url);
$data = $json ? json_decode($json, true) : null;

$estado = ($data && $data['reachable']) ? 'EN LINEA' : 'CAIDO';
$latencia = ($data && $data['rttAvgMs'] !== null) ? $data['rttAvgMs'] . ' ms' : '-';
```

### JavaScript (navegador, requiere CORS activado)

```javascript
const r = await fetch('http://10.1.1.71:8099/api/ping?host=10.1.1.50&count=1', {
  headers: { 'X-Api-Key': 'SU_CLAVE' }
});
const data = await r.json();
console.log(data.reachable, data.rttAvgMs);
```

### Python

```python
import requests
r = requests.get("http://127.0.0.1:8099/api/ping",
                 params={"host": "10.1.1.50", "count": 1},
                 headers={"X-Api-Key": "SU_CLAVE"},
                 timeout=10)
data = r.json()
print(data["reachable"], data["rttAvgMs"])
```

### SQL directo (solo lectura)

Puede abrir `vmPing.db` con DB Browser for SQLite, DBeaver o la CLI de SQLite:

```sql
-- Disponibilidad por host de los últimos 7 días
SELECT hostname,
       COUNT(*) AS muestras,
       SUM(success) AS ok,
       ROUND(SUM(success) * 100.0 / COUNT(*), 2) AS disponibilidad,
       ROUND(AVG(CASE WHEN success = 1 THEN latency_ms END), 2) AS rtt_promedio
FROM probe_samples
WHERE timestamp_utc >= strftime('%Y-%m-%dT%H:%M:%fZ', 'now', '-7 days')
GROUP BY hostname
ORDER BY hostname;
```

> No modifique ni bloquee la base mientras vmPing está escribiendo. Para respaldos, copie `vmPing.db` con la aplicación cerrada (o use la API).

---

## 7. Exponer a la red local

1. En Opciones → API seleccione **Red local (0.0.0.0)** y configure clave.
2. Permita el puerto en el firewall de Windows (ejecutar como administrador una sola vez):

```powershell
New-NetFirewallRule -DisplayName "vmPing API" -Direction Inbound `
  -Protocol TCP -LocalPort 8099 -Action Allow -Profile Domain,Private
```

3. Desde otro equipo:

```text
http://IP-DEL-EQUIPO:8099/api/health
```

Si no responde, revise que vmPing esté abierto, que la API esté habilitada y que el firewall permita el puerto.

---

## 8. Notas y límites

- La API es de **solo lectura** sobre el historial; no permite borrar ni editar datos.
- `GET /api/ping` ejecuta tráfico real desde el equipo donde corre vmPing. Con `save=true` queda registrado en SQLite.
- El historial en vivo (`/api/hosts`) corresponde a la instancia de vmPing que tiene la API habilitada. Si abre otra instancia, ambas comparten `vmPing.db` pero solo una puede usar el mismo puerto.
- El puerto debe ser único por equipo.
- La API no reemplaza un sistema de monitoreo central; es un puente para que su bitácora consulte datos reales de ping, disponibilidad e inventario.
- Si necesita HTTPS, publique la API detrás de un proxy inverso (IIS/Nginx) en el mismo equipo.
