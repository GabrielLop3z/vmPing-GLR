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
