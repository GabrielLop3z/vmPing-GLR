# Manual de la API HTTP de vmPing GLR

**Versión del manual:** 1.9.4 · **Aplica a:** vmPing GLR 1.9.0 o superior.

vmPing expone su monitoreo por HTTP para que bitácoras, paneles, scripts o inventarios consulten:
el estado en vivo, hagan un ping bajo demanda y lean el historial guardado en SQLite.

La API corre **dentro de vmPing**. Escucha por defecto en `http://127.0.0.1:8099` y no necesita IIS,
permisos de administrador ni reservas `netsh`/URL ACL.

---

## 1. Inicio rápido (5 minutos)

### 1.1 Activar la API

1. Abra vmPing → **Herramientas → Opciones → pestaña API**.
2. Marque **Habilitar API HTTP**.
3. Elija el puerto (por defecto `8099`).
4. En **Escuchar en**:
   - **Solo este equipo (127.0.0.1)**: recomendado si la bitácora corre en el mismo PC.
   - **Red local (0.0.0.0)**: para consultar desde otros equipos. **Exige clave API**.
5. Pulse **Generar** para crear una **Clave API** (o escriba una propia).
6. Si una página web en el navegador va a llamar la API desde otro origen, active **Permitir CORS**.
7. Marque **Guardar como predeterminados de vmPing** y acepte.

### 1.2 Verificar que funciona

Abra **Herramientas → API / Integraciones**. Debe mostrar `Estado: En ejecución` y la URL local.

- Con **clave API configurada**, use los botones:
  - **Copiar URL**: copia `http://127.0.0.1:<puerto>?key=<su-clave>`.
  - **Abrir en navegador**: abre esa misma URL, así que no aparece el error `401`.
  - **Copiar clave**: copia solo la clave para pegarla en `X-Api-Key`.
- Sin clave, basta con abrir `http://127.0.0.1:8099`.

> **No comparta la URL copiada cuando tenga clave**: el parámetro `?key=` revela su clave.
> Para integraciones, prefiera enviar la cabecera `X-Api-Key`.

Prueba mínima desde PowerShell:

```powershell
Invoke-RestMethod "http://127.0.0.1:8099/api/health" |
  Select-Object status, version, port, hostsMonitored
```

---

## 2. Convenciones generales

| Concepto | Valor |
|---|---|
| Base URL | `http://127.0.0.1:8099` (local) o `http://IP-DEL-EQUIPO:8099` (LAN) |
| Métodos | `GET`, `HEAD` y `OPTIONS` (preflight CORS). No se permite `POST` |
| Formato de respuesta | JSON (la página `/` devuelve HTML) |
| Autenticación | `X-Api-Key`, `Authorization: Bearer` o `?key=` |
| Codificación | UTF-8. Los parámetros deben ir URL-codificados |
| Límites | Máximo 32 clientes simultáneos; cabeceras y longitud de petición acotadas |
| Caché | `Cache-Control: no-store` |

Forma de una respuesta correcta:

```json
{ "count": 1, "hosts": [ ... ] }
```

Forma de una respuesta de error:

```json
{ "error": "No autorizado. Envíe la cabecera X-Api-Key." }
```

### Códigos de estado

| Código | Significado | Qué hacer |
|---|---|---|
| `200` | OK | Respuesta JSON o HTML |
| `204` | Preflight `OPTIONS` correcto | Solicitud CORS aceptada |
| `400` | Falta un parámetro obligatorio o fecha inválida | Revisar `host`, `date`, `from`, `to` |
| `401` | Clave ausente o incorrecta | Enviar `X-Api-Key` o usar **Copiar clave** |
| `404` | Ruta inexistente u host sin datos | Verificar URL, nombre/alias del host |
| `405` | Método no permitido | Usar `GET` |

---

## 3. Autenticación

Si configuró una clave, puede enviarla de tres formas:

```http
X-Api-Key: SU_CLAVE
```

```http
Authorization: Bearer SU_CLAVE
```

```text
http://127.0.0.1:8099/api/hosts?key=SU_CLAVE
```

También se acepta `?apikey=SU_CLAVE`.

- Sin clave, la API solo escucha en `127.0.0.1`.
- Para escuchar en la red local la clave es **obligatoria**; vmPing se niega a iniciar si falta.
- Use una clave distinta por instalación y guárdela como secreto, no en un script compartido.

Si olvidó la clave: **Herramientas → Opciones → API** (el campo es visible) o el botón
**Copiar clave** en **API / Integraciones**.

---

## 4. Filtros de fecha

Los parámetros `from`, `to` (y `date` en `/api/daily`) aceptan:

| Valor | Significado | Ejemplo |
|---|---|---|
| `YYYY-MM-DD` | Día completo en hora local | `from=2026-11-16` |
| ISO-8601 | Fecha/hora con zona | `from=2026-11-16T08:00:00Z` |
| `today`, `hoy` | Día actual | `from=today` |
| `yesterday`, `ayer` | Día anterior completo | `from=yesterday&to=yesterday` |
| `-30m`, `-24h`, `-7d`, `-4w` | Relativo a ahora | `from=-7d` |
| `now`, `ahora` | Momento actual | `to=now` |

Si `from` es mayor que `to`, vmPing los intercambia automáticamente.

---

## 5. Endpoints

### 5.1 `GET /api/health`

Estado de la API, versión, máquina, base de datos y número de hosts monitoreados.

```bash
curl.exe "http://127.0.0.1:8099/api/health" -H "X-Api-Key: SU_CLAVE"
```

```json
{
  "status": "ok",
  "app": "vmPing",
  "version": "1.9.4",
  "machine": "MI-PC",
  "timeUtc": "2026-11-20T14:00:00.0000000Z",
  "uptimeSeconds": 3600,
  "listen": "127.0.0.1",
  "port": 8099,
  "corsEnabled": false,
  "database": "C:\\Users\\usuario\\AppData\\Local\\vmPing\\vmPing.db",
  "historyEnabled": true,
  "hostsMonitored": 12
}
```

### 5.2 `GET /api/hosts`

Hosts abiertos en la instancia actual de vmPing, ordenados por alias.

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

### 5.3 `GET /api/hosts/{host}`

Detalle en vivo + muestras y eventos del historial. Acepta hostname o **alias** (sin distinción
de mayúsculas). Un host desconocido devuelve `404`.

| Parámetro | Default | Rango |
|---|---|---|
| `from` | `-24h` | Ver §4 |
| `to` | ahora | Ver §4 |
| `samples` | `100` | 1–5000 |
| `events` | `50` | 1–2000 |

```text
GET /api/hosts/10.1.1.50?from=yesterday&to=today&samples=200
GET /api/hosts/Tienda%20Centro?from=-7d
```

```json
{
  "host": { "hostname": "10.1.1.50", "alias": "Tienda Centro", "status": "Up" },
  "range": { "fromUtc": "...", "toUtc": "..." },
  "samplesCount": 200,
  "samples": [ { "timestampUtc": "...", "success": true, "latencyMs": 12 } ],
  "eventsCount": 2,
  "events": [ { "oldStatus": "Down", "newStatus": "Up", "timestampLocal": "..." } ]
}
```

### 5.4 `GET /api/ping` — ping bajo demanda

Hace un ping **real desde el equipo donde corre vmPing**, aunque el host no esté abierto en la
interfaz. Es el endpoint pensado para bitácoras.

| Parámetro | Default | Descripción |
|---|---|---|
| `host` | obligatorio | IP, nombre o `host:puerto` |
| `count` | `1` | Intentos (1–20) |
| `timeout` | `2000` | Ms por intento (200–10000) |
| `type` | automático | `icmp` o `tcp`. Si `host` trae `:puerto`, usa TCP |
| `save` | `false` | `true` guarda la muestra en SQLite (`API-ICMP`/`API-TCP`) |

```text
GET /api/ping?host=10.1.1.50&count=3
GET /api/ping?host=10.1.1.50:443&count=1
GET /api/ping?host=impresora.local&count=2&timeout=3000&save=true
```

Para `type=tcp` es obligatorio indicar el puerto en `host` (`host=10.1.1.50:443`, o
`host=[::1]:443` para IPv6).

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
  "timestampUtc": "2026-11-20T14:00:00.0000000Z"
}
```

> `reachable` es `true` si **al menos un** intento tuvo éxito. Revise `received`/`sent` y
> `attempts[].error` para pérdidas parciales.

### 5.5 `GET /api/samples`

Muestras históricas de `probe_samples`.

| Parámetro | Default | Rango |
|---|---|---|
| `host` | todos | Filtro por hostname |
| `from` / `to` | últimas 24 h | Ver §4 |
| `limit` | `500` | 1–5000 |

```text
GET /api/samples?host=10.1.1.50&from=2026-11-01&to=2026-11-16&limit=1000
GET /api/samples?from=-7d
```

Devuelve `count`, `range` y `samples[]` con `timestampUtc`, `timestampLocal`, `success`,
`latencyMs`, `error`, `probeType` y `alias`.

### 5.6 `GET /api/events`

Cambios de estado (Up, Down, Error, Start, Stop…).

| Parámetro | Default | Rango |
|---|---|---|
| `host` | todos | Filtro por hostname |
| `from` / `to` | últimas 24 h | Ver §4 |
| `limit` | `200` | 1–2000 |

```text
GET /api/events?host=10.1.1.50&from=-30d&limit=500
```

Cada evento incluye `oldStatus`, `newStatus`, `timestampUtc`, `timestampLocal`, `reason` y `alias`.

### 5.7 `GET /api/stats`

Disponibilidad y latencia agregadas por host. Ideal para responder “¿cómo estuvo ayer?”.

```text
GET /api/stats?from=yesterday&to=yesterday
GET /api/stats?from=2026-11-01&to=2026-11-16
GET /api/stats?host=10.1.1.50&from=-7d
```

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

### 5.8 `GET /api/daily`

Resumen por hora de un día local. Requiere `host`.

| Parámetro | Obligatorio | Descripción |
|---|---|---|
| `host` | sí | IP o nombre del host |
| `date` | no | `YYYY-MM-DD`; por defecto hoy |

```text
GET /api/daily?host=10.1.1.50&date=2026-11-16
```

Devuelve 24 bloques (`hour` 0–23) con `samples`, `ok`, `failed`,
`availabilityPercent` y `rttAvgMs`.

### 5.9 `GET /api/inventory`

Inventario guardado por **Herramientas → Inventario de Equipos**.

```text
GET /api/inventory
GET /api/inventory?host=10.1.1.50
```

El filtro `host` busca por host, DNS, alias. Incluye serial, fabricante, modelo, SO, CPU, RAM,
IPv4, MAC, dominio y datos SNMP.

### 5.10 `GET /api/openapi.json`

Especificación OpenAPI 3.0. Impórtela en Postman, Swagger UI, n8n, Insomnia, etc.

```text
http://127.0.0.1:8099/api/openapi.json?key=SU_CLAVE
```

### 5.11 `GET /`

Página HTML de prueba con un formulario de ping. Si abre con `?key=SU_CLAVE`, la página conserva
la clave en los enlaces y en la prueba (v1.9.4). No comparta esa URL.

---

## 6. Ejemplos listos para usar

### 6.1 Navegador

Con la clave configurada, en **API / Integraciones** pulse **Abrir en navegador**.
Sin clave, escriba:

```text
http://127.0.0.1:8099/api/health
```

### 6.2 curl (Windows PowerShell o Linux/macOS)

En PowerShell use `curl.exe` (no `curl`, que es un alias de `Invoke-WebRequest`):

```powershell
curl.exe "http://127.0.0.1:8099/api/ping?host=10.1.1.50&count=2" `
  -H "X-Api-Key: SU_CLAVE"
```

Con la clave en la URL (útil solo en pruebas locales):

```bash
curl "http://127.0.0.1:8099/api/hosts?key=SU_CLAVE"
```

Ver disponibilidad de ayer:

```bash
curl "http://127.0.0.1:8099/api/stats?from=yesterday&to=yesterday" -H "X-Api-Key: SU_CLAVE"
```

### 6.3 PowerShell (recomendado en equipos Windows)

```powershell
$base = 'http://127.0.0.1:8099'
$headers = @{ 'X-Api-Key' = 'SU_CLAVE' }

try {
    $r = Invoke-RestMethod -Uri "$base/api/ping?host=10.1.1.50&count=2" `
         -Headers $headers -TimeoutSec 15
    [pscustomobject]@{
        Host      = $r.host
        Reach     = $r.reachable
        Recibidos = "$($r.received)/$($r.sent)"
        RttMs     = $r.rttAvgMs
        Error     = $r.error
    }
}
catch {
    $detalle = $_.ErrorDetails.Message
    Write-Error "Falló la consulta: $($_.Exception.Message) $detalle"
}
```

Registrar la muestra en el historial:

```powershell
Invoke-RestMethod "$base/api/ping?host=10.1.1.50&count=1&save=true" -Headers $headers
```

### 6.4 Python

```python
import requests

BASE = "http://127.0.0.1:8099"
HEADERS = {"X-Api-Key": "SU_CLAVE"}

r = requests.get(
    f"{BASE}/api/ping",
    params={"host": "10.1.1.50", "count": 2},
    headers=HEADERS,
    timeout=15,
)
r.raise_for_status()
data = r.json()

print("EN LINEA" if data["reachable"] else "CAIDO", data["rttAvgMs"], "ms")
```

Disponibilidad de los últimos 7 días:

```python
stats = requests.get(
    f"{BASE}/api/stats",
    params={"from": "-7d", "to": "now"},
    headers=HEADERS,
    timeout=15,
).json()

for h in stats["hosts"]:
    print(h["hostname"], h["availabilityPercent"], "%")
```

### 6.5 Node.js 18+ / JavaScript (servidor)

```javascript
const base = 'http://127.0.0.1:8099';
const key = 'SU_CLAVE';

const url = `${base}/api/ping?host=${encodeURIComponent('10.1.1.50')}&count=2`;
const res = await fetch(url, { headers: { 'X-Api-Key': key } });
if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);

const data = await res.json();
console.log(data.reachable ? 'EN LINEA' : 'CAIDO', data.rttAvgMs, 'ms');
```

### 6.6 JavaScript en el navegador (requiere CORS)

Active **Permitir CORS** en Opciones → API.

```javascript
const r = await fetch('http://10.1.1.71:8099/api/ping?host=10.1.1.50&count=1', {
  headers: { 'X-Api-Key': 'SU_CLAVE' }
});
if (!r.ok) throw new Error(await r.text());
const data = await r.json();
console.log(data.reachable, data.rttAvgMs);
```

También puede abrir el ejemplo listo: [`docs/ejemplo-bitacora.html`](ejemplo-bitacora.html).

### 6.7 PHP (bitácora)

```php
<?php
$apiKey = 'SU_CLAVE';
$host   = '10.1.1.50';

$url = 'http://127.0.0.1:8099/api/ping?host=' . rawurlencode($host) . '&count=1';

$ch = curl_init($url);
curl_setopt_array($ch, [
    CURLOPT_RETURNTRANSFER => true,
    CURLOPT_TIMEOUT        => 10,
    CURLOPT_HTTPHEADER     => ['X-Api-Key: ' . $apiKey],
]);
$json = curl_exec($ch);
$code = curl_getinfo($ch, CURLINFO_HTTP_CODE);
curl_close($ch);

$data = $json ? json_decode($json, true) : null;
$estado = ($code === 200 && $data['reachable']) ? 'EN LINEA' : 'CAIDO';
$latencia = ($data && $data['rttAvgMs'] !== null) ? $data['rttAvgMs'] . ' ms' : '-';

echo "$host: $estado ($latencia)\n";
```

### 6.8 Excel / Power Query (Power BI)

Datos → Obtener datos → Consulta web en blanco → Editor avanzado:

```m
let
    Url = "http://127.0.0.1:8099/api/stats?from=yesterday&to=yesterday",
    Encabezados = [#"X-Api-Key" = "SU_CLAVE"],
    Fuente = Json.Document(Web.Contents(Url, [Headers = Encabezados])),
    Tabla = Table.FromList(Fuente[hosts], Splitter.SplitByNothing(), null, null, ExtraValues.Error),
    Expandida = Table.ExpandRecordColumn(Tabla, "Column1",
        {"hostname", "alias", "availabilityPercent", "rttAvgMs"})
in
    Expandida
```

### 6.9 Postman / n8n / Swagger

Importe como URL de OpenAPI:

```text
http://127.0.0.1:8099/api/openapi.json?key=SU_CLAVE
```

Después configure la variable `X-Api-Key` en el entorno de Postman o el nodo HTTP de n8n.

---

## 7. Recetas frecuentes

| Necesidad | Consulta |
|---|---|
| ¿Responde este equipo ahora? | `/api/ping?host=IP&count=1` |
| Registrar cada verificación en el historial | `/api/ping?host=IP&count=1&save=true` |
| ¿Estuvo caído ayer? | `/api/stats?from=yesterday&to=yesterday` |
| ¿En qué horas falló? | `/api/daily?host=IP&date=2026-11-16` |
| Timeline de caídas y recuperaciones | `/api/events?host=IP&from=-7d` |
| Muestras crudas para BI | `/api/samples?from=-7d&limit=5000` |
| Datos de inventario de un equipo | `/api/inventory?host=IP` |
| Estado general de la instancia | `/api/health` |

Ejemplo: detectar horas con problemas:

```powershell
$dia = Invoke-RestMethod "http://127.0.0.1:8099/api/daily?host=10.1.1.50&date=2026-11-16" `
       -Headers @{ 'X-Api-Key' = 'SU_CLAVE' }
$dia.hours | Where-Object { $_.failed -gt 0 } |
  Select-Object hour, samples, failed, availabilityPercent
```

---

## 8. ¿Dónde están los datos?

Desde 1.8.9.7 el historial se guarda en SQLite:

| Modo | Archivo |
|---|---|
| Normal | `%LOCALAPPDATA%\vmPing\vmPing.db` |
| Portable (hay `vmPing.xml` junto al exe) | `<carpeta de vmPing>\vmPing.db` |

| Tabla | Contenido |
|---|---|
| `hosts` | Hosts vistos, alias, primera/última vez |
| `probe_samples` | Muestras: fecha UTC, éxito, latencia, error |
| `status_changes` | Cambios de estado |
| `audit_log` | Auditoría de la aplicación |
| `report_runs` | Envíos de reportes |

Retención por defecto: **90 días** (configurable en **Opciones → Reportes**). Solo se puede
consultar lo que no haya sido purgado.

La API **solo lee** el historial; la única forma de escribir desde HTTP es
`GET /api/ping?...&save=true`.

Consulta SQL directa (solo lectura, con la aplicación cerrada para respaldos):

```sql
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

---

## 9. Exponer la API a la red local

1. Opciones → API → **Escuchar en: Red local (0.0.0.0)** y configure una clave.
2. Abra el puerto en el firewall de Windows (una vez, como administrador):

```powershell
New-NetFirewallRule -DisplayName "vmPing API" -Direction Inbound `
  -Protocol TCP -LocalPort 8099 -Action Allow -Profile Domain,Private
```

3. Desde otro equipo:

```text
http://IP-DEL-EQUIPO:8099/api/health
```

4. En la bitácora, envíe `X-Api-Key: SU_CLAVE`.

Si necesita HTTPS, publique la API detrás de un proxy inverso (IIS, Nginx) en el mismo equipo.

---

## 10. Solución de problemas

| Síntoma | Causa probable | Solución |
|---|---|---|
| `401 No autorizado. Envíe la cabecera X-Api-Key.` | Falta o es incorrecta la clave | Use **Copiar clave** y envíe `X-Api-Key`; o abra/copie la URL desde **API / Integraciones** (incluye `?key=`) |
| `No se pudo iniciar la API: ...` al activar LAN | Internet listening exige clave | Configure **Clave API** antes de elegir Red local |
| `Connection refused` / no responde | API detenida, puerto cambiado o firewall | Revise **API / Integraciones**, el puerto y el firewall |
| `404 No hay datos para el host` | Host/alias inexistente o fuera de retención | Verifique el nombre con `/api/hosts` y la fecha |
| `404 Ruta no encontrada` | URL mal escrita | Use `/api/...` |
| `400 Indique el host` | Falta parámetro obligatorio | Agregue `host=` o `date=` |
| `405 Método no permitido` | Usó `POST` | Use `GET` |
| Error de CORS en el navegador | CORS desactivado | Active **Permitir CORS** o llame desde un backend |
| Respuestas vacías | Aún no hay muestras o el rango está fuera de la retención | Espere al primer ciclo de ping o amplíe el rango |
| La clave aparece en el historial del navegador | Abrió la URL con `?key=` | En producción use `X-Api-Key` y no comparta esa URL |

---

## 11. Notas de seguridad

- La API no expone contraseñas WMI/SNMP ni la clave DPAPI; solo datos de monitoreo.
- Cualquier persona con la clave puede leer el historial: trátela como una contraseña.
- No publique `?key=` en tickets, correos ni capturas.
- Use bind local (`127.0.0.1`) siempre que sea posible y un backend intermedio si la bitácora está en otro equipo.
- Cambie la clave si sospecha que se filtró: Opciones → API → **Generar** → guardar → reiniciar API.

---

## 12. Referencia rápida

```text
GET /api/health                         Estado y versión
GET /api/hosts                          Hosts en vivo
GET /api/hosts/{host}                   Detalle + historial (404 si no existe)
GET /api/ping?host=IP&count=1           Ping ICMP/TCP bajo demanda
GET /api/samples?host=IP&from=-7d       Muestras históricas
GET /api/events?host=IP&from=-7d        Cambios de estado
GET /api/stats?from=yesterday           Disponibilidad agregada
GET /api/daily?host=IP&date=YYYY-MM-DD  Resumen por hora
GET /api/inventory?host=IP              Inventario de equipos
GET /api/openapi.json                   Especificación OpenAPI
GET /                                   Página de prueba HTML
```
