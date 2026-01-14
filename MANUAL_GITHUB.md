# Guía de Gestión de GitHub para vmPing-GLR

Este manual explica paso a paso cómo subir cambios y mantener actualizado el repositorio de GitHub para el proyecto **vmPing-GLR**.

## Requisitos Previos

El proyecto ya está configurado y vinculado al siguiente repositorio remoto:
- **URL**: `https://github.com/GabrielLop3z/vmPing-GLR.git`

## Flujo de Trabajo Básico

Cada vez que realices cambios en el código (modificar archivos, crear nuevos, etc.), sigue estos pasos para subirlos a GitHub.

### 1. Verificar el Estado
Antes de nada, es bueno ver qué archivos han cambiado. Abre una terminal en la carpeta del proyecto y ejecuta:

```powershell
git status
```

*   **Rojo**: Archivos modificados pero no preparados.
*   **Verde**: Archivos preparados listos para confirmarse.

### 2. Preparar los Cambios (Stage)
Para preparar todos los archivos modificados para su subida:

```powershell
git add .
```

Si solo quieres agregar un archivo específico:
```powershell
git add nombre_del_archivo.ext
```

### 3. Guardar los Cambios (Commit)
Una vez preparados, debes confirmar los cambios con un mensaje descriptivo de lo que hiciste:

```powershell
git commit -m "Descripción breve de los cambios realizados"
```

*Ejemplo:* `git commit -m "Mejorar interfaz de usuario y corregir colores"`

### 4. Subir a GitHub (Push)
Finalmente, envía tus confirmaciones a la nube (GitHub):

```powershell
git push origin main
```

Si todo sale bien, verás un mensaje indicando que se han escrito objetos y completado la subida.

---

## Solución de Problemas Comunes

### Error: "Updates were rejected because the remote contains work that you do not have locally"
Esto sucede si alguien más (o tú desde otra PC) subió cambios al repositorio y tu versión local está desactualizada.

**Solución:** Descarga los cambios remotos primero.
```powershell
git pull origin main
```
Luego intenta hacer el `git push` nuevamente.

### Credenciales
Si te pide usuario y contraseña:
1.  Si usas autenticación moderna (recomendado), debería abrirse una ventana del navegador para iniciar sesión en GitHub.
2.  Si te pide contraseña en la terminal, ten en cuenta que GitHub ya no acepta contraseñas de cuenta, debes usar un **Personal Access Token (PAT)**.

## Resumen Rápido

```powershell
# 1. Ver cambios
git status

# 2. Agregar todo
git add .

# 3. Guardar con mensaje
git commit -m "Mis cambios"

# 4. Subir
git push
```

---

## 5. Gestión de Versiones (Tags y Releases)

Para que el sistema de actualizaciones automáticas funcione (y para mantener un historial ordenado), debes crear "Tags" (etiquetas) que marquen versiones específicas (ej. `v1.3.26`).

### Paso 1: Crear la Etiqueta (Tag) Localmente
Una vez que hayas guardado (commit) todos tus cambios para la nueva versión, crea la etiqueta:

```powershell
# Sintaxis: git tag -a [VERSION] -m "[MENSAJE]"
git tag -a v1.3.26 -m "Lanzamiento versión 1.3.26 con mejoras"
```

### Paso 2: Subir la Etiqueta a GitHub
Las etiquetas no se suben automáticamente con un `git push` normal. Debes subirlas explícitamente:

```powershell
git push --tags
```

o para una etiqueta específica:
```powershell
git push origin v1.3.26
```

### Paso 3: Crear la "Release" en GitHub (Importante para Auto-Update)
El archivo `update.xml` de tu proyecto apunta a las descargas en GitHub Releases. Para que esto funcione:

1.  Ve a la página de tu repositorio en GitHub: https://github.com/GabrielLop3z/vmPing-GLR
2.  Haz clic en **"Releases"** (a la derecha) o ve a **"Tags"**.
3.  Verás tu nuevo tag `v1.3.26`. Haz clic en los tres puntos o en el tag y selecciona **"Create release"** (o "Draft a new release" y selecciona el tag existente).
4.  Escribe un título (ej. "v1.3.26") y una descripción de los cambios.
5.  **MUY IMPORTANTE**: En la sección de "Assets" (Archivos adjuntos), debes **subir el archivo compilado** (ej. `vmPing.zip` o `vmPing.exe`).
    *   *Nota: Tu `update.xml` busca un archivo llamado específicamente `vmPing.zip` en esa versión.*
6.  Haz clic en **"Publish release"**.

### Paso 4: Actualizar `update.xml`
Finalmente, edita el archivo `update.xml` en tu código para apuntar a esta nueva versión y súbelo:

```xml
<item>
    <version>1.3.26</version>
    <url>https://github.com/GabrielLop3z/vmPing-GLR/releases/download/v1.3.26/vmPing.zip</url>
    <changelog>https://github.com/GabrielLop3z/vmPing-GLR/releases/tag/v1.3.26</changelog>
    <mandatory>false</mandatory>
</item>
```
