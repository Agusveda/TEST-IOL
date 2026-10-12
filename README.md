# Token Bucket Rate Limiter

Implementación de un Rate Limiter en memoria utilizando Token Bucket, .NET 10 y ASP.NET Core.

La API limita solicitudes por cliente. El header `X-Api-Key` identifica qué bucket utilizar y `RateLimitMiddleware` traduce la decisión del algoritmo a una respuesta HTTP. El estado se conserva en memoria dentro de una única instancia de la aplicación.

## Tecnologías utilizadas

- C# y .NET 10.
- ASP.NET Core Minimal API.
- xUnit.
- `WebApplicationFactory` y `TestServer` para Integration Tests.
- `TimeProvider` para controlar el tiempo.
- `System.Diagnostics.Metrics` para métricas.

## Estructura del proyecto

```text
src/
├── RateLimiter.Core/
│   └── Algoritmo Token Bucket y modelos independientes de HTTP.
└── RateLimiter.Api/
    └── Minimal API, middleware, configuración, logging y métricas.

tests/
├── RateLimiter.UnitTests/
│   └── Tests de la lógica central, configuración y concurrencia.
└── RateLimiter.IntegrationTests/
    └── Tests del pipeline HTTP mediante WebApplicationFactory.
```

`RateLimiter.Api` depende de `RateLimiter.Core`. `RateLimiter.Core` no depende de ASP.NET Core.

## Compilar y ejecutar

Se requiere el SDK de .NET 10.

Desde la raíz del repositorio:

```bash
dotnet restore RateLimiter.sln
dotnet build RateLimiter.sln
dotnet test RateLimiter.sln
dotnet run --project src/RateLimiter.Api/RateLimiter.Api.csproj
```

Al ejecutar la aplicación se muestra en la consola la dirección utilizada. El perfil HTTP incluido utiliza `http://localhost:5002`.

## Uso de la API

Endpoint disponible:

```http
GET /api/test
```

La solicitud debe incluir un único header `X-Api-Key` con un valor no vacío:

```bash
curl -i -H "X-Api-Key: client-123" http://localhost:5002/api/test
```

Una respuesta permitida devuelve:

```json
{
  "message": "Solicitud permitida."
}
```

`X-Api-Key` solamente identifica el bucket del cliente. No implementa autenticación ni autorización.

## Respuestas HTTP y headers

| Estado | Condición |
|---|---|
| `200 OK` | La solicitud tiene un `X-Api-Key` válido y hay tokens disponibles. |
| `400 Bad Request` | `X-Api-Key` falta, está vacío, contiene solamente espacios o tiene múltiples valores. |
| `429 Too Many Requests` | El bucket del cliente no tiene tokens disponibles. |

Las respuestas permitidas y rechazadas por límite incluyen:

- `X-RateLimit-Limit`: capacidad máxima del bucket.
- `X-RateLimit-Remaining`: tokens restantes después de evaluar la solicitud.

Las respuestas `429 Too Many Requests` también incluyen:

- `Retry-After`: segundos enteros que faltan para el próximo refill, redondeados hacia arriba.

Una respuesta `400 Bad Request` ocurre antes de consultar el Rate Limiter y no incluye estos headers.

## Configuración

La configuración actual se encuentra en `src/RateLimiter.Api/appsettings.json`:

```json
{
  "RateLimiter": {
    "Capacity": 3,
    "TokensPerPeriod": 1,
    "RefillPeriodSeconds": 1
  }
}
```

- `Capacity`: cantidad máxima de tokens del bucket.
- `TokensPerPeriod`: tokens agregados por cada período completo de refill.
- `RefillPeriodSeconds`: duración del período de refill, expresada en segundos.

Los tres valores deben ser mayores que cero. La aplicación valida la configuración antes de iniciar y falla si falta una clave o si algún valor es inválido.

## Tests

La solución contiene 22 casos de test:

- 13 Unit Tests: cubren disponibilidad de tokens, bucket vacío, refill, capacidad máxima, independencia entre clientes, `RetryAfter`, configuración inválida y concurrencia.
- 9 Integration Tests: cubren el pipeline HTTP, status codes, headers, independencia entre clientes, refill y validación de configuración durante el inicio.

Para ejecutarlos:

```bash
dotnet test RateLimiter.sln
```

## Limitaciones conocidas

- El estado se mantiene únicamente en memoria.
- Los buckets se pierden cuando la aplicación se reinicia.
- Cada instancia de la API mantiene sus propios buckets y no se coordina con otras instancias.
- Los buckets creados no expiran ni se eliminan.
- `X-Api-Key` no implementa autenticación.
- No existe un backend distribuido ni persistencia externa.

Ver [DESIGN.md](DESIGN.md) para conocer las decisiones de diseño y trade-offs.
