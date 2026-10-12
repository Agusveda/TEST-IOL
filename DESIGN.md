# Diseño

## 1. Contexto, objetivos y fuera de alcance

El proyecto implementa un Rate Limiter por cliente mediante el algoritmo Token Bucket. Cada cliente se identifica con el header `X-Api-Key` y mantiene su propio estado dentro de la instancia de la API.

Los objetivos son:

- Limitar solicitudes de manera independiente por cliente.
- Permitir configurar capacidad, cantidad de tokens y período de refill.
- Mantener un comportamiento correcto cuando varias solicitudes acceden al mismo bucket de forma concurrente.
- Producir respuestas HTTP y headers claros.
- Permitir tests deterministas sin esperas reales.
- Mantener `RateLimiter.Core` independiente de ASP.NET Core.

Quedan fuera de alcance:

- Autenticación y autorización.
- Rate limiting distribuido entre procesos o servidores.
- Persistencia del estado.
- Múltiples políticas o límites diferentes por cliente.
- Exportación externa de métricas.

## 2. Requisitos y restricciones

La implementación debe aceptar o rechazar una solicitud usando el estado correspondiente a su `clientId`. Un bucket comienza con su capacidad completa, recupera tokens según períodos completos transcurridos y nunca puede superar `Capacity`.

Una solicitud permitida consume un token. Si no hay tokens, el resultado informa cuánto falta para el próximo refill mediante `RetryAfter`.

La configuración inválida debe detectarse durante el inicio de la aplicación. El algoritmo debe poder probarse sin depender del reloj real y debe conservar su estado entre solicitudes, por lo que `TokenBucketRateLimiter` se registra como Singleton.

El diseño se limita a estos requisitos y no agrega infraestructura que el proyecto no utiliza.

## 3. Arquitectura y flujo de una request

```text
HTTP Request
      ↓
RateLimitMiddleware
      ├── X-Api-Key inválido ───────────────→ 400 Bad Request
      ↓
TokenBucketRateLimiter
      ↓
Client Bucket
      ↓
RateLimitResult
      ├── IsAllowed = true ────────────────→ continuar con _next
      └── IsAllowed = false ───────────────→ 429 Too Many Requests
```

Responsabilidades:

- `RateLimiter.Core`: contiene `TokenBucketRateLimiter`, `TokenBucketOptions` y `RateLimitResult`. No conoce HTTP ni ASP.NET Core.
- `RateLimiter.Api`: configura dependencias, expone la Minimal API y traduce las decisiones del Core a HTTP mediante `RateLimitMiddleware`.
- `RateLimiter.UnitTests`: verifica las reglas e invariantes de la lógica central.
- `RateLimiter.IntegrationTests`: ejecuta el pipeline de ASP.NET Core en memoria y verifica el contrato HTTP.

La dirección de dependencias es:

```text
RateLimiter.Api → RateLimiter.Core
```

`RateLimiter.Core` no referencia `RateLimiter.Api` ni componentes de ASP.NET Core.

## 4. Modelo Token Bucket

Token Bucket permite aceptar ráfagas de solicitudes hasta una capacidad máxima y recuperar disponibilidad con el paso del tiempo. Es adecuado para este problema porque representa el límite con un estado pequeño por cliente: tokens disponibles y tiempo acumulado.

`TokenBucketOptions` define:

- `Capacity`: cantidad máxima de tokens.
- `TokensPerPeriod`: tokens generados por período completo.
- `RefillPeriod`: duración de cada período.

El primer acceso de un `clientId` crea un `Bucket` lleno. Cada solicitud permitida consume un token. El refill agrega tokens según el tiempo transcurrido, pero nunca permite superar `Capacity`.

Los clientes se comparan mediante `StringComparer.Ordinal`, por lo que el identificador distingue mayúsculas y minúsculas.

Cuando el bucket está vacío, `RateLimitResult` devuelve:

- `IsAllowed = false`.
- `RemainingTokens = 0`.
- `RetryAfter` con el tiempo restante hasta el próximo token.

## 5. Modelo de concurrencia

`TokenBucketRateLimiter` utiliza un `ConcurrentDictionary<string, Bucket>` para relacionar cada cliente con su bucket. El diccionario permite buscar o agregar clientes de forma segura cuando llegan solicitudes concurrentes.

`ConcurrentDictionary` protege la colección, pero no las variables internas de cada `Bucket`. Por ese motivo, cada bucket posee su propio `lock`.

Dentro del mismo `lock` se ejecutan:

1. El refill según el tiempo transcurrido.
2. La validación de tokens disponibles.
3. El consumo de un token.

Estas operaciones deben permanecer juntas. Sin sincronización, dos solicitudes podrían observar el mismo token disponible y ambas ser permitidas.

El lock es por bucket y no global. Dos solicitudes del mismo cliente no modifican el bucket al mismo tiempo, mientras que clientes diferentes pueden utilizar sus buckets sin bloquearse entre sí.

El test `AllowsOnlyCapacity_WhenRequestsAreConcurrent` prepara 100 adquisiciones concurrentes para un cliente con `Capacity = 10` y comprueba que exactamente 10 sean permitidas y 90 rechazadas.

## 6. Tiempo y cálculo de refill

El Core recibe un `TimeProvider`. En producción se registra `TimeProvider.System`; en los tests se reemplaza por `ManualTimeProvider`.

`GetTimestamp` obtiene una marca temporal y `GetElapsedTime` calcula cuánto tiempo pasó desde la última revisión. Esto evita depender de fechas de calendario y permite avanzar el reloj manualmente durante los tests.

El refill considera únicamente períodos completos. La división calcula cuántos períodos transcurrieron y el módulo conserva la parte del tiempo que todavía no completa otro período. Ese resto se utiliza en la siguiente solicitud.

Ejemplo con `RefillPeriod = 1 segundo`:

- Después de 750 ms todavía no se agrega un token y esos 750 ms quedan acumulados.
- Si pasan otros 250 ms, se completa un período y se agregan los tokens correspondientes.

Si el bucket ya está lleno, no puede acumular tokens por encima de `Capacity`.

Cuando una solicitud es rechazada, `RetryAfter` se calcula restando el tiempo parcial acumulado al período completo. El middleware convierte ese `TimeSpan` a segundos HTTP.

`ManualTimeProvider` hace que los tests sean rápidos y deterministas. No es necesario utilizar `Thread.Sleep` ni depender de la velocidad de la máquina.

## 7. HTTP, configuración y manejo de errores

`RateLimitMiddleware` exige exactamente un valor no vacío en `X-Api-Key`. Un header ausente, vacío, compuesto por espacios o con múltiples valores produce `400 Bad Request` y corta el pipeline antes de consultar `TokenBucketRateLimiter`.

Con un identificador válido, el middleware consume un token y agrega:

- `X-RateLimit-Limit`.
- `X-RateLimit-Remaining`.

Si `RateLimitResult.IsAllowed` es `true`, ejecuta `await _next(context)`. El token ya fue consumido y no se devuelve aunque el endpoint posterior responda con un error.

Si no hay tokens, el middleware responde `429 Too Many Requests` sin llamar a `_next`. También agrega `Retry-After`. Se utiliza `Math.Ceiling` para expresar segundos enteros y evitar que el cliente reintente antes del próximo refill.

La configuración se obtiene mediante `IConfiguration` desde la sección `RateLimiter`. `GetValue<int?>` permite distinguir una clave ausente de un valor presente igual a cero.

`TokenBucketOptions` valida que `Capacity`, `TokensPerPeriod` y `RefillPeriod` sean mayores que cero. Las opciones se construyen antes de `builder.Build()`, por lo que una configuración ausente o inválida impide iniciar la aplicación (fail-fast).

La configuración es pequeña, se lee una sola vez y no necesita recarga dinámica. Por eso se usa directamente `IConfiguration` y no se agregó Options Pattern.

## 8. Estrategia de testing

La solución ejecuta 22 casos de test:

- 13 casos en `RateLimiter.UnitTests`.
- 9 casos en `RateLimiter.IntegrationTests`.

Los Unit Tests verifican:

- Solicitudes permitidas y bucket vacío.
- Refill después de uno o varios períodos.
- Capacidad máxima.
- Independencia entre clientes.
- Cálculo de `RetryAfter`.
- Validación de `TokenBucketOptions`.
- Comportamiento bajo concurrencia.

`ManualTimeProvider` permite avanzar el tiempo sin esperas reales. Los escenarios temporales pueden comprobar valores exactos y no dependen de `Thread.Sleep`.

Los Integration Tests utilizan `WebApplicationFactory<Program>` y `TestServer` para ejecutar el pipeline real de ASP.NET Core sin abrir un puerto de red. Verifican status codes, bodies, headers, independencia entre clientes, refill y validación de configuración durante el inicio.

`ConfigureTestServices` reemplaza `TokenBucketOptions`, `TimeProvider` y `TokenBucketRateLimiter` por instancias controladas para cada escenario. Los tests de configuración utilizan variables de entorno y restauran su valor en `finally` para no afectar otros casos.

## 9. Observabilidad

### Logging

`RateLimitMiddleware` utiliza logging estructurado mediante `ILogger<RateLimitMiddleware>`:

- Registra `Warning` cuando `X-Api-Key` no es válido.
- Registra `Information` cuando se supera el límite.
- No registra solicitudes permitidas para evitar ruido innecesario.

### Métricas

La aplicación registra un contador mediante `System.Diagnostics.Metrics`:

```text
Meter:   RateLimiter.Api
Counter: rate_limiter.requests
Tag:     rate_limiter.result
Values:  allowed | rejected | invalid
```

Las solicitudes permitidas se cuentan antes de llamar a `_next`, porque la métrica representa la decisión del Rate Limiter aunque el endpoint posterior falle.

`clientId` no se utiliza como tag. Incluir un valor diferente por cliente generaría una cantidad de series que crecería con el número de clientes y aumentaría el uso de recursos.

La aplicación genera métricas utilizando `System.Diagnostics.Metrics`, pero actualmente no existe un exporter externo configurado.

## 10. Trade-offs, limitaciones y evolución distribuida

### Implementación actual

`TokenBucketRateLimiter` se registra como Singleton, pero ese Singleton solo vive dentro del proceso. El lock mantiene coherente el estado dentro de esa instancia; otra instancia tendría sus propios buckets.

Las limitaciones actuales son:

- El estado vive en memoria.
- Los buckets se pierden al reiniciar el proceso.
- Varias instancias no comparten límites.
- Los buckets creados no expiran ni se eliminan.
- No existe persistencia externa.

Por estas limitaciones, la solución no se presenta como production-ready.

### Posible evolución distribuida

Una evolución posible sería:

```text
Load Balancer
      ↓
Múltiples instancias de RateLimiter.Api
      ↓
Redis compartido
      ↓
Operación Token Bucket atómica
```

Redis podría almacenar el estado compartido para que todas las instancias consulten el mismo bucket por cliente. El refill, la validación y el consumo deberían ejecutarse como una única operación para evitar que dos instancias entreguen el mismo token.

Este cambio agregaría I/O de red, latencia, una dependencia externa y nuevos modos de fallo. También obligaría a decidir si la aplicación debe permitir solicitudes cuando Redis no está disponible (fail-open) o rechazarlas para mantener el límite (fail-closed).

Redis no está implementado en la solución actual.

### Por qué no se implementó Redis

No se agregó Redis porque el alcance actual es de una sola instancia. Incorporarlo habría sumado infraestructura, I/O de red y nuevos tests sin resolver un requisito presente. Para este challenge era suficiente dejarlo documentado como una posible evolución.

## 11. Uso de IA y principios de ingeniería

Usé herramientas de IA para discutir la arquitectura, comparar alternativas, apoyar partes de la implementación y generar primeros borradores de algunos tests y comentarios. También las usé para repasar conceptos de concurrencia y ASP.NET Core.

Revisé los cambios, los simplifiqué cuando hizo falta y los validé con build, Unit Tests, Integration Tests y pruebas manuales. Repasé especialmente concurrencia, configuración, observabilidad e integración con ASP.NET Core para entender las decisiones tomadas.

Varias propuestas se descartaron porque no resolvían una necesidad actual:

- No se agregó `IRateLimiter` sin una segunda implementación o un beneficio concreto.
- Redis se documentó como posible evolución en lugar de implementarlo.
- No se agregó Options Pattern porque el uso directo de `IConfiguration` era suficiente para esta configuración pequeña y estática.
- No se creó una abstracción de métricas para un único contador.

El criterio fue mantener una solución pequeña y explicable, aplicando KISS/YAGNI y evitando overengineering o AI slop.
